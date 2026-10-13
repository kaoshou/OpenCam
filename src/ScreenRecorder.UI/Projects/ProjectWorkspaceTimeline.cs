// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    public IReadOnlyList<ProjectTimelineClip> TimelineClips { get; private set; } = [];
    public long DurationTicks => TimelineClips.LastOrDefault()?.EndTicks ?? 0;
    [ObservableProperty] private long _playheadTicks;
    [ObservableProperty] private long? _rangeStartTicks;
    [ObservableProperty] private long? _rangeEndTicks;
    private ProjectTimelineClip? PlayheadClip => TimelineClips.FirstOrDefault(c =>
        PlayheadTicks > c.StartTicks && PlayheadTicks < c.EndTicks);
    public bool CanSplit => CanEditTimeline && PlayheadClip is not null;
    public bool CanDeleteRange => CanEditTimeline && RangeStartTicks is >= 0 && RangeEndTicks > RangeStartTicks && RangeEndTicks <= DurationTicks;
    public bool CanUngroup => CanEditTimeline && CanEditSelection && SelectedClip?.GroupId is not null;
    public bool CanMergePrevious => CanMerge(-1);
    public bool CanMergeNext => CanMerge(1);
    private bool CanMerge(int direction)
    {
        if (!CanEditTimeline || !CanEditSelection || SelectedClip is null) return false;
        var i = Clips.IndexOf(SelectedClip) + direction;
        return i >= 0 && i < Clips.Count && Clips[i].GroupId == SelectedClip.GroupId;
    }
    [RelayCommand] public Task MergeWithPreviousAsync() => MergeAdjacentAsync(true);
    [RelayCommand] public Task MergeWithNextAsync() => MergeAdjacentAsync(false);
    private async Task MergeAdjacentAsync(bool previous)
    {
        if (!(previous ? CanMergePrevious : CanMergeNext)) return;
        var selected = SelectedClip!;
        var mergedId = previous ? Clips[Clips.IndexOf(selected) - 1].Id : selected.Id;
        await ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Merge(selected.Id, previous) });
        if (Error is null) await SelectClipForPlaybackAsync(mergedId);
    }
    public async Task SelectClipForPlaybackAsync(Guid clipId)
    {
        if (!CanEditTimeline) return;
        var project = State.ProjectId; var revision = State.Revision;
        if (!await StopPreviewAsync() || State.ProjectId != project || State.Revision != revision) return;
        var clip = Clips.FirstOrDefault(c => c.Id == clipId);
        var span = TimelineClips.FirstOrDefault(c => c.ClipId == clipId);
        if (clip is null || span is null) return;
        SelectedClip = clip; Seek(span.StartTicks);
        await PollPreviewAsync();
    }
    public string TimelineTimeText => $"{FormatTime(PlayheadTicks)} / {FormatTime(DurationTicks)}";
    private static string FormatTime(long ticks) => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.fff");
    partial void OnPlayheadTicksChanged(long value)
    {
        UpdateSubtitleText();
        if (!_clockUpdate)
        {
            if (_scrubbing)
            {
                _failedPreviewTicks = null;
                OnPropertyChanged(nameof(PreviewStatus));
                NotifyTimeline();
                return;
            }
            ClearPreview();
            if (IsPlayingPreview)
            {
                var intent = ++_playbackIntent;
                if (value < DurationTicks) _ = StartPreviewAsync(value, intent);
                else _ = StopPreviewAsync();
            }
        }
        NotifyTimeline();
    }
    partial void OnRangeStartTicksChanged(long? value) => NotifyTimeline();
    partial void OnRangeEndTicksChanged(long? value) => NotifyTimeline();
    private void NotifyTimeline()
    {
        OnPropertyChanged(nameof(CanSplit));
        OnPropertyChanged(nameof(CanDeleteRange));
        OnPropertyChanged(nameof(TimelineTimeText));
        OnPropertyChanged(nameof(CanUngroup));
        OnPropertyChanged(nameof(CanMergePrevious));
        OnPropertyChanged(nameof(CanMergeNext));
    }
    private void PublishTimeline(IReadOnlyList<ProjectTimelineClip> spans)
    {
        var changed = !TimelineClips.SequenceEqual(spans);
        TimelineClips = spans;
        PlayheadTicks = Math.Clamp(PlayheadTicks, 0, DurationTicks);
        if (changed) { RangeStartTicks = null; RangeEndTicks = null; }
        OnPropertyChanged(nameof(TimelineClips));
        OnPropertyChanged(nameof(DurationTicks));
        NotifyTimeline();
    }
    public void Seek(long ticks)
    {
        if (CanEditTimeline) PlayheadTicks = Math.Clamp(ticks, 0, DurationTicks);
    }
    public void SetRangeStart(long ticks)
    {
        if (!CanEditTimeline) return;
        RangeEndTicks = null;
        RangeStartTicks = Math.Clamp(ticks, 0, DurationTicks);
        Error = null;
    }
    public void SetRangeEnd(long ticks)
    {
        if (!CanEditTimeline) return;
        var end = Math.Clamp(ticks, 0, DurationTicks);
        if (RangeStartTicks is not long start || end <= start)
        {
            RangeEndTicks = null;
            Error = Strings[RangeStartTicks is null ? "ProjectRangeStartRequired" : "ProjectRangeEndAfterStart"];
            return;
        }
        RangeEndTicks = end;
        Error = null;
    }
    [RelayCommand] public void MarkRangeStart() => SetRangeStart(PlayheadTicks);
    [RelayCommand] public void MarkRangeEnd() => SetRangeEnd(PlayheadTicks);
    [RelayCommand] public Task SplitAtPlayheadAsync() => !CanSplit ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.SplitAtTimeline(PlayheadClip!.ClipId, PlayheadTicks, Guid.NewGuid()) });
    [RelayCommand] public Task DeleteRangeAsync() => !CanDeleteRange ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.RemoveRange(RangeStartTicks!.Value, RangeEndTicks!.Value) });
    public Task MoveClipAsync(Guid clipId, Guid? beforeId) => !CanEditTimeline || !Clips.Any(c => c.Id == clipId) ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Move(clipId, beforeId) });
    public Task TrimEdgeAsync(Guid clipId, bool start, long deltaTicks) => !CanEditTimeline || !Clips.Any(c => c.Id == clipId) ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.TrimEdge(clipId, start, deltaTicks) });
    [RelayCommand] public Task GroupWithNextAsync()
    {
        if (!CanMoveLater) return Task.CompletedTask;
        var selected = Clips.IndexOf(SelectedClip!);
        var first = GroupStart(selected);
        var last = GroupEnd(GroupEnd(selected) + 1);
        return ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Group(Clips[first].Id, Clips[last].Id, Guid.NewGuid()) });
    }
    [RelayCommand] public Task GroupWithPreviousAsync()
    {
        if (!CanMoveEarlier) return Task.CompletedTask;
        var selected = Clips.IndexOf(SelectedClip!);
        var first = GroupStart(GroupStart(selected) - 1);
        var last = GroupEnd(selected);
        return ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Group(Clips[first].Id, Clips[last].Id, Guid.NewGuid()) });
    }
    [RelayCommand] public Task UngroupSelectedAsync() => !CanUngroup ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Ungroup(SelectedClip!.GroupId!.Value) });
}
