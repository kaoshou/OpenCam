// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Preflight;

namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightLiveSession(Func<PreflightCaptureResources> create, IDisplayService displays,
    string executable, Func<RecordingConfiguration, string>? audioIdentity = null) : IPreflightLiveSession
{
    private PreflightCaptureResources? _resources;
    private PreflightPreviewPipe? _preview;
    private RecordingConfiguration? _configuration;
    private CaptureRegion _bounds = CaptureRegion.Empty;
    private string _displayName = "", _displaySignature = "", _audioIdentity = "";
    private string? _error;
    private DateTimeOffset _started;
    private readonly object _captureState = new();
    private volatile PreflightPhase _capturePhase = PreflightPhase.Preparing;
    private int _missingFrameReported;
    public HardwareEncoderType ActiveEncoder => _resources?.Engine.ActiveEncoder ?? throw new InvalidOperationException("Check has not started.");
    public CaptureSelection? ActiveCapture => _resources?.Engine.ActiveCapture;

    public async Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _configuration = snapshot;
        var monitors = displays.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Index == snapshot.MonitorIndex)
            ?? throw new InvalidOperationException("PreflightDisplayUnavailable");
        _bounds = snapshot.CaptureSource == CaptureSourceType.CustomRegion ? snapshot.Region : monitor.Bounds;
        if (_bounds.Width < 2 || _bounds.Height < 2 || _bounds.Width > 16384 || _bounds.Height > 16384 ||
            _bounds.X < monitor.Bounds.X || _bounds.Y < monitor.Bounds.Y ||
            (long)_bounds.X + _bounds.Width > (long)monitor.Bounds.X + monitor.Bounds.Width ||
            (long)_bounds.Y + _bounds.Height > (long)monitor.Bounds.Y + monitor.Bounds.Height)
            throw new InvalidOperationException("PreflightInvalidBounds");
        _bounds = _bounds with { Width = _bounds.Width / 2 * 2, Height = _bounds.Height / 2 * 2 };
        _displayName = monitor.DeviceName;
        _displaySignature = JsonSerializer.Serialize(monitors);
        _audioIdentity = audioIdentity?.Invoke(snapshot) ?? "";
        _resources = create();
        _resources.Engine.EngineErrorOccurred += EngineError;
        _resources.Engine.EngineWarningOccurred += EngineWarning;
        _resources.Engine.AudioDeviceLost += AudioLost;
        if (_resources.Engine is not IBoundRecordingOutput output) throw new NotSupportedException("Preflight streaming output unavailable.");
        _preview = new(executable);
        output.OutputStream = _preview.Input;
        _started = DateTimeOffset.UtcNow;
        await _resources.Engine.StartRecordingAsync("preflight-check.mkv", snapshot, _bounds, ct);
        lock (_captureState)
        {
            _started = DateTimeOffset.UtcNow;
            _capturePhase = PreflightPhase.Checking;
        }
    }
    private void EngineError(object? sender, string error) => Volatile.Write(ref _error, "PreflightCaptureFailed");
    private void EngineWarning(object? sender, string warning) => Volatile.Write(ref _error, "PreflightAudioUnavailable");
    private void AudioLost(object? sender, EventArgs args) => Volatile.Write(ref _error, "PreflightAudioUnavailable");

    public PreflightStatus ReadStatus(DateTimeOffset now)
    {
        var phase = _capturePhase;
        if (_configuration is not { } configuration || _resources is not { } resources)
            return PreflightStatus.Empty with { Phase = PreflightPhase.Preparing };
        var error = Volatile.Read(ref _error);
        // The UI polls while native initialization is still awaiting its first frame.
        // Not running yet is not an unexpected capture exit, and must not poison
        // every subsequent frame with a sticky failure.
        if (phase == PreflightPhase.Preparing)
            return PreflightStatus.Empty with { Phase = phase, Bounds = _bounds, DisplayName = _displayName,
                Microphone = PreflightReadiness.Resolve(configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone, null, now),
                SystemAudio = PreflightReadiness.Resolve(configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone, null, now),
                ErrorCode = error };
        try
        {
            if (_displaySignature != JsonSerializer.Serialize(displays.GetMonitors()) ||
                _audioIdentity != (audioIdentity?.Invoke(configuration) ?? "")) error = "PreflightDeviceChanged";
        }
        catch { error = "PreflightDeviceChanged"; }
        (bool Running, long Frames, double Seconds, bool DecoderExited)? missingFrame = null;
        lock (_captureState)
        {
            // Serialize the health snapshot with intentional shutdown. Otherwise
            // a poll could observe Checking, then the engine after Stop changed it.
            phase = _capturePhase;
            if (phase == PreflightPhase.Checking)
            {
                var running = resources.Engine.IsRunning;
                var exited = _preview?.HasExited == true;
                if (!running || exited) error ??= "PreflightCaptureFailed";
                if (_preview is { LatestFrame: null } && now - _started > TimeSpan.FromSeconds(5) &&
                    Interlocked.Exchange(ref _missingFrameReported, 1) == 0)
                    missingFrame = (running, resources.Engine.CurrentFramesRecorded, resources.Engine.CurrentRecordedTime.TotalSeconds, exited);
            }
        }
        if (missingFrame is { } missing)
            Serilog.Log.Warning("Preflight preview has no frame: EngineRunning={EngineRunning}, CapturedFrames={CapturedFrames}, RecordedSeconds={RecordedSeconds}, DecoderExited={DecoderExited}, ErrorCode={ErrorCode}",
                missing.Running, missing.Frames, missing.Seconds, missing.DecoderExited, error);
        if (error is not null) Volatile.Write(ref _error, error); // invalidation is sticky until a new check
        var levels = (resources.Engine as IRecordingAudioLevelSource)?.ReadInputLevels(now) ?? default;
        var microphone = PreflightReadiness.Resolve(configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone,
            levels.Microphone, now);
        var system = PreflightReadiness.Resolve(configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone,
            levels.SystemAudio, now);
        if (now - _started > TimeSpan.FromSeconds(2))
        {
            if (microphone.State == PreflightSourceState.Waiting) microphone = new(PreflightSourceState.Unavailable);
            if (system.State == PreflightSourceState.Waiting) system = new(PreflightSourceState.Unavailable);
        }
        return PreflightStatus.Empty with { Phase = phase, Bounds = _bounds, DisplayName = _displayName,
            Microphone = microphone, SystemAudio = system, HasFrame = phase == PreflightPhase.Checking && error is null && _preview?.LatestFrame is not null, ErrorCode = error };
    }
    public ProjectFrameReply? ReadFrame() => ReadStatus(DateTimeOffset.UtcNow).HasFrame && _preview?.LatestFrame is { } pixels
        ? new(0, 0, Guid.Empty, pixels) { PixelWidth = PreflightPreviewPipe.Width, PixelHeight = PreflightPreviewPipe.Height } : null;

    public async Task StopAsync(CancellationToken ct)
    {
        // Publish intentional shutdown before the engine changes IsRunning.
        lock (_captureState) _capturePhase = PreflightPhase.Stopping;
        if (_resources is { } resources)
        {
            await resources.Engine.StopRecordingAsync(ct);
            if (resources.Engine.IsRunning) throw new IOException("Preflight stop is unconfirmed.");
            await resources.StopSourcesAsync(ct);
        }
        if (_preview is { } preview)
        {
            await preview.DisposeAsync();
            _preview = null;
        }
        _capturePhase = PreflightPhase.Closed;
    }
    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopAsync(stop.Token);
        if (_resources is { } resources)
        {
            resources.Engine.EngineErrorOccurred -= EngineError;
            resources.Engine.EngineWarningOccurred -= EngineWarning;
            resources.Engine.AudioDeviceLost -= AudioLost;
            await resources.DisposeAsync();
            _resources = null;
        }
    }
}
