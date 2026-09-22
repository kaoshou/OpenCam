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
    private readonly IAudioDeviceService _devices;
    private readonly AudioLevelAccumulator _levels = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _endpoint;
    private WasapiCapture? _capture;
    private bool _running;

    public WindowsMicrophoneLevelObserver(IAudioDeviceService devices) => _devices = devices;

    public Task<bool> StartAsync(string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCore();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        try
        {
            _enumerator = new MMDeviceEnumerator();
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var endpointId = WindowsMicrophoneEndpointMatcher.Match(deviceId,
                _devices.GetRecordingDevices(),
                endpoints.Select(endpoint => (endpoint.ID, endpoint.FriendlyName)).ToArray());
            if (endpointId is null)
            {
                StopCore();
                return Task.FromResult(false);
            }

            _endpoint = endpoints.Single(endpoint => endpoint.ID == endpointId);
            _capture = new WasapiCapture(_endpoint);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _running = true;
            return Task.FromResult(true);
        }
        catch
        {
            StopCore();
            return Task.FromResult(false);
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

            if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                _levels.PublishPcm16(args.Buffer.AsSpan(0, args.BytesRecorded), DateTimeOffset.UtcNow);
            }
            else if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                var count = args.BytesRecorded / sizeof(float);
                var stride = count <= 16 ? 1 : 16;
                double sum = 0;
                double peak = 0;
                var sampled = 0;
                for (var index = 0; index < count; index += stride)
                {
                    var value = BitConverter.ToSingle(args.Buffer, index * sizeof(float));
                    if (!float.IsFinite(value)) return;
                    var amplitude = Math.Min(1, Math.Abs((double)value));
                    sum += amplitude * amplitude;
                    peak = Math.Max(peak, amplitude);
                    sampled++;
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
