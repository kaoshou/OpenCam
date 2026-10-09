// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private readonly Dictionary<Guid, ProjectFrameReply> _thumbnails = new();
    private readonly HashSet<Guid> _thumbnailFailures = new();
    private Guid[] _visibleThumbnailClips = [];
    public void SetVisibleThumbnailClips(IEnumerable<Guid> clips) =>
        _visibleThumbnailClips = clips.Distinct().Take(63).ToArray();
    public IReadOnlyDictionary<Guid, ProjectFrameReply> Thumbnails => _thumbnails;
    private void ClearThumbnails()
    {
        _thumbnails.Clear(); _thumbnailFailures.Clear(); _visibleThumbnailClips = [];
        OnPropertyChanged(nameof(Thumbnails));
    }
    public async Task PollThumbnailAsync()
    {
        // Preview and thumbnails share one media worker. Scrubbing has priority over background cards.
        if (IsPlayingPreview || IsClipListCompact || !CanEdit || State.ProjectId is null ||
            Interlocked.CompareExchange(ref _pollingPreview, 1, 0) != 0) return;
        try
        {
            var state = State;
            var priority = new[] { SelectedClip?.Id ?? Guid.Empty }.Concat(_visibleThumbnailClips)
                .Concat(TimelineClips.Select(c => c.ClipId)).Where(id => id != Guid.Empty).Distinct().Take(64).ToArray();
            var next = priority.FirstOrDefault(id => !_thumbnails.ContainsKey(id) && !_thumbnailFailures.Contains(id));
            var span = TimelineClips.FirstOrDefault(c => c.ClipId == next);
            if (span is null) return;
            var reply = await client.SendAsync("GetProjectFrame", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, TimelineTicks = span.StartTicks, ClipId = span.ClipId });
            if (!CanEdit || State.ProjectId != state.ProjectId || State.Revision != state.Revision ||
                State.ServerInstanceId != state.ServerInstanceId) return;
            if (!reply.Success || reply.Unconfirmed) { _thumbnailFailures.Add(span.ClipId); return; }
            if (reply.State.ProjectId != state.ProjectId || reply.State.Revision != state.Revision ||
                reply.State.ServerInstanceId != state.ServerInstanceId || reply.Frame is not { } frame ||
                frame.ClipId != span.ClipId || frame.Revision != state.Revision || frame.TimelineTicks != span.StartTicks) return;
            if (frame.Error is not null || frame.Rgba is { Length: not ProjectFrameReply.ByteCount })
            { _thumbnailFailures.Add(span.ClipId); return; }
            if (frame.Rgba is null) return;
            if (_thumbnails.Count >= 64)
            {
                var evict = _thumbnails.Keys.First(id => !priority.Contains(id));
                _thumbnails.Remove(evict);
            }
            _thumbnails[span.ClipId] = frame;
            OnPropertyChanged(nameof(Thumbnails));
        }
        catch { /* Derived images never change project save state. */ }
        finally { Interlocked.Exchange(ref _pollingPreview, 0); }
    }
}
