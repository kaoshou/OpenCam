// SPDX-License-Identifier: AGPL-3.0-or-later
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows.Audio;

/// <summary>Best-effort shared-mode tap; FFmpeg keeps owning the recording input.</summary>
public sealed class WindowsMicrophoneLevelObserver : IMicrophoneLevelObserver
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly IAudioDeviceService _devices;
    private readonly AudioLevelAccumulator _levels = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _endpoint;
    private WasapiCapture? _capture;
    private bool _running;
    private long _lastLevelUpdateMs;

    public WindowsMicrophoneLevelObserver(IAudioDeviceService devices) => _devices = devices;

    public async Task<bool> StartAsync(string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCore();
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            _enumerator = new MMDeviceEnumerator();
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            // A level meter is best-effort. DirectShow enumeration must never
            // hold up the already-started FFmpeg recording pipeline.
            var dshowDevices = await Task.Run(
                _devices.GetRecordingDevices, cancellationToken)
                .WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
            var endpointId = WindowsMicrophoneEndpointMatcher.Match(deviceId,
                dshowDevices,
                endpoints.Select(endpoint => (endpoint.ID, endpoint.FriendlyName)).ToArray());
            if (endpointId is null)
            {
                StopCore();
                return false;
            }

            _endpoint = endpoints.Single(endpoint => endpoint.ID == endpointId);
            _capture = new WasapiCapture(_endpoint);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            Interlocked.Exchange(ref _lastLevelUpdateMs, 0);
            _running = true;
            return true;
        }
        catch
        {
            StopCore();
            return false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCore();
        return Task.CompletedTask;
    }

    public AudioLevelSample? ReadLatestLevel(DateTimeOffset now) =>
        _running ? _levels.ReadFresh(now, TimeSpan.FromSeconds(1)) : null;

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        try
        {
            var format = _capture?.WaveFormat;
            if (format is null || args.BytesRecorded <= 0)
            {
                return;
            }
            if (!ShouldSampleLevel()) return;

            var pcm16 = format.BitsPerSample == 16 &&
                (format.Encoding == WaveFormatEncoding.Pcm ||
                 format is WaveFormatExtensible { SubFormat: var pcmSubtype } && pcmSubtype == PcmSubFormat);
            var float32 = format.BitsPerSample == 32 &&
                (format.Encoding == WaveFormatEncoding.IeeeFloat ||
                 format is WaveFormatExtensible { SubFormat: var floatSubtype } && floatSubtype == FloatSubFormat);
            if (pcm16)
            {
                _levels.PublishPcm16(args.Buffer.AsSpan(0, args.BytesRecorded), DateTimeOffset.UtcNow, format.Channels);
            }
            else if (float32)
            {
                var count = args.BytesRecorded / sizeof(float);
                var channels = Math.Max(1, format.Channels);
                var frameCount = count / channels;
                var stride = frameCount <= 16 ? 1 : Math.Max(1, 16 / channels);
                double sum = 0;
                double peak = 0;
                var sampled = 0;
                for (var frame = 0; frame < frameCount; frame += stride)
                {
                    for (var channel = 0; channel < channels; channel++)
                    {
                        var value = BitConverter.ToSingle(args.Buffer, (frame * channels + channel) * sizeof(float));
                        if (!float.IsFinite(value)) return;
                        var amplitude = Math.Min(1, Math.Abs((double)value));
                        sum += amplitude * amplitude;
                        peak = Math.Max(peak, amplitude);
                        sampled++;
                    }
                }
                if (sampled > 0)
                {
                    _levels.PublishNormalized(Normalize(Math.Sqrt(sum / sampled)), Normalize(peak), DateTimeOffset.UtcNow);
                }
            }
        }
        catch
        {
            // Observation cannot change the recorder's DirectShow capture path.
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        _running = false;
        _levels.Clear();
    }

    private static double Normalize(double amplitude) => amplitude <= 0
        ? 0
        : Math.Clamp((20 * Math.Log10(amplitude) + 60) / 60, 0, 1);

    private bool ShouldSampleLevel()
    {
        var now = Environment.TickCount64;
        while (true)
        {
            var last = Volatile.Read(ref _lastLevelUpdateMs);
            if (last != 0 && now - last < 200) return false;
            if (Interlocked.CompareExchange(ref _lastLevelUpdateMs, now, last) == last) return true;
        }
    }

    private void StopCore()
    {
        _running = false;
        _levels.Clear();
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }
        try { _endpoint?.Dispose(); } catch { }
        _endpoint = null;
        try { _enumerator?.Dispose(); } catch { }
        _enumerator = null;
    }

    public ValueTask DisposeAsync()
    {
        StopCore();
        return ValueTask.CompletedTask;
    }
}
