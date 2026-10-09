// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private int _pollingPreview;
    private long? _failedPreviewTicks;
    public ProjectFrameReply? PreviewFrame { get; private set; }
    public string PreviewStatus => IsChangingPreviewQuality ? Strings["PreviewChangingQuality"] : IsPlayingPreview ? Strings["ProjectPreviewPlaying"] : DurationTicks == 0 ? Strings["ProjectFrameEmpty"] : Strings[_failedPreviewTicks == PlayheadTicks
        ? "ProjectFrameUnavailable" : PreviewFrame is null ? "ProjectFrameLoading" : "ProjectStillPreview"];

    private void ClearPreview()
    {
        PreviewFrame = null;
        _failedPreviewTicks = null;
        OnPropertyChanged(nameof(PreviewFrame));
        OnPropertyChanged(nameof(PreviewResolutionText));
        OnPropertyChanged(nameof(PreviewStatus));
    }

    public async Task PollPreviewAsync()
    {
        if (IsPlayingPreview) { await PollPlaybackAsync(); return; }
        if (!CanEdit || State.ProjectId is null || DurationTicks == 0 ||
            PreviewFrame?.TimelineTicks == PlayheadTicks || _failedPreviewTicks == PlayheadTicks ||
            Interlocked.CompareExchange(ref _pollingPreview, 1, 0) != 0) return;
        var state = State;
        var ticks = PlayheadTicks;
        var qualityRequest = _qualityRequest;
        try
        {
            // Timeline end has no frame. Do not display a source frame outside the retained interval.
            if (ticks >= DurationTicks) { _failedPreviewTicks = ticks; return; }
            var reply = await client.SendAsync("GetProjectFrame", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, TimelineTicks = ticks, PreviewQuality = PreviewQuality });
            if (!CanEdit || State.ProjectId != state.ProjectId || State.Revision != state.Revision ||
                State.ServerInstanceId != state.ServerInstanceId || PlayheadTicks != ticks || qualityRequest != _qualityRequest) return;
            if (!reply.Success || reply.Unconfirmed)
            {
                _failedPreviewTicks = ticks;
                Error = reply.Error;
                StatusUnconfirmed = reply.Unconfirmed;
                NotifyState();
                return;
            }
            var frame = reply.Frame;
            if (reply.State.ProjectId != state.ProjectId || reply.State.Revision != state.Revision ||
                reply.State.ServerInstanceId != state.ServerInstanceId || frame is null ||
                frame.Revision != state.Revision || frame.TimelineTicks != ticks || !TimelineClips.Any(c => c.ClipId == frame.ClipId)) return;
            if (frame.Error is not null || (frame.Rgba is not null && !frame.HasValidPixels))
            { _failedPreviewTicks = ticks; return; }
            if (frame.Rgba is null) return;
            PreviewFrame = frame;
            OnPropertyChanged(nameof(PreviewFrame));
            OnPropertyChanged(nameof(PreviewResolutionText));
        }
        catch
        {
            if (State.ProjectId == state.ProjectId && State.Revision == state.Revision &&
                State.ServerInstanceId == state.ServerInstanceId && PlayheadTicks == ticks) _failedPreviewTicks = ticks;
        }
        finally
        {
            OnPropertyChanged(nameof(PreviewStatus));
            Interlocked.Exchange(ref _pollingPreview, 0);
        }
    }
}
