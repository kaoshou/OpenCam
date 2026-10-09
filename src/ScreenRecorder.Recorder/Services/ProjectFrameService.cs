// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Recorder.Services;

/// <summary>One still decoder, bounded memory, latest-position wins. Caller serializes queries with capture start.</summary>
public sealed class ProjectFrameService(ProjectRecordingCoordinator coordinator, IProjectMediaProcess process) : IAsyncDisposable
{
    private (Guid Project, long Revision, long Ticks) _key;
    private Task<ProjectFrameReply>? _running;
    private CancellationTokenSource? _cancel;
    private bool _superseded;

    public async Task<ProjectFrameReply> QueryAsync(Guid projectId, long revision, long ticks)
    {
        if (coordinator.Current?.ProjectId != projectId || coordinator.Current.Revision != revision ||
            coordinator.Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Preview snapshot is no longer current.");
        var timeline = ProjectTimeline.Build(coordinator.Current);
        var position = timeline.Locate(ticks) ?? throw new InvalidDataException("Preview position is outside the timeline.");
        var key = (projectId, revision, ticks);
        if (_running is not null)
        {
            if (!_running.IsCompleted)
            {
                if (_key != key) { _superseded = true; _cancel!.Cancel(); }
                return new(revision, ticks, position.ClipId, null);
            }
            if (_key == key && !_superseded) return await _running;
            await _running;
            _cancel!.Dispose();
        }
        var lease = await coordinator.OpenMediaSourceAsync(projectId, revision, position.ClipId, default);
        _key = key;
        _superseded = false;
        _cancel = new(TimeSpan.FromSeconds(15));
        var token = _cancel.Token;
        _running = Task.Run(async () =>
        {
            using (lease)
            {
                try
                {
                    if (lease.Stream.Length != lease.Source.FileSize ||
                        !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(lease.Stream, token)),
                            lease.Source.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Original source fingerprint changed.");
                    lease.Stream.Position = 0;
                    var job = ProjectMediaJob.ExtractFrame(position.SourcePts, lease.Source.Timing.TimeBase,
                        ProjectFrameReply.Width, ProjectFrameReply.Height, lease.Clip.OutPts);
                    var result = await process.RunAsync(job, [lease.Stream], null, token);
                    if (result.Output.Length != ProjectFrameReply.ByteCount) throw new InvalidDataException("Invalid frame size.");
                    return new ProjectFrameReply(revision, ticks, position.ClipId, result.Output);
                }
                catch (Exception) { return new(revision, ticks, position.ClipId, null, "Preview could not be decoded. Original media is unchanged."); }
            }
        });
        return new(revision, ticks, position.ClipId, null);
    }

    public async Task SuspendAsync()
    {
        _cancel?.Cancel();
        if (_running is not null) await _running;
        _cancel?.Dispose();
        _cancel = null;
        _running = null;
    }
    public async ValueTask DisposeAsync() => await SuspendAsync();
}
