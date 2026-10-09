// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Infrastructure.IPC;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private Guid? _playbackGeneration;
    private bool _clockUpdate;
    private long _playbackIntent;
    public bool IsPlayingPreview { get; private set; }
    public bool CanPreview => IsPlayingPreview || (CanChangePreviewOptions && DurationTicks > 0);
    public string PlaybackButtonText => Strings[IsPlayingPreview ? "ProjectPreviewPause" : "ProjectPreviewPlay"];

    private void NotifyPlayback()
    {
        OnPropertyChanged(nameof(IsPlayingPreview));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(PlaybackButtonText));
        OnPropertyChanged(nameof(PreviewStatus));
    }

    [RelayCommand]
    public async Task TogglePlaybackAsync()
    {
        if (IsPlayingPreview || IsChangingPreviewQuality) await StopPreviewAsync();
        else if (CanPreview) await StartPreviewAsync(PlayheadTicks >= DurationTicks ? 0 : PlayheadTicks, ++_playbackIntent);
    }

    private async Task StartPreviewAsync(long ticks, long intent)
    {
        await _playbackGate.WaitAsync();
        try { await StartPreviewCoreAsync(ticks, intent); }
        finally { NotifyPlayback(); _playbackGate.Release(); }
    }

    private async Task StartPreviewCoreAsync(long ticks, long intent)
    {
        try
        {
            if (intent != _playbackIntent || !CanPreview) return;
            var state = State;
            var reply = await client.SendAsync("PlayProjectPreview", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, TimelineTicks = ticks, OperationId = Guid.NewGuid(), PreviewQuality = PreviewQuality,
                PreviewMuted = PreviewMuted });
            if (intent != _playbackIntent) return; // Next queued command replaces or stops this generation.
            if (!reply.Success || reply.Unconfirmed || reply.Playback is null)
            { Error = reply.Error ?? "Preview status is unconfirmed."; StatusUnconfirmed = reply.Unconfirmed; return; }
            if (State.ProjectId != state.ProjectId || State.Revision != state.Revision || State.ServerInstanceId != state.ServerInstanceId) return;
            _playbackGeneration = reply.Playback.Generation;
            IsPlayingPreview = reply.Playback.Playing;
            _clockUpdate = true;
            try { PlayheadTicks = ticks; } finally { _clockUpdate = false; }
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; }
        finally { NotifyPlayback(); }
    }

    public async Task<bool> StopPreviewAsync()
    {
        ++_playbackIntent;
        await _playbackGate.WaitAsync();
        try { return await StopPreviewCoreAsync(); }
        finally { NotifyPlayback(); _playbackGate.Release(); }
    }

    private async Task<bool> StopPreviewCoreAsync()
    {
        try
        {
            if (State.ProjectId is null) return true;
            // Always send Stop, including when a Play reply was lost or superseded.
            var reply = await client.SendAsync("StopProjectPreview", new() { ProjectId = State.ProjectId,
                ExpectedRevision = State.Revision, PlaybackGeneration = _playbackGeneration, OperationId = Guid.NewGuid() });
            if (!reply.Success || reply.Unconfirmed)
            { Error = reply.Error; StatusUnconfirmed = true; return false; }
            if (reply.Playback is { } playback)
            {
                _clockUpdate = true;
                try { PlayheadTicks = Math.Clamp(playback.TimelineTicks, 0, DurationTicks); }
                finally { _clockUpdate = false; }
            }
            _playbackGeneration = null; IsPlayingPreview = false;
            return true;
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; return false; }
        finally { NotifyPlayback(); }
    }

    private async Task PollPlaybackAsync()
    {
        if (_playbackGeneration is not { } generation || Interlocked.CompareExchange(ref _pollingPreview, 1, 0) != 0) return;
        var state = State;
        var intent = _playbackIntent;
        try
        {
            var reply = await client.SendAsync("GetProjectFrame", new() { ProjectId = state.ProjectId,
                ExpectedRevision = state.Revision, PlaybackGeneration = generation });
            if (_playbackGeneration != generation || intent != _playbackIntent || State.ProjectId != state.ProjectId ||
                State.Revision != state.Revision || State.ServerInstanceId != state.ServerInstanceId) return;
            if (!reply.Success || reply.Unconfirmed || reply.Playback?.Generation != generation ||
                reply.State.ProjectId != state.ProjectId || reply.State.Revision != state.Revision || reply.State.ServerInstanceId != state.ServerInstanceId)
            { Error = reply.Error ?? "Preview connection lost."; await StopPreviewAsync(); return; }
            var playback = reply.Playback;
            _clockUpdate = true;
            try { PlayheadTicks = Math.Clamp(playback.TimelineTicks, 0, DurationTicks); }
            finally { _clockUpdate = false; }
            if (reply.Frame is { HasValidPixels: true } frame && frame.Revision == state.Revision &&
                TimelineClips.Any(c => c.ClipId == frame.ClipId && frame.TimelineTicks >= c.StartTicks && frame.TimelineTicks < c.EndTicks))
            { PreviewFrame = frame; OnPropertyChanged(nameof(PreviewFrame)); OnPropertyChanged(nameof(PreviewResolutionText)); }
            IsPlayingPreview = playback.Playing;
            if (!playback.Playing) { _playbackGeneration = null; if (playback.Error is not null) Error = playback.Error; }
        }
        catch (Exception ex) { Error = ex.Message; await StopPreviewAsync(); }
        finally { NotifyPlayback(); Interlocked.Exchange(ref _pollingPreview, 0); }
    }
}
