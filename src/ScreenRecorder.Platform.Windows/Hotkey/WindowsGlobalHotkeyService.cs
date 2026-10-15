// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Windows.Hotkey;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const uint HotkeyMessage = 0x0312, WorkMessage = 0x8001, Quit = 0x0012;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    private sealed class Request(int id, int key, uint mods, bool remove)
    {
        public readonly int Id = id, Key = key; public readonly uint Mods = mods; public readonly bool Remove = remove;
        public bool Canceled, Done, Result;
        public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly ConcurrentDictionary<int, Request> _requests = new();
    private readonly Dictionary<int, (int NativeId, int Key, uint Mods)> _bindings = [];
    private readonly Thread _thread;
    private uint _threadId;
    private int _requestId, _nativeId;
    private volatile bool _disposed;
    public event EventHandler? HotkeyPressed;
    public event EventHandler<int>? HotkeyTriggered;
    public WindowsGlobalHotkeyService()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "OpenCam hotkeys" };
        _thread.Start(); ready.Wait();
    }
    private void Loop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); ready.Set();
        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.Id == WorkMessage && _requests.TryRemove((int)msg.WParam, out var request))
                {
                    lock (request)
                    {
                        if (request.Canceled) continue;
                        request.Result = Change(request); request.Done = true;
                        request.Completion.TrySetResult(request.Result);
                    }
                }
                else if (msg.Id == HotkeyMessage)
                {
                    var native = (int)msg.WParam;
                    var logical = _bindings.FirstOrDefault(k => k.Value.NativeId == native);
                    if (logical.Key != 0)
                        try { HotkeyPressed?.Invoke(this, EventArgs.Empty); HotkeyTriggered?.Invoke(this, logical.Key); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "Windows shortcut callback failed"); }
                }
            }
        }
        finally
        {
            foreach (var binding in _bindings.Values) UnregisterHotKey(IntPtr.Zero, binding.NativeId);
            _bindings.Clear();
            foreach (var request in _requests.Values) request.Completion.TrySetResult(false);
            _requests.Clear();
        }
    }
    private bool Change(Request request)
    {
        _bindings.TryGetValue(request.Id, out var old);
        if (request.Remove)
        {
            if (old.NativeId == 0) return true;
            if (!UnregisterHotKey(IntPtr.Zero, old.NativeId)) return false;
            _bindings.Remove(request.Id); return true;
        }
        if (old.NativeId != 0 && old.Key == request.Key && old.Mods == request.Mods) return true;
        var nativeId = ++_nativeId;
        if (nativeId > 0xBFFF || request.Key <= 0 || (request.Mods & ~15u) != 0 ||
            !RegisterHotKey(IntPtr.Zero, nativeId, request.Mods | 0x4000, (uint)request.Key)) return false;
        if (old.NativeId != 0 && !UnregisterHotKey(IntPtr.Zero, old.NativeId))
        { UnregisterHotKey(IntPtr.Zero, nativeId); return false; }
        _bindings[request.Id] = (nativeId, request.Key, request.Mods); return true;
    }
    private bool Invoke(Request request)
    {
        if (_disposed) return false;
        var id = Interlocked.Increment(ref _requestId); _requests[id] = request;
        if (!PostThreadMessage(_threadId, WorkMessage, (IntPtr)id, IntPtr.Zero)) { _requests.TryRemove(id, out _); return false; }
        request.Completion.Task.Wait(TimeSpan.FromSeconds(2));
        lock (request)
        {
            if (request.Done) return request.Result;
            request.Canceled = true; _requests.TryRemove(id, out _); return false;
        }
    }
    public bool RegisterHotkey(int key, uint modifiers = 0) => RegisterHotkey(9001, key, modifiers);
    public bool RegisterHotkey(int id, int key, uint modifiers = 0) => Invoke(new(id, key, modifiers, false));
    public void UnregisterHotkey() => UnregisterHotkey(9001);
    public void UnregisterHotkey(int id) { if (!Invoke(new(id, 0, 0, true)) && !_disposed) throw new InvalidOperationException("Cannot unregister Windows shortcut."); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (!PostThreadMessage(_threadId, Quit, IntPtr.Zero, IntPtr.Zero) || !_thread.Join(TimeSpan.FromSeconds(3)))
            throw new InvalidOperationException("Cannot stop Windows shortcut service.");
    }
}
