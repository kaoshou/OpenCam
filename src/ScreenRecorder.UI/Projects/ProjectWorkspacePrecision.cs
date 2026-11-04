// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.UI.Projects.Editor;
namespace ScreenRecorder.UI.Projects;
public sealed partial class ProjectWorkspaceViewModel
{
    [ObservableProperty] private bool _timelineSnapping = true;
    private long _precisionIntent;
    public string SelectionRangeText => RangeStartTicks is long start && RangeEndTicks is long end && end > start ?
        $"{FormatTime(start)} — {FormatTime(end)} · {FormatTime(end-start)}" : Strings["EditorRangeHint"];
    [RelayCommand] private Task PreviousFrameAsync() => StepPreviewFrameAsync(-1);
    [RelayCommand] private Task NextFrameAsync() => StepPreviewFrameAsync(1);
    public Task StepPreviewFrameAsync(int direction) => PrecisionSeekAsync(t =>
        EditorTimelineMath.StepFrame(t, direction, DurationTicks, State.Canvas?.Fps ?? new(30,1)));
    public Task SeekRelativeAsync(long deltaTicks) => PrecisionSeekAsync(t => (long)System.Numerics.BigInteger.Clamp(
        (System.Numerics.BigInteger)t + deltaTicks, 0, Math.Max(0, DurationTicks - 1)));
    private async Task PrecisionSeekAsync(Func<long,long> position)
    {
        if (!CanEditTimeline) return;
        var intent = ++_precisionIntent;
        var project = State.ProjectId; var revision = State.Revision; var server = State.ServerInstanceId;
        if (IsPlayingPreview && !await StopPreviewAsync()) return;
        if (intent != _precisionIntent || !CanEditTimeline || project != State.ProjectId || revision != State.Revision || server != State.ServerInstanceId) return;
        Seek(position(PlayheadTicks)); RequestPreviewRefresh();
    }
    public long SnapTimelinePosition(long ticks, long tolerance, bool bypass) => !TimelineSnapping || bypass ? ticks :
        EditorTimelineMath.Snap(ticks, TimelineClips.SelectMany(c => new[]{c.StartTicks,c.EndTicks})
            .Concat(Bookmarks.Where(b => b.TimelineTicks.HasValue).Select(b => b.TimelineTicks!.Value)), tolerance, DurationTicks);
}
