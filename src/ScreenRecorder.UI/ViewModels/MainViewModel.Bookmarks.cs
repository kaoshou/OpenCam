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
    public bool IsBookmarkHotkeyRegistered { get; private set; }
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
            var requested = new List<(int, int, uint)>();
            if (OperatingSystem.IsWindows())
            {
                requested.Add((9001, ResolveVirtualKey(settings.StartStopHotkeyKey), settings.StartStopHotkeyModifiers));
                requested.Add((9002, ResolveVirtualKey(settings.PauseResumeHotkeyKey), settings.PauseResumeHotkeyModifiers));
            }
            requested.Add((9003, ResolveVirtualKey(settings.BookmarkHotkeyKey), settings.BookmarkHotkeyModifiers));
            if (!_hotkeyBindings.Apply(requested.ToArray())) return Strings["HotkeyUnavailable"];
            IsBookmarkHotkeyRegistered = true;
            OnPropertyChanged(nameof(IsBookmarkHotkeyRegistered));
            return null;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Shortcut registration failed"); return Strings["HotkeyUnavailable"] + " " + ex.Message; }
    }
}
