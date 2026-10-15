using ScreenRecorder.Platform.macOS;
using ScreenRecorder.UI.Recording;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Media.Tests;

public sealed class BookmarkHotkeyTests
{
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
    private sealed class FakeHotkeys : IGlobalHotkeyService
    {
        public Dictionary<int, (int, uint)> Active = []; public int Reject;
        public event EventHandler? HotkeyPressed { add { } remove { } }
        public event EventHandler<int>? HotkeyTriggered { add { } remove { } }
        public bool RegisterHotkey(int key, uint mods = 0) => RegisterHotkey(9001, key, mods);
        public bool RegisterHotkey(int id, int key, uint mods = 0) { if (key == Reject) return false; Active[id] = (key, mods); return true; }
        public void UnregisterHotkey() => UnregisterHotkey(9001);
        public void UnregisterHotkey(int id) => Active.Remove(id);
        public void Dispose() { }
    }
}
