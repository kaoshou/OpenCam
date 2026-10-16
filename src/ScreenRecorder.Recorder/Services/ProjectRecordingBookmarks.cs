// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using System.Numerics;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    public async Task<(ProjectBookmarkPosition[] Items, int Count, long Revision)> ReadBookmarksAsync(int offset, int limit)
    {
        await _gate.WaitAsync();
        try
        {
            if (_handle is null || Current is null || offset < 0 || limit is < 1 or > 100)
                throw new InvalidOperationException("Invalid bookmark query.");
            var doc = await ProjectBookmarkStore.ReadAsync(_handle);
            var positions = new List<ProjectBookmarkPosition>();
            var projectBookmark = ProjectBookmarks.CreateProjector(Current);
            foreach (var b in doc.Bookmarks)
            {
                var mapped = projectBookmark(b);
                if (mapped.Count == 0) positions.Add(new(b, null, null)); else positions.AddRange(mapped);
                if (positions.Count > 20000) throw new InvalidDataException("Too many bookmark appearances in this edit.");
            }
            var ordered = positions.OrderBy(p => p.TimelineTicks ?? long.MaxValue).ThenBy(p => p.Bookmark.Id).ToArray();
            return (ordered.Skip(offset).Take(limit).ToArray(), ordered.Length, doc.Revision);
        }
        finally { _gate.Release(); }
    }

    public Task<ProjectCommandResult> AddBookmarkAsync(Guid operationId, string name, CancellationToken ct = default) =>
        Run(async () =>
        {
            if (_handle is null || _session is null || Mode != ProjectMode.Recording ||
                recorder.CurrentState != RecordingState.Recording || recorder.CurrentSession != _session)
                throw new InvalidOperationException("Bookmarks can only be added while recording.");
            // Capture time before asynchronous IO, using the recorder's active media clock.
            var ticks = recorder.GetBookmarkOffset(_session);
            var path = Path.GetRelativePath(_handle.ProjectDirectory, _session.WorkingFilePath).Replace(Path.DirectorySeparatorChar, '/');
            var doc = await ProjectBookmarkStore.ReadAsync(_handle, ct);
            var bookmark = new ProjectBookmark(operationId, _session.SessionId, path, ticks, $"{(string.IsNullOrWhiteSpace(name) ? "重點" : name.Trim())} {doc.Bookmarks.Length + 1}");
            await ProjectBookmarkStore.WriteAsync(_handle, doc with { Revision = checked(doc.Revision + 1), Bookmarks = doc.Bookmarks.Add(bookmark) }, doc.Revision, ct);
        }, operationId, "bookmark:" + name, ct);

    public Task<ProjectCommandResult> AddEditorBookmarkAsync(long timelineTicks, string name, long expectedRevision,
        long bookmarkRevision, Guid operationId, CancellationToken ct = default) => Run(async () =>
        {
            RequireNoExport();
            if (_handle is null || Current is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused) || HasRecoverableDraft)
                throw new InvalidOperationException("Pause or finish recording and resolve pending drafts before adding an editor bookmark.");
            CheckRevision(expectedRevision);
            ProjectValidation.ValidateName(name.Trim());
            var position = ProjectTimeline.Build(Current).Locate(timelineTicks)
                ?? throw new InvalidDataException("The bookmark must be inside the edited timeline.");
            var source = Current.Sources.Single(s => s.Id == position.SourceId);
            // Store source-relative media time, not a fragile absolute timeline position.
            // Round upward to retain the located PTS when the projector converts ticks back to PTS.
            var numerator = ((BigInteger)position.SourcePts - source.Timing.StartPts) * source.Timing.TimeBase.Numerator * TimeSpan.TicksPerSecond;
            var ticks = checked((long)((numerator + source.Timing.TimeBase.Denominator - 1) / source.Timing.TimeBase.Denominator));
            var doc = await ProjectBookmarkStore.ReadAsync(_handle, ct);
            if (doc.Revision != bookmarkRevision) throw new InvalidOperationException("Bookmark revision changed; refresh before adding.");
            var bookmark = new ProjectBookmark(operationId, source.SessionId, source.RelativePath, ticks, name.Trim());
            await ProjectBookmarkStore.WriteAsync(_handle, doc with { Revision = checked(doc.Revision + 1),
                Bookmarks = doc.Bookmarks.Add(bookmark) }, doc.Revision, ct);
        }, operationId, $"bookmark-at:{expectedRevision}:{bookmarkRevision}:{timelineTicks}:{name}", ct);

    public Task<ProjectCommandResult> ChangeBookmarkAsync(Guid id, string? name, long revision, Guid operationId, CancellationToken ct = default) =>
        Run(async () =>
        {
            RequireNoExport();
            if (_handle is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused))
                throw new InvalidOperationException("Pause or finish before editing bookmarks.");
            var doc = await ProjectBookmarkStore.ReadAsync(_handle, ct);
            if (revision != doc.Revision) throw new InvalidOperationException("Bookmark revision changed; refresh before editing.");
            var index = -1;
            for (var i = 0; i < doc.Bookmarks.Length; i++) if (doc.Bookmarks[i].Id == id) { index = i; break; }
            if (index < 0) throw new InvalidDataException("Bookmark not found.");
            var bookmarks = name is null ? doc.Bookmarks.RemoveAt(index) : doc.Bookmarks.SetItem(index, doc.Bookmarks[index] with { Name = name.Trim() });
            await ProjectBookmarkStore.WriteAsync(_handle, doc with { Revision = checked(doc.Revision + 1), Bookmarks = bookmarks }, revision, ct);
        }, operationId, $"bookmark-edit:{id}:{revision}:{name is null}:{name}", ct);
}
