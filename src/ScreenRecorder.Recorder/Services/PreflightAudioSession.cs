// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class PreflightAudioSession(
    Func<IPreflightAudioCapture> create,
    Func<RecordingConfiguration, string>? audioIdentity = null,
    Func<PreflightTrialStorage>? storage = null,
    Func<Stream, CancellationToken, Task<TimeSpan>>? validate = null,
    Func<PreflightTrialStorage, TimeSpan, PreflightAudioPlayback>? playback = null) : IPreflightLiveSession, IPreflightTrialSession
{
    private readonly object _audioGate = new();
    private RecordingConfiguration? _configuration;
    private IPreflightAudioCapture? _capture;
    private string? _identity, _error;
    private volatile bool _identityInitialized;
    private volatile PreflightPhase _phase = PreflightPhase.Preparing;
    private bool _monitor;

    public async Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct)
    {
        if (_configuration is not null) throw new InvalidOperationException("Session already started.");
        _configuration = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(snapshot))!;
        try
        {
            _identity = audioIdentity?.Invoke(_configuration);
            // Status polling can run while native endpoint enumeration is in progress.
            // Publish the baseline before allowing comparisons against it.
            _identityInitialized = true;
            if (_configuration.AudioSource == AudioSourceType.None)
            {
                _phase = PreflightPhase.Checking;
                return;
            }
            _capture = create();
            await _capture.StartAsync(_configuration, null, ct);
            lock (_audioGate) _monitor = true;
            _phase = PreflightPhase.Checking;
        }
        catch
        {
            _error = "PreflightAudioUnavailable";
            _phase = PreflightPhase.Failed;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await StopCaptureAsync(cleanup.Token);
            throw;
        }
    }

    public PreflightStatus ReadStatus(DateTimeOffset now)
    {
        var config = _configuration;
        var mic = PreflightReadiness.Resolve(config?.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone, null, now);
        var sys = PreflightReadiness.Resolve(config?.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone, null, now);
        var ready = config?.AudioSource == AudioSourceType.None && _error is null;
        if (_identityInitialized && config is not null && audioIdentity is not null && _phase != PreflightPhase.Closed)
        {
            try
            {
                if (_identity != audioIdentity(config))
                {
                    if (_error is null) Serilog.Log.Warning("Preflight audio endpoint identity changed");
                    _error = "PreflightAudioUnavailable";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Cannot confirm preflight audio identity");
                _error = "PreflightAudioUnavailable";
            }
        }
        lock (_audioGate)
        {
            if (_monitor && _capture is { } capture)
            {
                if (_error is null && capture.ErrorCode is { } error)
                {
                    Serilog.Log.Warning("Preflight audio capture reported failure: Phase={Phase}, ErrorCode={ErrorCode}, Running={Running}",
                        _phase, error, capture.IsRunning);
                    _error = error;
                }
                ready = capture.IsRunning && _error is null;
                if (ready)
                {
                    mic = capture.ReadMicrophone(now);
                    sys = capture.ReadSystemAudio(now);
                }
            }
        }
        return PreflightStatus.Empty with
        {
            Phase = _phase, Microphone = mic, SystemAudio = sys,
            ErrorCode = _error ?? _replay?.ErrorCode, AudioReady = _phase == PreflightPhase.Checking && ready,
            HasTrial = _trialReady, Playing = _replay?.Playing == true,
            MixLevel = _monitor && _capture is { IsRunning: true } active ? active.ReadMix(now) : null,
            TrialSeconds = _clock?.IsRunning == true ? Math.Clamp(_clock.Elapsed.TotalSeconds, 0, 5) : _trialSeconds
        };
    }

    public ProjectFrameReply? ReadFrame() => null;

    private async Task StopCaptureAsync(CancellationToken ct)
    {
        IPreflightAudioCapture? capture;
        lock (_audioGate) { _monitor = false; capture = _capture; }
        if (capture is null) return;
        await capture.StopAsync(ct);
        _error ??= capture.ErrorCode;
        await capture.DisposeAsync();
        lock (_audioGate) _capture = null;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _phase = PreflightPhase.Stopping;
        await StopReplayAsync(ct);
        await StopCaptureAsync(ct);
        if (_storage is not null) { await _storage.DisposeAsync(); _storage = null; }
        _trialReady = false;
        _phase = PreflightPhase.Closed;
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopAsync(deadline.Token);
    }
}
