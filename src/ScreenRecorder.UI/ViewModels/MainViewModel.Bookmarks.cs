// SPDX-License-Identifier: AGPL-3.0-or-later
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Recording;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    private RecordingHotkeyBindings? _hotkeyBindings;
    private string? _hotkeyInitializationError;
    private UserSettings? _registeredSettings;
    public bool IsBookmarkHotkeyRegistered { get; private set; }
    public bool IsRecordingHotkeyRegistered { get; private set; }
    internal void InvalidateHotkeyRegistrations()
    {
        // Native mapping loss invalidates the entire transactional cache, not
        // just the badges. Equal settings must issue fresh grabs next time.
        _hotkeyBindings=null;_annotationHotkeys.Clear();_annotationBindingsRequestedForRecording=false;
        IsRecordingHotkeyRegistered=false;IsBookmarkHotkeyRegistered=false;IsAnnotationHotkeyRegistered=false;
        _hotkeyInitializationError=Strings["HotkeyUnavailable"];ScreenAnnotationNotice=_hotkeyInitializationError;
        OnPropertyChanged(nameof(IsBookmarkHotkeyRegistered));OnPropertyChanged(nameof(IsAnnotationHotkeyRegistered));OnPropertyChanged(nameof(ScreenAnnotationsTooltip));
    }
    private bool _addingBookmark;
    private long _lastBookmarkKey;
    public bool CanAddBookmark => !_openingContent && _content?.Workspace.State.Mode == ProjectMode.Recording && !_addingBookmark;
    public string BookmarkHotkeyText => FormatHotkey(_bookmarkHotkeyModifiers, _bookmarkHotkeyKey);
    public bool MatchesBookmarkHotkey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers) =>
        MatchesHotkey(key, modifiers, _bookmarkHotkeyModifiers, _bookmarkHotkeyKey);

    [RelayCommand]
    public async Task AddBookmarkAsync()
    {
        if (!CanAddBookmark || _lastBookmarkKey != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastBookmarkKey).TotalMilliseconds < 500) return;
        _lastBookmarkKey = System.Diagnostics.Stopwatch.GetTimestamp(); _addingBookmark = true; OnPropertyChanged(nameof(CanAddBookmark));
        try
        {
            var vm = _content!.Workspace;
            await vm.AddRecordingBookmarkAsync(Strings["BookmarkPrefix"]);
            StatusMessage = vm.BookmarkError ?? vm.Error ?? Strings["BookmarkAdded"];
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { _addingBookmark = false; OnPropertyChanged(nameof(CanAddBookmark)); }
    }

    public string? TryApplyHotkeys(UserSettings settings)
    {
        if (_globalHotkeyService is null) return _hotkeyInitializationError; // Screenshot/headless path never registers with the OS.
        _hotkeyBindings ??= new(_globalHotkeyService);
        try
        {
            if(!RegisterAnnotationHotkey(null))return Strings["HotkeyUnavailable"];
            var requested = new List<(int, int, uint)>();
            requested.Add((9001, ResolveVirtualKey(settings.StartStopHotkeyKey), settings.StartStopHotkeyModifiers));
            requested.Add((9002, ResolveVirtualKey(settings.PauseResumeHotkeyKey), settings.PauseResumeHotkeyModifiers));
            requested.Add((9003, ResolveVirtualKey(settings.BookmarkHotkeyKey), settings.BookmarkHotkeyModifiers));
            if (!_hotkeyBindings.Apply(requested.ToArray())) { RegisterAnnotationHotkey(_annotationBindingsRequestedForRecording?_registeredSettings:null); return Strings["HotkeyUnavailable"]; }
            IsRecordingHotkeyRegistered=true;
            IsBookmarkHotkeyRegistered = true;
            OnPropertyChanged(nameof(IsBookmarkHotkeyRegistered));
            if(!RegisterAnnotationHotkey(settings))
            {
                RegisterAnnotationHotkey(null);
                if(_registeredSettings is not null)
                {
                    var prior=new List<(int,int,uint)>();
                    prior.Add((9001,ResolveVirtualKey(_registeredSettings.StartStopHotkeyKey),_registeredSettings.StartStopHotkeyModifiers)); prior.Add((9002,ResolveVirtualKey(_registeredSettings.PauseResumeHotkeyKey),_registeredSettings.PauseResumeHotkeyModifiers));
                    prior.Add((9003,ResolveVirtualKey(_registeredSettings.BookmarkHotkeyKey),_registeredSettings.BookmarkHotkeyModifiers));
                    if(!_hotkeyBindings.Apply(prior.ToArray())||!RegisterAnnotationHotkey(_annotationBindingsRequestedForRecording?_registeredSettings:null))return Strings["HotkeyUnavailable"];
                }
                return Strings["HotkeyUnavailable"];
            }
            _registeredSettings=System.Text.Json.JsonSerializer.Deserialize<UserSettings>(System.Text.Json.JsonSerializer.Serialize(settings));
            _annotationBindingsRequestedForRecording=CanOpenScreenAnnotations&&settings.ScreenAnnotationsEnabled;
            if(!_annotationBindingsRequestedForRecording&&!RegisterAnnotationHotkey(null))return Strings["HotkeyUnavailable"];
            return null;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Shortcut registration failed"); return Strings["HotkeyUnavailable"] + " " + ex.Message; }
    }
}
