// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
    private bool _resettingProperties;
    private bool _savingProperties, _publishingPropertyClips;
    public bool CanEditProperties => !IsChangingPreviewQuality && !StatusUnconfirmed && !IsExporting &&
        !State.HasRecoverableDraft && State.Mode is ProjectMode.Ready or ProjectMode.Paused &&
        (!IsBusy || _savingProperties) && SelectedClip is not null && Clips.Contains(SelectedClip);
    private CancellationTokenSource? _propertyApplyDelay;
    private static object Effects(ProjectClip c) => (c.Volume, c.Muted, c.FadeInTicks, c.FadeOutTicks, c.Scale, c.Crop, c.PositionX, c.PositionY);
    public bool HasMixedProperties => SelectedClip is { Parts.IsEmpty: false } c && c.Parts.Select(Effects).Distinct().Skip(1).Any();
    private ProjectClip? DisplayProperties
    {
        get
        {
            var c = SelectedClip;
            if (c is null || c.Parts.IsEmpty || HasMixedProperties) return c;
            var first = c.Parts[0];
            return first with { Id = c.Id, Name = c.Name };
        }
    }
    [ObservableProperty] private double _clipVolumePercent = 100;
    [ObservableProperty] private bool _clipMuted;
    [ObservableProperty] private decimal? _clipFadeInSeconds = 0;
    [ObservableProperty] private decimal? _clipFadeOutSeconds = 0;
    [ObservableProperty] private double _clipScalePercent = 100;
    [ObservableProperty] private double _clipCropPercent;
    [ObservableProperty] private double _clipPositionX;
    [ObservableProperty] private double _clipPositionY;

    private bool HasEffectDraft => DisplayProperties is { } c &&
        (!double.IsFinite(ClipVolumePercent) ||
         Math.Abs(ClipVolumePercent - c.Volume * 100) > 1e-9 ||
         ClipMuted != c.Muted || ClipFadeInSeconds != (decimal)c.FadeInTicks / TimeSpan.TicksPerSecond ||
         ClipFadeOutSeconds != (decimal)c.FadeOutTicks / TimeSpan.TicksPerSecond ||
         !double.IsFinite(ClipScalePercent) || Math.Abs(ClipScalePercent - c.Scale * 100) > 1e-9 ||
         ClipCropPercent != c.Crop || ClipPositionX != c.PositionX || ClipPositionY != c.PositionY);
    public bool HasPropertyDraft => SelectedClip is { } c && (ClipName != c.Name || HasEffectDraft);
    partial void OnClipNameChanged(string value) => NotifyPropertyDraft();
    partial void OnClipVolumePercentChanged(double value) => NotifyPropertyDraft();
    partial void OnClipMutedChanged(bool value) => NotifyPropertyDraft();
    partial void OnClipFadeInSecondsChanged(decimal? value) => NotifyPropertyDraft();
    partial void OnClipFadeOutSecondsChanged(decimal? value) => NotifyPropertyDraft();
    partial void OnClipScalePercentChanged(double value) => NotifyPropertyDraft();
    partial void OnClipCropPercentChanged(double value) => NotifyPropertyDraft();
    partial void OnClipPositionXChanged(double value) => NotifyPropertyDraft();
    partial void OnClipPositionYChanged(double value) => NotifyPropertyDraft();
    private void NotifyPropertyDraft()
    {
        OnPropertyChanged(nameof(HasPropertyDraft));
        NotifyState();
        if (_resettingProperties) return;
        CancelPropertyApplyDelay();
        if (!HasPropertyDraft || !CanEditProperties || _savingProperties) return;
        var delay = new CancellationTokenSource();
        _propertyApplyDelay = delay;
        _ = AutoApplyPropertiesAsync(delay, State.ProjectId, SelectedClip!.Id);
    }

    private void CancelPropertyApplyDelay()
    {
        _propertyApplyDelay?.Cancel();
        _propertyApplyDelay = null;
    }

    private async Task AutoApplyPropertiesAsync(CancellationTokenSource delay, Guid? project, Guid clip)
    {
        try
        {
            // Coalesce typing/slider events so one gesture does not flood IPC and undo history.
            await Task.Delay(250, delay.Token);
            if (State.ProjectId == project && SelectedClip?.Id == clip && HasPropertyDraft && CanEditSelection)
                await ApplyPropertiesAsync();
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            if (ReferenceEquals(_propertyApplyDelay, delay)) _propertyApplyDelay = null;
            delay.Dispose();
        }
    }

    [RelayCommand]
    public void ResetProperties()
    {
        CancelPropertyApplyDelay();
        _resettingProperties = true;
        try
        {
        OnPropertyChanged(nameof(HasMixedProperties));
        var clip = DisplayProperties;
        ClipName = clip?.Name ?? "";
        ClipVolumePercent = (clip?.Volume ?? 1) * 100;
        ClipMuted = clip?.Muted ?? false;
        ClipFadeInSeconds = (decimal)(clip?.FadeInTicks ?? 0) / TimeSpan.TicksPerSecond;
        ClipFadeOutSeconds = (decimal)(clip?.FadeOutTicks ?? 0) / TimeSpan.TicksPerSecond;
        ClipScalePercent = (clip?.Scale ?? 1) * 100;
        ClipCropPercent = clip?.Crop ?? 0;
        ClipPositionX = clip?.PositionX ?? 0;
        ClipPositionY = clip?.PositionY ?? 0;
        }
        finally { _resettingProperties = false; }
        NotifyPropertyDraft();
    }

    [RelayCommand]
    public Task ApplyPropertiesAsync()
    {
        CancelPropertyApplyDelay();
        if (!CanEditSelection || !HasPropertyDraft) return Task.CompletedTask;
        var clip = SelectedClip!;
        var project = State.ProjectId;
        var revision = State.Revision;
        if (!clip.Parts.IsEmpty && !HasEffectDraft)
            return SaveInspectorAsync("RenameProjectClip", new() { ClipId = clip.Id, Name = ClipName },
                () => CanEditSelection && State.ProjectId == project && State.Revision == revision && SelectedClip?.Id == clip.Id, true);
        var span = TimelineClips.FirstOrDefault(c => c.ClipId == clip.Id);
        var duration = span is null ? -1m : (decimal)(span.EndTicks - span.StartTicks) / TimeSpan.TicksPerSecond;
        if (string.IsNullOrWhiteSpace(ClipName) || ClipName.Length > 200 ||
            ClipName.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            !Within(ClipVolumePercent, 0, 200) || !Within(ClipScalePercent, 100, 200) ||
            !Within(ClipCropPercent, 0, 40) || !Within(ClipPositionX, -50, 50) ||
            !Within(ClipPositionY, -50, 50) ||
            ClipFadeInSeconds is not { } fadeIn || ClipFadeOutSeconds is not { } fadeOut ||
            fadeIn < 0 || fadeOut < 0 || fadeIn > duration || fadeOut > duration)
        {
            Error = Strings["ProjectInvalidProperties"];
            return Task.CompletedTask;
        }
        return SaveInspectorAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Properties(
            clip.Id, ClipName, ClipVolumePercent / 100, ClipMuted,
            checked((long)(fadeIn * TimeSpan.TicksPerSecond)),
            checked((long)(fadeOut * TimeSpan.TicksPerSecond)),
            ClipScalePercent / 100, ClipCropPercent, ClipPositionX, ClipPositionY) },
            () => CanEditSelection && State.ProjectId == project && State.Revision == revision && SelectedClip?.Id == clip.Id, !HasEffectDraft);
    }

    private async Task SaveInspectorAsync(string command, ScreenRecorder.Infrastructure.IPC.ProjectRequest request,
        Func<bool> precondition, bool mediaUnchanged)
    {
        _savingProperties = true;
        try { await ExecuteAsync(command, request, precondition, keepInspector: true, mediaUnchanged: mediaUnchanged); }
        finally
        {
            _savingProperties = false;
            OnPropertyChanged(nameof(CanEditProperties));
            // A newer keystroke/slider value may have arrived while the previous snapshot was saving.
            // Never replace it with that older acknowledgement; persist the new draft in order.
            if (Error is null && !StatusUnconfirmed) NotifyPropertyDraft();
        }
    }

    private static bool Within(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
