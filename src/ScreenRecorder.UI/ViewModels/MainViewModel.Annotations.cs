// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool _screenAnnotationsEnabled=true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasScreenAnnotationNotice))] private string? _screenAnnotationNotice;
    public bool HasScreenAnnotationNotice=>!string.IsNullOrEmpty(ScreenAnnotationNotice);
    public bool IsAnnotationHotkeyRegistered {get;private set;}
    public Action? RequestScreenAnnotations {get;set;}
    public string ScreenAnnotationsTooltip=>Strings["AnnotationsCaptured"]+(IsAnnotationHotkeyRegistered?"\n"+Strings["AnnotationsShortcut"]:"");
    private void RegisterAnnotationHotkey(bool enabled)
    {
        if(_globalHotkeyService is null)return;
        if(IsAnnotationHotkeyRegistered)
        {
            try{_globalHotkeyService.UnregisterHotkey(9004);IsAnnotationHotkeyRegistered=false;}
            catch(Exception error){Serilog.Log.Warning(error,"Cannot release annotation shortcut");return;}
        }
        try
        {
            IsAnnotationHotkeyRegistered=enabled&&_globalHotkeyService.RegisterHotkey(9004,0x77,6);
        }
        catch(Exception error){IsAnnotationHotkeyRegistered=false;Serilog.Log.Warning(error,"Annotation shortcut unavailable");}
        OnPropertyChanged(nameof(IsAnnotationHotkeyRegistered));OnPropertyChanged(nameof(ScreenAnnotationsTooltip));
    }
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
