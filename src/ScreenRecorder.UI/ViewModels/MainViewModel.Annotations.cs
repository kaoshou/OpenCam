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
    public Action<string>? RequestAnnotationCommand {get;set;}
    private Dictionary<string,AnnotationShortcut> _annotationShortcuts=AnnotationShortcut.Defaults();
    private readonly HashSet<int> _annotationHotkeys=[];
    private bool _annotationBindingsRequestedForRecording;
    public string ScreenAnnotationsTooltip=>Strings["AnnotationsCaptured"]+(IsAnnotationHotkeyRegistered?"\n"+FormatHotkey(_annotationShortcuts["toggleToolbar"].Modifiers,_annotationShortcuts["toggleToolbar"].Key):"");
    public bool MatchesAnnotationFinish(Avalonia.Input.Key key,Avalonia.Input.KeyModifiers modifiers) =>
        _annotationShortcuts.TryGetValue("finish",out var shortcut) && MatchesHotkey(key,modifiers,shortcut.Modifiers,shortcut.Key);
    private bool RegisterAnnotationHotkey(UserSettings? settings)
    {
        if(_globalHotkeyService is null)return true;
        foreach(var id in _annotationHotkeys.ToArray())
        {
            try{_globalHotkeyService.UnregisterHotkey(id);_annotationHotkeys.Remove(id);}
            catch(Exception error){Serilog.Log.Warning(error,"Cannot release annotation shortcut");return false;}
        }
        IsAnnotationHotkeyRegistered=false;
        if(settings?.ScreenAnnotationsEnabled==true)
        {
            var shortcuts=settings.AnnotationShortcuts??AnnotationShortcut.Defaults();
            var defaults=AnnotationShortcut.Defaults();
            foreach(var (name,id) in new[]{("toggleToolbar",9004),("togglePen",9005),("pen",9006),("line",9007),("rectangle",9008),("ellipse",9009)})
            {
                var key=shortcuts.TryGetValue(name,out var configured)&&configured is not null?configured:defaults[name];
                try { if(!_globalHotkeyService.RegisterHotkey(id,ResolveVirtualKey(key.Key),key.Modifiers))return false; _annotationHotkeys.Add(id); }
                catch(Exception error){Serilog.Log.Warning(error,"Annotation shortcut unavailable: {Shortcut}",name);return false;}
            }
            IsAnnotationHotkeyRegistered=true;
        }
        OnPropertyChanged(nameof(IsAnnotationHotkeyRegistered));OnPropertyChanged(nameof(ScreenAnnotationsTooltip));
        return true;
    }
    public uint ScreenAnnotationColor{get;private set;}=0xffe63946;
    public double ScreenAnnotationWidth{get;private set;}=4;
    public bool CanOpenScreenAnnotations=>(!OperatingSystem.IsLinux()||ScreenRecorder.Platform.Linux.LinuxX11InputPolicy.IsX11)&&ScreenAnnotationsEnabled&&IsRecording&&!IsPaused&&!IsPreparing&&!IsRecovering;
    public CaptureRegion ScreenAnnotationRegion=>IsCustomRegion?new(RegionX,RegionY,RegionWidth,RegionHeight):
        GetCurrentMonitors().FirstOrDefault(m=>m.Index==SelectedMonitor?.Index)?.Bounds??CaptureRegion.Empty;
    partial void OnScreenAnnotationsEnabledChanged(bool value)=>OnPropertyChanged(nameof(CanOpenScreenAnnotations));
    public void NotifyAnnotationAvailability()
    {
        OnPropertyChanged(nameof(CanOpenScreenAnnotations));
        var needed=CanOpenScreenAnnotations&&_registeredSettings?.ScreenAnnotationsEnabled==true;
        if(needed==_annotationBindingsRequestedForRecording)return;
        _annotationBindingsRequestedForRecording=needed;
        if(!RegisterAnnotationHotkey(needed?_registeredSettings:null))
        {
            RegisterAnnotationHotkey(null);
            ScreenAnnotationNotice=Strings["HotkeyUnavailable"];
        }
    }
    public void SaveScreenAnnotationStyle(uint color,double width)
    {
        ScreenAnnotationColor=color|0xff000000;
        ScreenAnnotationWidth=double.IsFinite(width)?width<=2?2:width<=4?4:8:4;
        PersistUserSettings();
    }
    private void ApplyAnnotationSettings(UserSettings settings)
    {
        ScreenAnnotationsEnabled=settings.ScreenAnnotationsEnabled;
        _annotationShortcuts=AnnotationShortcut.Defaults();
        if(settings.AnnotationShortcuts is not null)
            foreach(var (name,value) in settings.AnnotationShortcuts)
                if(_annotationShortcuts.ContainsKey(name)&&value is not null&&!string.IsNullOrWhiteSpace(value.Key))
                    _annotationShortcuts[name]=new(){Modifiers=value.Modifiers,Key=value.Key};
        ScreenAnnotationColor=settings.ScreenAnnotationColor|0xff000000;
        ScreenAnnotationWidth=settings.ScreenAnnotationWidth;
    }
}
