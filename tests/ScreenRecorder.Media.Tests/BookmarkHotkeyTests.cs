using ScreenRecorder.Platform.macOS;
using ScreenRecorder.UI.Recording;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class BookmarkHotkeyTests
{
    [Fact]
    public void LostNativeRegistrationsAreRecreatedWhenIdenticalSettingsAreApplied()
    {
        var vm=new MainViewModel(forScreenshot:true);var native=new FakeHotkeys();
        typeof(MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,native);
        try {
            var settings=new UserSettings{StartStopHotkeyKey="F12",PauseResumeHotkeyKey="F12"};
            Assert.Null(vm.TryApplyHotkeys(settings));Assert.True(vm.IsRecordingHotkeyRegistered);
            native.Active.Clear();vm.InvalidateHotkeyRegistrations();
            Assert.False(vm.IsRecordingHotkeyRegistered);Assert.False(vm.IsBookmarkHotkeyRegistered);
            Assert.Null(vm.TryApplyHotkeys(settings));
            Assert.Equal((0x7b,2u),native.Active[9001]);Assert.Equal((0x7b,0u),native.Active[9002]);
            Assert.True(vm.IsRecordingHotkeyRegistered);
        }finally{vm.Cleanup();}
    }
    [Theory]
    [InlineData(0x4D, 46)]
    [InlineData(0x78, 101)]
    [InlineData(0x20, 49)]
    public void VirtualKeysMapToCorrectMacPhysicalKeys(int key, int native)
    { Assert.True(MacOsGlobalHotkeyService.TryMapKey(key, out var mapped)); Assert.Equal((uint)native, mapped); }
    [Fact]
    public void FailedReplacementPreservesEarlierSuccessfulBindings()
    {
        using var native = new FakeHotkeys();
        var bindings = new RecordingHotkeyBindings(native);
        Assert.True(bindings.Apply([(9001, 120, 0u), (9003, 77, 6u)]));
        native.Reject = 83;
        Assert.False(bindings.Apply([(9001, 121, 0u), (9003, 83, 6u)]));
        Assert.Equal((120, 0u), native.Active[9001]); Assert.Equal((77, 6u), native.Active[9003]);
    }
    [Fact]
    public void ConflictingAnnotationShortcutRollsBackBookmarkAndKeepsToolbarShortcut()
    {
        var vm=new MainViewModel(forScreenshot:true){IsRecording=true};
        var native=new FakeHotkeys();
        typeof(MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,native);
        try
        {
            var settings=new UserSettings{ScreenAnnotationsEnabled=true};
            Assert.Null(vm.TryApplyHotkeys(settings));Assert.True(vm.IsAnnotationHotkeyRegistered);
            settings.BookmarkHotkeyKey="F11";settings.BookmarkHotkeyModifiers=0;
            Assert.NotNull(vm.TryApplyHotkeys(settings));Assert.True(vm.IsAnnotationHotkeyRegistered);
            Assert.Equal((0x4d,6u),native.Active[9003]);
            settings.BookmarkHotkeyKey="M";settings.BookmarkHotkeyModifiers=6;Assert.Null(vm.TryApplyHotkeys(settings));Assert.True(vm.IsAnnotationHotkeyRegistered);
        }
        finally{vm.Cleanup();}
    }
    [Fact]
    public void FailedAnnotationShortcutReleaseDoesNotChangeExistingBookmark()
    {
        var vm=new MainViewModel(forScreenshot:true){IsRecording=true};var native=new FakeHotkeys();
        typeof(MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,native);
        try
        {
            var settings=new UserSettings{ScreenAnnotationsEnabled=true};Assert.Null(vm.TryApplyHotkeys(settings));
            native.FailRelease=true;settings.BookmarkHotkeyKey="F8";
            Assert.NotNull(vm.TryApplyHotkeys(settings));Assert.Equal((0x4d,6u),native.Active[9003]);
            Assert.True(vm.IsAnnotationHotkeyRegistered);
        }
        finally{native.FailRelease=false;vm.Cleanup();}
    }
    [Fact] public void AnnotationHotkeysAreReleasedOutsideRecording()
    {
        var vm=new MainViewModel(forScreenshot:true);var native=new FakeHotkeys();
        typeof(MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,native);
        try
        {
            Assert.Null(vm.TryApplyHotkeys(new UserSettings()));
            Assert.False(native.Active.ContainsKey(9004));Assert.False(native.Active.ContainsKey(9005));
            vm.IsRecording=true;vm.NotifyAnnotationAvailability();
            Assert.True(native.Active.ContainsKey(9004));Assert.True(native.Active.ContainsKey(9005));
            vm.IsRecording=false;vm.NotifyAnnotationAvailability();
            Assert.False(native.Active.ContainsKey(9004));Assert.False(native.Active.ContainsKey(9005));
        }
        finally {vm.Cleanup();}
    }
    [Fact] public void FailedSettingsChangeDoesNotReenableIdleAnnotationHotkeys()
    {
        var vm=new MainViewModel(forScreenshot:true);var native=new FakeHotkeys();
        typeof(MainViewModel).GetField("_globalHotkeyService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(vm,native);
        try
        {
            var settings=new UserSettings();Assert.Null(vm.TryApplyHotkeys(settings));
            settings.BookmarkHotkeyKey="F11";settings.BookmarkHotkeyModifiers=0;
            Assert.NotNull(vm.TryApplyHotkeys(settings));Assert.Equal((0x4d,6u),native.Active[9003]);
            Assert.False(native.Active.ContainsKey(9004));Assert.False(native.Active.ContainsKey(9005));
        }
        finally {vm.Cleanup();}
    }
    private sealed class FakeHotkeys : IGlobalHotkeyService
    {
        public Dictionary<int, (int, uint)> Active = []; public int Reject; public bool FailRelease;
        public event EventHandler? HotkeyPressed { add { } remove { } }
        public event EventHandler<int>? HotkeyTriggered { add { } remove { } }
        public bool RegisterHotkey(int key, uint mods = 0) => RegisterHotkey(9001, key, mods);
        public bool RegisterHotkey(int id, int key, uint mods = 0) { if (key == Reject || Active.Any(x=>x.Key!=id&&x.Value==(key,mods))) return false; Active[id] = (key, mods); return true; }
        public void UnregisterHotkey() => UnregisterHotkey(9001);
        public void UnregisterHotkey(int id) { if(FailRelease&&id==9004)throw new InvalidOperationException("release failed");Active.Remove(id); }
        public void Dispose() { }
    }
}
