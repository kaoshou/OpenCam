// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _screenAnnotationsEnabled=true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasScreenAnnotationNotice))] private string? _screenAnnotationNotice;
    public bool HasScreenAnnotationNotice=>!string.IsNullOrEmpty(ScreenAnnotationNotice);
    public uint ScreenAnnotationColor{get;private set;}=0xffe63946;
    public double ScreenAnnotationWidth{get;private set;}=4;
    public bool CanOpenScreenAnnotations=>ScreenAnnotationsEnabled&&IsRecording&&!IsPaused&&!IsPreparing&&!IsRecovering;
    public CaptureRegion ScreenAnnotationRegion=>IsCustomRegion?new(RegionX,RegionY,RegionWidth,RegionHeight):
        GetCurrentMonitors().FirstOrDefault(m=>m.Index==SelectedMonitor?.Index)?.Bounds??CaptureRegion.Empty;
    partial void OnScreenAnnotationsEnabledChanged(bool value)=>OnPropertyChanged(nameof(CanOpenScreenAnnotations));
    public void NotifyAnnotationAvailability()=>OnPropertyChanged(nameof(CanOpenScreenAnnotations));
    public void SaveScreenAnnotationStyle(uint color,double width)
    {
        ScreenAnnotationColor=color|0xff000000;
        ScreenAnnotationWidth=double.IsFinite(width)?width<=2?2:width<=4?4:8:4;
        PersistUserSettings();
    }
    private void ApplyAnnotationSettings(UserSettings settings)
    {
        ScreenAnnotationsEnabled=settings.ScreenAnnotationsEnabled;
        ScreenAnnotationColor=settings.ScreenAnnotationColor|0xff000000;
        ScreenAnnotationWidth=settings.ScreenAnnotationWidth;
    }
}
