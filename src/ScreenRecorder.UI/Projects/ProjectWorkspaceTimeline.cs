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
    public string TimelineTimeText => $"{FormatTime(PlayheadTicks)} / {FormatTime(DurationTicks)}";
    private static string FormatTime(long ticks) => TimeSpan.FromTicks(ticks).ToString(@"hh\:mm\:ss\.fff");
    partial void OnPlayheadTicksChanged(long value) => NotifyTimeline();
    partial void OnRangeStartTicksChanged(long? value) => NotifyTimeline();
    partial void OnRangeEndTicksChanged(long? value) => NotifyTimeline();
    private void NotifyTimeline()
    {
        OnPropertyChanged(nameof(CanSplit));
        OnPropertyChanged(nameof(CanDeleteRange));
        OnPropertyChanged(nameof(TimelineTimeText));
        OnPropertyChanged(nameof(CanUngroup));
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
        if (CanEditTimeline) RangeStartTicks = Math.Clamp(ticks, 0, DurationTicks);
    }
    public void SetRangeEnd(long ticks)
    {
        if (CanEditTimeline) RangeEndTicks = Math.Clamp(ticks, 0, DurationTicks);
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
    [RelayCommand] public Task UngroupSelectedAsync() => !CanUngroup ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Ungroup(SelectedClip!.GroupId!.Value) });
}
