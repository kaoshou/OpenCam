// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ComponentModel;
using Serilog;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows.Cursor;

[SupportedOSPlatform("windows")]
public class WindowsCursorHighlightService : ICursorHighlightService, IPresentationPointerOverlay
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;

    private const int ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;

    private const int VK_LBUTTON = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetPhysicalCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static bool s_classRegistered;
    private static readonly object s_classLock = new();
    private static WndProc? s_wndProcDelegate;

    private const int OverlaySize = 140;
    private const int HalfSize = OverlaySize / 2;

    private Thread? _workerThread;
    private volatile bool _isRunning;
    private CursorEffectMode _currentMode = CursorEffectMode.Default;
    private readonly object _stateLock = new();
    private TaskCompletionSource<bool>? _presentationReady;
    private int _overlayFailureReported;
    private bool _disposed;
    private TeachingPointerOptions _presentationPointer = TeachingPointerOptions.Default;
    public event EventHandler? OverlayFailed;

    public bool IsRunning => _isRunning;

    public async Task<bool> StartPresentationAsync(TeachingPointerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || _disposed) return false;
        Stop();
        _presentationPointer = TeachingPointerOptions.Normalize(options);
        Start(CursorEffectMode.PresentationStick);
        var ready = _currentMode == CursorEffectMode.PresentationStick ? _presentationReady : null;
        if (ready is null) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { return await ready.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Stop(); throw; }
        catch (OperationCanceledException) { Stop(); return false; }
    }

    public void Start(CursorEffectMode mode)
    {
        lock (_stateLock)
        {
            if (_disposed || !OperatingSystem.IsWindows() || _isRunning || _workerThread?.IsAlive == true ||
                mode == CursorEffectMode.Default || mode == CursorEffectMode.Hidden)
            {
                return;
            }

            _currentMode = mode;
            _isRunning = true;
            _overlayFailureReported = 0;
            _presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "CursorHighlightOverlayThread"
            };
            try { _workerThread.Start(); }
            catch (Exception error)
            {
                _isRunning = false;
                _presentationReady.TrySetResult(false);
                Log.Warning("Cursor decoration thread unavailable: {ErrorType}", error.GetType().Name);
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _isRunning = false;
            _presentationReady?.TrySetResult(false);
        }

        if (_workerThread != null && _workerThread.IsAlive)
        {
            if (_workerThread.Join(1000)) _workerThread = null;
            else Log.Warning("Cursor overlay thread shutdown unconfirmed; no duplicate will be started.");
        }
    }

    private void WorkerLoop()
    {
        nint previousDpi = 0;
        try
        {
            previousDpi = SetThreadDpiAwarenessContext(-4);
            if (previousDpi == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            RunWorkerLoop();
        }
        catch (Exception error)
        {
            _presentationReady?.TrySetResult(false);
            Log.Warning("Optional cursor overlay failed: {ErrorType}, nativeError={NativeError}",
                error.GetType().Name, error is Win32Exception win32 ? win32.NativeErrorCode : (int?)null);
            if (Interlocked.Exchange(ref _overlayFailureReported, 1) == 0)
            {
                try { OverlayFailed?.Invoke(this, EventArgs.Empty); }
                catch (Exception subscriberError) { Log.Warning("Cursor warning subscriber failed: {ErrorType}", subscriberError.GetType().Name); }
            }
        }
        finally
        {
            _isRunning = false;
            _presentationReady?.TrySetResult(false);
            if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private void RunWorkerLoop()
    {
        using var frameWaiter=OverlayFrameWaiter.CreateWindows();
        var frameClock=new OverlayFrameClock(System.Diagnostics.Stopwatch.Frequency,
            System.Diagnostics.Stopwatch.GetTimestamp,frameWaiter.Wait);
        var artwork = _currentMode == CursorEffectMode.PresentationStick ? TeachingPointerArtwork.Get(_presentationPointer) : null;
        var width = artwork?.Width ?? OverlaySize;
        var height = artwork?.Height ?? OverlaySize;
        var hotspotX = (int)Math.Round(artwork?.HotspotX ?? HalfSize);
        var hotspotY = (int)Math.Round(artwork?.HotspotY ?? HalfSize);
        EnsureClassRegistered();
        nint hwnd = 0, screenDc = 0, memDc = 0, hBitmap = 0, oldBmp = 0;
        try
        {
            hwnd = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
                "OpenCamCursorHighlightClass", "OpenCamCursorHighlight", WS_POPUP,
                -1000, -1000, width, height, 0, 0, GetModuleHandle(null), 0);
            if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            screenDc = GetDC(0);
            memDc = CreateCompatibleDC(screenDc);
            if (screenDc == 0 || memDc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            hBitmap = CreateDIBSection(memDc, ref bmi, 0, out var pBits, 0, 0);
            if (hBitmap == 0 || pBits == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            oldBmp = SelectObject(memDc, hBitmap);
            if (oldBmp == 0 || oldBmp == new nint(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var pixelBuffer = new byte[width * height * 4];
            bool wasLeftDown = false;
            int rippleFrame = 0;
            const int rippleTotalFrames = 18;
            bool firstUpdate=true;
            int lastX=0,lastY=0;
            if (_currentMode == CursorEffectMode.PresentationStick)
            {
                RenderStick(pixelBuffer, _presentationPointer);
                Marshal.Copy(pixelBuffer, 0, pBits, pixelBuffer.Length);
            }
            while (_isRunning)
            {
                if (!GetPhysicalCursorPos(out var pt)) throw new Win32Exception(Marshal.GetLastWin32Error());

                bool isLeftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (_currentMode == CursorEffectMode.HaloWithClickRipple)
                {
                    if (isLeftDown && !wasLeftDown)
                    {
                        rippleFrame = 1; // 激發點擊水波紋
                    }
                    else if (rippleFrame > 0 && rippleFrame < rippleTotalFrames)
                    {
                        rippleFrame++;
                    }
                    else if (rippleFrame >= rippleTotalFrames)
                    {
                        rippleFrame = 0;
                    }
                }
                wasLeftDown = isLeftDown;

                // 繪製光圈與波紋到緩衝區
                if (_currentMode != CursorEffectMode.PresentationStick)
                {
                    RenderCircle(pixelBuffer, rippleFrame, rippleTotalFrames);
                    Marshal.Copy(pixelBuffer, 0, pBits, pixelBuffer.Length);
                }

                // 更新視窗位置與內容
                var wndPoint = new POINT { X = pt.X - hotspotX, Y = pt.Y - hotspotY };
                var srcPoint = new POINT { X = 0, Y = 0 };
                var wndSize = new SIZE { cx = width, cy = height };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                var moved=firstUpdate||wndPoint.X!=lastX||wndPoint.Y!=lastY;
                // Stick pixels are immutable: upload once, then move the existing
                // surface. Animated halos/ripples still upload their current pixels.
                if ((firstUpdate||_currentMode!=CursorEffectMode.PresentationStick) &&
                    !UpdateLayeredWindow(hwnd, screenDc, ref wndPoint, ref wndSize, memDc, ref srcPoint, 0, ref blend, ULW_ALPHA))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if ((moved||_currentMode!=CursorEffectMode.PresentationStick) &&
                    !SetWindowPos(hwnd, HWND_TOPMOST, wndPoint.X, wndPoint.Y, width, height, SWP_NOACTIVATE | SWP_NOSIZE | SWP_SHOWWINDOW))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                firstUpdate=false;lastX=wndPoint.X;lastY=wndPoint.Y;
                _presentationReady?.TrySetResult(true);
                frameClock.NoteUpdate(moved);
                frameClock.WaitNext();
            }
        }
        finally
        {
            if (hwnd != 0) ShowWindow(hwnd, SW_HIDE);
            if (oldBmp != 0 && oldBmp != new nint(-1) && memDc != 0) SelectObject(memDc, oldBmp);
            if (hBitmap != 0) DeleteObject(hBitmap);
            if (memDc != 0) DeleteDC(memDc);
            if (screenDc != 0) ReleaseDC(0, screenDc);
            if (hwnd != 0) DestroyWindow(hwnd);
            try
            {
                Log.Information("Cursor overlay pacing: {Measurement:l}",System.Text.Json.JsonSerializer.Serialize(
                    new{Mode=_currentMode.ToString(),Style=_presentationPointer.Style.ToString(),
                        Size=_presentationPointer.Size.ToString(),Timer=frameWaiter.Mode,Metrics=frameClock.Snapshot()}));
            }
            catch(Exception error) when(error is not OutOfMemoryException)
            {Log.Debug("Cannot report optional overlay timing: {ErrorType}",error.GetType().Name);}
        }
    }

    private static void RenderStick(byte[] buffer, TeachingPointerOptions options)
    {
        var artwork = TeachingPointerArtwork.Get(options);
        if (buffer.Length != artwork.Pixels.Length) throw new ArgumentException("Invalid teaching pointer buffer.");
        artwork.Pixels.Span.CopyTo(buffer);
        // UpdateLayeredWindow consumes premultiplied BGRA, not straight RGBA.
        for (var i = 0; i < buffer.Length; i += 4)
        {
            var red = buffer[i]; var blue = buffer[i + 2]; var alpha = buffer[i + 3];
            buffer[i] = (byte)((blue * alpha + 127) / 255);
            buffer[i + 1] = (byte)((buffer[i + 1] * alpha + 127) / 255);
            buffer[i + 2] = (byte)((red * alpha + 127) / 255);
        }
    }

    private static void RenderCircle(byte[] buffer, int rippleFrame, int rippleMaxFrames)
    {
        Array.Clear(buffer, 0, buffer.Length);

        const int cx = HalfSize;
        const int cy = HalfSize;
        const double haloRadius = 24.0;
        const double innerRadius = 22.0;

        // 亮黃色聚光圈 (#FFEB3B: R=255, G=235, B=59)
        const byte haloR = 255;
        const byte haloG = 235;
        const byte haloB = 59;
        const byte haloAlpha = 115; // 柔和聚光半透明

        // 金黃外框線 (#FDD835)
        const byte borderR = 253;
        const byte borderG = 216;
        const byte borderB = 53;
        const byte borderAlpha = 200;

        // 預乘 Alpha (Premultiplied Alpha)
        byte preHaloR = (byte)(haloR * haloAlpha / 255);
        byte preHaloG = (byte)(haloG * haloAlpha / 255);
        byte preHaloB = (byte)(haloB * haloAlpha / 255);

        byte preBorderR = (byte)(borderR * borderAlpha / 255);
        byte preBorderG = (byte)(borderG * borderAlpha / 255);
        byte preBorderB = (byte)(borderB * borderAlpha / 255);

        // 點擊波紋參數
        double rippleRadius = 0;
        byte preRippleR = 0, preRippleG = 0, preRippleB = 0, rippleA = 0;
        if (rippleFrame > 0)
        {
            double progress = (double)rippleFrame / rippleMaxFrames;
            rippleRadius = haloRadius + (progress * 30.0); // 24 -> 54px
            rippleA = (byte)((1.0 - progress) * 190);      // 190 -> 0

            preRippleR = (byte)(255 * rippleA / 255);
            preRippleG = (byte)(110 * rippleA / 255); // 暖橘紅色波紋
            preRippleB = (byte)(64 * rippleA / 255);
        }

        for (int y = 0; y < OverlaySize; y++)
        {
            int dy = y - cy;
            int dy2 = dy * dy;

            for (int x = 0; x < OverlaySize; x++)
            {
                int dx = x - cx;
                double dist = Math.Sqrt(dx * dx + dy2);

                int idx = (y * OverlaySize + x) * 4;

                // 1. 基礎黃色光圈與金邊
                if (dist <= innerRadius)
                {
                    buffer[idx] = preHaloB;
                    buffer[idx + 1] = preHaloG;
                    buffer[idx + 2] = preHaloR;
                    buffer[idx + 3] = haloAlpha;
                }
                else if (dist <= haloRadius)
                {
                    buffer[idx] = preBorderB;
                    buffer[idx + 1] = preBorderG;
                    buffer[idx + 2] = preBorderR;
                    buffer[idx + 3] = borderAlpha;
                }
                else if (dist <= haloRadius + 1.5)
                {
                    // 柔和羽化外緣
                    double edgeFade = 1.0 - ((dist - haloRadius) / 1.5);
                    byte a = (byte)(borderAlpha * edgeFade);
                    buffer[idx] = (byte)(borderB * a / 255);
                    buffer[idx + 1] = (byte)(borderG * a / 255);
                    buffer[idx + 2] = (byte)(borderR * a / 255);
                    buffer[idx + 3] = a;
                }

                // 2. 點擊水波紋環 (Ripple Ring: 寬度約 3px)
                if (rippleFrame > 0 && Math.Abs(dist - rippleRadius) <= 1.8)
                {
                    double ringAlphaScale = 1.0 - (Math.Abs(dist - rippleRadius) / 1.8);
                    byte currentRingA = (byte)(rippleA * ringAlphaScale);

                    // Alpha Blending 疊加在光圈上
                    byte existingA = buffer[idx + 3];
                    if (currentRingA > existingA)
                    {
                        buffer[idx] = (byte)(preRippleB * ringAlphaScale);
                        buffer[idx + 1] = (byte)(preRippleG * ringAlphaScale);
                        buffer[idx + 2] = (byte)(preRippleR * ringAlphaScale);
                        buffer[idx + 3] = currentRingA;
                    }
                }
            }
        }
    }

    private static void EnsureClassRegistered()
    {
        lock (s_classLock)
        {
            if (s_classRegistered) return;

            s_wndProcDelegate = DefWindowProc;
            var wcx = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = "OpenCamCursorHighlightClass",
                hIconSm = IntPtr.Zero
            };

            if (RegisterClassEx(ref wcx) == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            s_classRegistered = true;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
