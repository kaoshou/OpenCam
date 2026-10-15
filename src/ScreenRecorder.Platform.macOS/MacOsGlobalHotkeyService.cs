// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

/// <summary>Registered Carbon hotkeys, not a keyboard event tap. Call on the owning UI thread.</summary>
public sealed class MacOsGlobalHotkeyService : IGlobalHotkeyService
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Signature = 0x4F43616D; // OCam
    [StructLayout(LayoutKind.Sequential)] private struct EventType { public uint Class, Kind; }
    [StructLayout(LayoutKind.Sequential)] private struct HotkeyId { public uint Signature, Id; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EventCallback(IntPtr next, IntPtr evt, IntPtr user);
    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, EventCallback handler, nuint count, [In] EventType[] types, IntPtr user, out IntPtr reference);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handler);
    [DllImport(Carbon)] private static extern uint GetEventKind(IntPtr evt);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint key, uint modifiers, HotkeyId id, IntPtr target, uint options, out IntPtr reference);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr reference);
    [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr evt, uint name, uint type, IntPtr actualType, nuint size, IntPtr actualSize, out HotkeyId id);
    private readonly EventCallback _callback;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<int, (IntPtr Ref, uint NativeId, int Key, uint Mods)> _keys = [];
    private IntPtr _handler;
    private uint _nextId;
    private bool _disposed;
    private readonly HashSet<uint> _pressed = [];
    public event EventHandler? HotkeyPressed;
    public event EventHandler<int>? HotkeyTriggered;

    public MacOsGlobalHotkeyService()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        _callback = OnEvent;
        var result = InstallEventHandler(GetApplicationEventTarget(), _callback, 2,
            [new() { Class = 0x6b657962, Kind = 5 }, new() { Class = 0x6b657962, Kind = 6 }], IntPtr.Zero, out _handler);
        if (result != 0) throw new InvalidOperationException($"Cannot install macOS hotkey handler ({result}).");
    }
    public static bool TryMapKey(int key, out uint native)
    {
        var mapped = key switch {
            0x4D => 46, 0x52 => 15, 0x53 => 1, 0x50 => 35, 0x20 => 49, 0x2D => 114, 0x24 => 115,
            0x23 => 119, 0x21 => 116, 0x22 => 121,
            0x70 => 122, 0x71 => 120, 0x72 => 99, 0x73 => 118, 0x74 => 96, 0x75 => 97,
            0x76 => 98, 0x77 => 100, 0x78 => 101, 0x79 => 109, 0x7A => 103, 0x7B => 111, _ => -1 };
        native = (uint)Math.Max(0, mapped); return mapped >= 0;
    }
    private void EnsureThread() { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Hotkey access must use the owning UI thread."); }
    public bool RegisterHotkey(int key, uint modifiers = 0) => RegisterHotkey(9001, key, modifiers);
    public bool RegisterHotkey(int id, int key, uint modifiers = 0)
    {
        EnsureThread();
        if (_disposed || (modifiers & ~15u) != 0 || !TryMapKey(key, out var code)) return false;
        if (_keys.TryGetValue(id, out var previous) && previous.Key == key && previous.Mods == modifiers) return true;
        var mods = ((modifiers & 1) != 0 ? 2048u : 0) | ((modifiers & 2) != 0 ? 4096u : 0) |
            ((modifiers & 4) != 0 ? 512u : 0) | ((modifiers & 8) != 0 ? 256u : 0);
        var nativeId = checked(++_nextId);
        var status = RegisterEventHotKey(code, mods, new() { Signature = Signature, Id = nativeId }, GetApplicationEventTarget(), 1, out var reference);
        if (status != 0) return false;
        if (previous.Ref != IntPtr.Zero && UnregisterEventHotKey(previous.Ref) != 0)
        { UnregisterEventHotKey(reference); return false; }
        _keys[id] = (reference, nativeId, key, modifiers); return true;
    }
    private int OnEvent(IntPtr next, IntPtr evt, IntPtr user)
    {
        try
        {
            if (_disposed || GetEventParameter(evt, 0x2d2d2d2d, 0x686b6964, IntPtr.Zero, 8, IntPtr.Zero, out var key) != 0 || key.Signature != Signature) return -9874;
            if (GetEventKind(evt) == 6) { _pressed.Remove(key.Id); return 0; }
            if (!_pressed.Add(key.Id)) return 0;
            foreach (var binding in _keys)
                if (binding.Value.NativeId == key.Id) { HotkeyPressed?.Invoke(this, EventArgs.Empty); HotkeyTriggered?.Invoke(this, binding.Key); return 0; }
            return -9874;
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "macOS hotkey callback failed"); return -9874; }
    }
    public void UnregisterHotkey() => UnregisterHotkey(9001);
    public void UnregisterHotkey(int id)
    {
        EnsureThread();
        if (_keys.TryGetValue(id, out var key))
        {
            if (UnregisterEventHotKey(key.Ref) != 0) throw new InvalidOperationException("Cannot unregister macOS shortcut.");
            _keys.Remove(id);
            _pressed.Remove(key.NativeId);
        }
    }
    public void Dispose()
    {
        EnsureThread(); if (_disposed) return;
        foreach (var id in _keys.Keys.ToArray()) UnregisterHotkey(id);
        if (_handler != IntPtr.Zero && RemoveEventHandler(_handler) != 0) throw new InvalidOperationException("Cannot remove macOS hotkey handler.");
        _handler = IntPtr.Zero; _disposed = true; GC.KeepAlive(_callback);
    }
}
