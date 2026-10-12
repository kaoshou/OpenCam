// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel
{
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
    }

    [RelayCommand]
    public void ResetProperties()
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
        NotifyPropertyDraft();
    }

    [RelayCommand]
    public Task ApplyPropertiesAsync()
    {
        if (!CanEditSelection) return Task.CompletedTask;
        var clip = SelectedClip!;
        if (!clip.Parts.IsEmpty && !HasEffectDraft)
            return RenameAsync(clip.Id, ClipName);
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
        return ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Properties(
            clip.Id, ClipName, ClipVolumePercent / 100, ClipMuted,
            checked((long)(fadeIn * TimeSpan.TicksPerSecond)),
            checked((long)(fadeOut * TimeSpan.TicksPerSecond)),
            ClipScalePercent / 100, ClipCropPercent, ClipPositionX, ClipPositionY) });
    }

    private static bool Within(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
