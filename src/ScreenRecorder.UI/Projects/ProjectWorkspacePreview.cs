// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private int _pollingPreview;
    private long? _failedPreviewTicks;
    private long _previewEpoch, _scrubSequence;
    private bool _scrubbing, _resumeAfterScrub;
    private Task<bool>? _scrubStop;
    private long _scrubPlaybackIntent;
    public bool IsScrubbingPreview => _scrubbing;

    public void BeginPreviewScrub()
    {
        if (_scrubbing || !CanEditTimeline) return;
        _scrubbing = true;
        ++_scrubSequence;
        _resumeAfterScrub = IsPlayingPreview;
        _scrubStop = IsPlayingPreview ? StopPreviewAsync() : Task.FromResult(true);
        _scrubPlaybackIntent = _playbackIntent;
    }

    public async Task EndPreviewScrubAsync()
    {
        if (!_scrubbing) return;
        var sequence = _scrubSequence;
        var stopped = await (_scrubStop ?? Task.FromResult(true));
        if (sequence != _scrubSequence || !_scrubbing) return;
        _scrubbing = false;
        var resume = stopped && _resumeAfterScrub && _playbackIntent == _scrubPlaybackIntent && CanEditTimeline;
        _resumeAfterScrub = false;
        _scrubStop = null;
        _failedPreviewTicks = null;
        if (PlayheadTicks >= DurationTicks) { ClearPreview(); return; }
        if (resume) await StartPreviewAsync(PlayheadTicks, ++_playbackIntent);
        OnPropertyChanged(nameof(PreviewStatus));
    }
    public ProjectFrameReply? PreviewFrame { get; private set; }
    public string PreviewStatus => IsChangingPreviewQuality ? Strings["PreviewChangingQuality"] : IsPlayingPreview ? Strings["ProjectPreviewPlaying"] : DurationTicks == 0 ? Strings["ProjectFrameEmpty"] : Strings[_failedPreviewTicks == PlayheadTicks
        ? "ProjectFrameUnavailable" : PreviewFrame is null ? "ProjectFrameLoading" : "ProjectStillPreview"];

    private void ClearPreview()
    {
        ++_previewEpoch;
        ++_scrubSequence;
        _scrubbing = false;
        _resumeAfterScrub = false;
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
        var epoch = _previewEpoch;
        var intent = _playbackIntent;
        try
        {
            // Timeline end has no frame. Do not display a source frame outside the retained interval.
            if (ticks >= DurationTicks) { _failedPreviewTicks = ticks; return; }
            var reply = await client.SendAsync("GetProjectFrame", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, TimelineTicks = ticks, PreviewQuality = PreviewQuality });
            if (!CanEdit || State.ProjectId != state.ProjectId || State.Revision != state.Revision ||
                State.ServerInstanceId != state.ServerInstanceId || (PlayheadTicks != ticks && !_scrubbing) ||
                qualityRequest != _qualityRequest || epoch != _previewEpoch || intent != _playbackIntent || IsPlayingPreview) return;
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
                frame.Revision != state.Revision || frame.TimelineTicks != ticks ||
                !TimelineClips.Any(c => c.ClipId == frame.ClipId && ticks >= c.StartTicks && ticks < c.EndTicks)) return;
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
