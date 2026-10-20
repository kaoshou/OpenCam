// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Preflight;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class PreflightAudioSession
{
    private PreflightTrialStorage? _storage;
    private PreflightAudioPlayback? _replay;
    private Stopwatch? _clock;
    private TimeSpan _duration;
    private volatile bool _trialReady;
    private double _trialSeconds;

    public async Task RecordTrialAsync(CancellationToken ct)
    {
        var before = ReadStatus(DateTimeOffset.UtcNow);
        if (_configuration is null || validate is null || before.ErrorCode is not null ||
            _phase is not (PreflightPhase.Checking or PreflightPhase.Reviewing) ||
            _phase == PreflightPhase.Checking && !before.AudioReady)
            throw new InvalidOperationException("Audio trial is unavailable.");
        _phase = PreflightPhase.TrialStarting;
        _trialReady = false; _clock = null; _trialSeconds = 0;
        try
        {
            await StopReplayAsync(ct);
            await StopCaptureAsync(ct);
            if (_error is not null) throw new IOException("Audio check did not stop cleanly.");
            if (_storage is not null) { await _storage.DisposeAsync(); _storage = null; }
            _storage = storage?.Invoke() ?? new PreflightTrialStorage();
            _capture = create();
            await _capture.StartAsync(_configuration, _storage.Writer, ct);
            lock (_audioGate) _monitor = true;
            _clock = Stopwatch.StartNew();
            _phase = PreflightPhase.TrialRecording;
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            _clock.Stop(); _trialSeconds = 5;
            _phase = PreflightPhase.TrialFinalizing;
            await StopCaptureAsync(ct);
            if (_error is not null) throw new IOException("Audio trial did not stop cleanly.");
            await _storage.SealAsync(ct);
            await using (var input = _storage.OpenRead()) _duration = await validate(input, ct);
            if (_duration.TotalSeconds is < 4.5 or > 60) throw new InvalidDataException("Invalid audio trial duration.");
            ct.ThrowIfCancellationRequested();
            _trialReady = true;
            _phase = PreflightPhase.Reviewing;
        }
        catch
        {
            _clock?.Stop();
            _error = "PreflightTrialFailed"; _phase = PreflightPhase.Failed;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await StopCaptureAsync(cleanup.Token);
        }
    }

    public async Task PlayTrialAsync(CancellationToken ct)
    {
        if (_phase != PreflightPhase.Reviewing || !_trialReady || _storage is null ||
            ReadStatus(DateTimeOffset.UtcNow).ErrorCode is not null || playback is null)
            throw new InvalidOperationException("No verified audio trial is available.");
        await StopReplayAsync(ct);
        _replay = playback(_storage, _duration);
        await _replay.PlayAsync(ct);
    }

    public async Task StopReplayAsync(CancellationToken ct)
    {
        if (_replay is null) return;
        await _replay.StopAsync(ct);
        _error ??= _replay.ErrorCode;
        _replay = null;
    }
}
