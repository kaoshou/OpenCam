// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Recorder.Services;

public sealed class ProjectSegmentCommitter(IProjectSourceProbe probe)
{
    private readonly ProjectWriteJournal _journal = new();

    public async Task CommitAsync(IProjectHandle handle, RecordingSession session, Guid operationId, CancellationToken ct = default,
        Action<RecordingProject, bool>? onCommitted = null)
    {
        if (operationId == Guid.Empty || session.ProjectId != handle.Current.ProjectId ||
            session.CompletionPolicy != ProjectCompletionPolicy.KeepProjectSources ||
            session.State is not (RecordingState.Paused or RecordingState.Completed or RecordingState.Interrupted))
            throw new InvalidOperationException("Only finalized project segments can be committed.");
        foreach (var path in session.SegmentFilePaths)
        {
            var safe = SessionPathPolicy.SegmentPath(session.WorkingDirectory, path);
            var relative = Path.GetRelativePath(handle.ProjectDirectory, safe).Replace(Path.DirectorySeparatorChar, '/');
            if (handle.Current.Sources.Any(s => s.RelativePath == relative)) continue;
            // Stable per source, including retries made after a process restart.
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{handle.Current.ProjectId}/{relative}"))[..16]);
            var intent = (await _journal.ReadPendingAsync(handle, ct)).FirstOrDefault(i => i.SourceId == id)
                ?? new SegmentCommitIntent(id, id, session.SessionId, relative, handle.Current.Revision,
                    CaptureFps: session.Configuration.Fps);
            await _journal.WriteIntentAsync(handle, intent, ct);
            await CommitIntentAsync(handle, intent, ct, onCommitted);
        }
    }

    public async Task ReconcileAsync(IProjectHandle handle, CancellationToken ct = default)
    {
        foreach (var intent in await _journal.ReadPendingAsync(handle, ct))
            await CommitIntentAsync(handle, intent, ct);
    }

    private async Task CommitIntentAsync(IProjectHandle handle, SegmentCommitIntent intent, CancellationToken ct,
        Action<RecordingProject, bool>? onCommitted = null)
    {
        var existing = handle.Current.Sources.FirstOrDefault(s => s.Id == intent.SourceId);
        if (existing is not null)
        {
            if (existing.RelativePath != intent.RelativePath || existing.SessionId != intent.SessionId)
                throw new InvalidDataException("Journal does not match committed source.");
            await _journal.CompleteAsync(handle, intent.OperationId, ct);
            return;
        }
        using var source = ProjectPathPolicy.OpenSource(handle, intent.RelativePath);
        var size = source.Length;
        if (size == 0) throw new InvalidDataException($"Empty project source: {intent.RelativePath}");
        var info = await probe.ProbeAsync(source, ct);
        source.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct));
        if (source.Length != size) throw new IOException("Source changed during validation.");
        var media = new ProjectSource { Id = intent.SourceId, SessionId = intent.SessionId,
            RelativePath = intent.RelativePath, FileSize = size, Sha256 = hash, Timing = info.Timing,
            Width = info.Width, Height = info.Height, VideoCodec = info.VideoCodec, AudioCodec = info.AudioCodec };
        var current = handle.Current;
        var insertion = await new ProjectInsertionJournal().FindAsync(handle, intent.SessionId, ct);
        // Sources survive Undo. Their presence, not the current clip list, is the receipt.
        var firstInsertionSource = insertion is not null && !current.Sources.Any(s => s.SessionId == intent.SessionId);
        var next = current with {
            Revision = checked(current.Revision + 1), Sources = current.Sources.Add(media),
            Sessions = current.Sessions.Contains(intent.SessionId) ? current.Sessions : current.Sessions.Add(intent.SessionId),
            Clips = current.Clips.Add(new ProjectClip { Id = intent.SourceId, SourceId = intent.SourceId,
                InPts = info.Timing.StartPts, OutPts = checked(info.Timing.StartPts + info.Timing.DurationTs),
                Name = $"Clip {current.Clips.Length + 1}" })
        };
        if (firstInsertionSource)
            next = ProjectInsertion.Apply(current, insertion!.Anchor, media, insertion.OperationId);
        if (current.Sources.IsEmpty)
            next = next with { Canvas = new(info.Width, info.Height,
                intent.CaptureFps is { } fps ? new(fps, 1) : current.Canvas.Fps) };
        await handle.SaveAsync(next, current.Revision, ct);
        onCommitted?.Invoke(handle.Current, firstInsertionSource);
        await _journal.CompleteAsync(handle, intent.OperationId, ct);
    }
}
