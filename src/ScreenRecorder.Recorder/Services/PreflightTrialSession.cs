// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Probe;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Recorder.Services;

public interface IPreflightTrialSession
{
    Task RecordTrialAsync(CancellationToken ct);
    Task PlayTrialAsync(CancellationToken ct);
    Task StopReplayAsync(CancellationToken ct);
}

public sealed class PreflightTrialSession(PreflightLiveSession live,
    Func<HardwareEncoderType?, CaptureSelection?, PreflightCaptureResources> create,
    Func<PreflightTrialStorage> storage, ProjectPreviewDecoder playback) : IPreflightLiveSession, IPreflightTrialSession
{
    private RecordingConfiguration? _configuration;
    private PreflightCaptureResources? _capture;
    private PreflightTrialStorage? _storage;
    private RecordingProject? _trial;
    private ProjectFrameReply? _frame;
    private PreflightTrialPlayback? _replay;
    private volatile PreflightPhase _phase = PreflightPhase.Preparing;
    private string? _error;
    private DateTimeOffset? _trialStarted;
    private double _seconds;
    private readonly object _audioGate = new();
    private bool _monitorTrialAudio;
    public async Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct)
    {
        _configuration = snapshot;
        await live.StartAsync(snapshot, ct);
        _phase = PreflightPhase.Checking;
    }
    public PreflightStatus ReadStatus(DateTimeOffset now)
    {
        var status = live.ReadStatus(now);
        if (_phase == PreflightPhase.Checking) return status;
        // Closed/replayed media is not evidence of a fresh input signal.
        PreflightSourceResult Stopped(PreflightSourceResult source) => source.State == PreflightSourceState.Disabled
            ? source : new(PreflightSourceState.Waiting);
        var microphone = Stopped(status.Microphone);
        var system = Stopped(status.SystemAudio);
        lock (_audioGate)
        {
            if (_phase == PreflightPhase.TrialRecording && _monitorTrialAudio && _configuration is { } config)
            {
                var levels = (_capture?.Engine as IRecordingAudioLevelSource)?.ReadInputLevels(now) ?? default;
                microphone = PreflightReadiness.Resolve(config.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone, levels.Microphone, now);
                system = PreflightReadiness.Resolve(config.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone, levels.SystemAudio, now);
            }
        }
        return status with { Phase = _phase, Microphone = microphone, SystemAudio = system,
            HasFrame = _frame is not null, HasTrial = _trial is not null,
            TrialSeconds = _trialStarted is { } start ? Math.Clamp((now - start).TotalSeconds, 0, 5) : _seconds,
            Playing = _replay?.Playing == true, ErrorCode = _error ?? status.ErrorCode ?? _replay?.ErrorCode };
    }
    public ProjectFrameReply? ReadFrame() => ReadStatus(DateTimeOffset.UtcNow).ErrorCode is not null ? null :
        _phase == PreflightPhase.Checking ? live.ReadFrame() : Volatile.Read(ref _frame);
    public async Task RecordTrialAsync(CancellationToken ct)
    {
        var before = live.ReadStatus(DateTimeOffset.UtcNow);
        if (_configuration is null || before.ErrorCode is not null ||
            (_phase != PreflightPhase.Checking && _phase != PreflightPhase.Reviewing))
            throw new InvalidOperationException("Preflight is not ready for a trial.");
        if (_phase == PreflightPhase.Checking && !before.HasFrame) throw new InvalidOperationException("Check preview is not ready.");
        await StopReplayAsync(ct);
        await live.StopAsync(ct);
        if (_storage is not null) { await _storage.DisposeAsync(); _storage = null; }
        _trial = null; _frame = null; _seconds = 0; _error = null;
        _phase = PreflightPhase.TrialRecording;
        _capture = create(live.ActiveEncoder, live.ActiveCapture);
        _capture.Engine.EngineErrorOccurred += CaptureError;
        _capture.Engine.EngineWarningOccurred += CaptureError;
        _capture.Engine.AudioDeviceLost += AudioLost;
        try
        {
            _storage = storage();
            if (_capture.Engine is not IBoundRecordingOutput output) throw new NotSupportedException("Trial streaming unavailable.");
            output.OutputStream = _storage.Writer;
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(40));
                await _capture.Engine.StartRecordingAsync("preflight-trial.mkv", _configuration, before.Bounds, startup.Token);
            }
            lock (_audioGate) _monitorTrialAudio = true;
            _trialStarted = DateTimeOffset.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            await StopCaptureAsync();
            _trialStarted = null;
            if (_error is not null) throw new IOException(_error);
            await _storage.SealAsync(ct);
            using var file = _storage.OpenRead();
            var info = await new ProjectSourceProbe().ProbeAsync(file, ct);
            var mediaSeconds = (double)info.Timing.DurationTs * info.Timing.TimeBase.Numerator / info.Timing.TimeBase.Denominator;
            if (mediaSeconds is < 4.5 or > 60 ||
                (_configuration.AudioSource != AudioSourceType.None && info.AudioCodec is null))
                throw new InvalidDataException($"Trial is incomplete or missing enabled audio: seconds={mediaSeconds:R}, audio={info.AudioCodec ?? "none"}, requested={_configuration.AudioSource}.");
            // The bound MKV includes encoder startup buffering. Review only the final five seconds,
            // recorded after startup acknowledgement; no startup frames enter the replay recipe.
            var reviewPts = Math.Min(info.Timing.DurationTs,
                checked(5 * info.Timing.TimeBase.Denominator / info.Timing.TimeBase.Numerator));
            _seconds = (double)reviewPts * info.Timing.TimeBase.Numerator / info.Timing.TimeBase.Denominator;
            var id = Guid.NewGuid();
            var source = new ProjectSource { Id = id, SessionId = "preflight", RelativePath = "trial.mkv", FileSize = _storage.Length,
                Sha256 = _storage.Sha256, Timing = info.Timing, Width = info.Width, Height = info.Height,
                VideoCodec = info.VideoCodec, AudioCodec = info.AudioCodec };
            // Decoder recipe only: never persisted or registered as a project/session.
            _trial = new() { ProjectId = Guid.NewGuid(), Name = "Preflight", Sessions = ["preflight"], Canvas = new(info.Width, info.Height, new(_configuration.Fps, 1)),
                Sources = [source], Clips = [new() { Id = id, SourceId = id, Name = "Trial", InPts = checked(info.Timing.StartPts + info.Timing.DurationTs - reviewPts),
                    OutPts = checked(info.Timing.StartPts + info.Timing.DurationTs) }] };
            _phase = PreflightPhase.Reviewing;
        }
        catch { _phase = PreflightPhase.Failed; _error ??= "PreflightTrialFailed"; throw; }
        finally { _trialStarted = null; await StopCaptureAsync(); }
    }
    private void CaptureError(object? sender, string error) => Volatile.Write(ref _error, "PreflightTrialCaptureFailed");
    private void AudioLost(object? sender, EventArgs args) => Volatile.Write(ref _error, "PreflightAudioUnavailable");
    private async Task StopCaptureAsync()
    {
        // Finish any sample read before stopping/disposal; do not hold the lock across awaits.
        lock (_audioGate) _monitorTrialAudio = false;
        if (_capture is not { } capture) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await capture.Engine.StopRecordingAsync(deadline.Token);
        if (capture.Engine.IsRunning) throw new IOException("Trial stop is unconfirmed.");
        await capture.StopSourcesAsync(deadline.Token);
        await capture.DisposeAsync();
        capture.Engine.EngineErrorOccurred -= CaptureError;
        capture.Engine.EngineWarningOccurred -= CaptureError;
        capture.Engine.AudioDeviceLost -= AudioLost;
        _capture = null;
    }
    public async Task PlayTrialAsync(CancellationToken ct)
    {
        if (_trial is null || _storage is null || _phase != PreflightPhase.Reviewing || ReadStatus(DateTimeOffset.UtcNow).ErrorCode is not null)
            throw new InvalidOperationException("No valid closed trial.");
        await StopReplayAsync(ct);
        _replay = new(playback, _trial, _storage, pixels => Volatile.Write(ref _frame,
            new(0, pixels.TimelineTicks, Guid.Empty, pixels.Rgba) { PixelWidth = 960, PixelHeight = 540 }));
        _replay.Start(ct);
    }
    public async Task StopReplayAsync(CancellationToken ct)
    { if (_replay is { } replay) { await replay.StopAsync(ct); _replay = null; } }
    public async Task StopAsync(CancellationToken ct)
    { _phase = PreflightPhase.Stopping; await StopReplayAsync(ct); await live.StopAsync(ct); await StopCaptureAsync(); }
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopAsync(deadline.Token);
        await live.DisposeAsync();
        if (_storage is not null) { await _storage.DisposeAsync(); _storage = null; }
        _phase = PreflightPhase.Closed;
    }
}
