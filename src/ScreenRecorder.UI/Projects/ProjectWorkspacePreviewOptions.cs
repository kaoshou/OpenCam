// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using CommunityToolkit.Mvvm.Input;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    public ProjectPreviewQuality PreviewQuality { get; private set; } = ProjectPreviewQuality.P720;
    public bool IsChangingPreviewQuality { get; private set; }
    private long _qualityRequest;
    private bool _qualityResume;
    public bool CanChangePreviewOptions => !IsBusy && !StatusUnconfirmed && !IsExporting && !HasPropertyDraft &&
        State.ProjectId is not null && State.Mode is ProjectMode.Ready or ProjectMode.Paused;
    public bool CanUseOriginalPreview => State.Canvas is { } canvas && (long)canvas.Width * canvas.Height * 4 <= 64 * 1024 * 1024;
    public string PreviewResolutionText => PreviewFrame is { } frame ? $"{frame.PixelWidth} × {frame.PixelHeight}" : "";
    public bool PreviewMuted { get; private set; }
    public string PreviewSoundText => Strings[PreviewMuted ? "PreviewSoundOff" : "PreviewSoundOn"];
    public string PreviewSoundIcon => PreviewMuted ? "sound-off" : "sound-on";

    [RelayCommand]
    public Task TogglePreviewSoundAsync() => SetPreviewMutedAsync(!PreviewMuted);

    public async Task SetPreviewMutedAsync(bool muted)
    {
        if (!CanChangePreviewOptions) return;
        await _playbackGate.WaitAsync();
        try
        {
            if (!CanChangePreviewOptions) return;
            if (IsPlayingPreview)
            {
                var generation = _playbackGeneration;
                var reply = await client.SendAsync("SetProjectPreviewMuted", new() { ProjectId = State.ProjectId,
                    ExpectedRevision = State.Revision, PlaybackGeneration = generation, PreviewMuted = muted, OperationId = Guid.NewGuid() });
                if (!reply.Success || reply.Unconfirmed || reply.Playback?.Generation != generation || reply.Playback?.Muted != muted)
                { Error = reply.Error ?? Strings["PreviewSoundFailed"]; StatusUnconfirmed = reply.Unconfirmed; return; }
            }
            PreviewMuted = muted;
            OnPropertyChanged(nameof(PreviewMuted)); OnPropertyChanged(nameof(PreviewSoundText)); OnPropertyChanged(nameof(PreviewSoundIcon));
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; }
        finally { _playbackGate.Release(); }
    }

    public async Task SetPreviewQualityAsync(ProjectPreviewQuality quality)
    {
        if (!CanChangePreviewOptions || !Enum.IsDefined(quality) || (quality == ProjectPreviewQuality.Original && !CanUseOriginalPreview)) return;
        var request = ++_qualityRequest;
        var intent = ++_playbackIntent;
        if (!IsChangingPreviewQuality) _qualityResume = IsPlayingPreview;
        var resume = _qualityResume;
        IsChangingPreviewQuality = true;
        OnPropertyChanged(nameof(IsChangingPreviewQuality)); NotifyState();
        await _playbackGate.WaitAsync();
        try
        {
            if (intent != _playbackIntent || !CanChangePreviewOptions) return;
            if (IsPlayingPreview && !await StopPreviewCoreAsync()) return;
            if (intent != _playbackIntent) return;
            PreviewQuality = quality;
            ClearPreview(); OnPropertyChanged(nameof(PreviewQuality));
            if (resume && PlayheadTicks < DurationTicks) await StartPreviewCoreAsync(PlayheadTicks, intent);
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; }
        finally
        {
            _playbackGate.Release();
            if (request == _qualityRequest)
            {
                IsChangingPreviewQuality = false; _qualityResume = false;
                OnPropertyChanged(nameof(IsChangingPreviewQuality)); NotifyState(); NotifyPlayback();
            }
        }
    }
}
