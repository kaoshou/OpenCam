using System.Runtime.InteropServices;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Windows.Hotkey;

public class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_USER_REGISTER = 0x0400 + 1;
    private const int WM_USER_UNREGISTER = 0x0400 + 2;
    private const int WM_QUIT = 0x0012;

    private const int DEFAULT_HOTKEY_ID = 9001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    private readonly Thread _loopThread;
    private uint _threadId;
    private readonly ManualResetEvent _readyEvent = new(false);
    private volatile bool _isDisposed;

    public event EventHandler? HotkeyPressed;
    public event EventHandler<int>? HotkeyTriggered;

    public WindowsGlobalHotkeyService()
    {
        _loopThread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "GlobalHotkeyMessageLoop"
        };
        _loopThread.Start();
        _readyEvent.WaitOne(2000);
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _readyEvent.Set();

        // 強制初始化執行緒訊息佇列
        MSG tempMsg;
        GetMessage(out tempMsg, IntPtr.Zero, 0, 0);

        while (!_isDisposed)
        {
            var res = GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (res <= 0) break;

            if (msg.message == WM_HOTKEY)
            {
                int id = (int)msg.wParam;
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                HotkeyTriggered?.Invoke(this, id);
            }
            else if (msg.message == WM_USER_REGISTER)
            {
                int id = (int)msg.wParam;
                uint packed = (uint)msg.lParam;
                uint vk = packed & 0xFFFF;
                uint mods = (packed >> 16) & 0xFFFF;
                RegisterHotKey(IntPtr.Zero, id, mods, vk);
            }
            else if (msg.message == WM_USER_UNREGISTER)
            {
                int id = (int)msg.wParam;
                UnregisterHotKey(IntPtr.Zero, id);
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    public bool RegisterHotkey(int virtualKey, uint modifiers = 0)
    {
        return RegisterHotkey(DEFAULT_HOTKEY_ID, virtualKey, modifiers);
    }

    public bool RegisterHotkey(int id, int virtualKey, uint modifiers = 0)
    {
        if (_isDisposed || _threadId == 0) return false;
        uint packed = ((modifiers & 0xFFFF) << 16) | ((uint)virtualKey & 0xFFFF);
        return PostThreadMessage(_threadId, WM_USER_REGISTER, (IntPtr)id, (IntPtr)packed);
    }

    public void UnregisterHotkey()
    {
        UnregisterHotkey(DEFAULT_HOTKEY_ID);
    }

    public void UnregisterHotkey(int id)
    {
        if (_isDisposed || _threadId == 0) return;
        PostThreadMessage(_threadId, WM_USER_UNREGISTER, (IntPtr)id, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        UnregisterHotkey();
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _readyEvent.Dispose();
    }
}
