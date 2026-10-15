// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightTrialPlayback(ProjectPreviewDecoder decoder, RecordingProject recipe,
    PreflightTrialStorage storage, Action<ProjectPreviewFrame> publish)
{
    private CancellationTokenSource? _cancellation;
    private Task? _work;
    private string? _error;
    public bool Playing => _work is { IsCompleted: false };
    public string? ErrorCode => Volatile.Read(ref _error);
    public void Start(CancellationToken ct)
    {
        if (_work is not null) throw new InvalidOperationException("Replay already started.");
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _work = RunAsync(_cancellation.Token);
    }
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await decoder.PlayConfiguredAsync(recipe, 0, new(new(960, 540)),
                (_, cancel) => { cancel.ThrowIfCancellationRequested(); return Task.FromResult(storage.OpenRead()); },
                frame => { ct.ThrowIfCancellationRequested(); publish(frame); return Task.CompletedTask; }, _ => { }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { Volatile.Write(ref _error, "PreflightReplayFailed"); throw; }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        _cancellation?.Cancel();
        if (_work is not null) await _work.WaitAsync(TimeSpan.FromSeconds(10), ct);
        _cancellation?.Dispose();
    }
}
