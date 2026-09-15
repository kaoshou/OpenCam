using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Windows.Cursor;

[SupportedOSPlatform("windows")]
public class WindowsCursorHighlightService : ICursorHighlightService
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
    private static extern bool GetCursorPos(out POINT lpPoint);

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

    public bool IsRunning => _isRunning;

    public void Start(CursorEffectMode mode)
    {
        lock (_stateLock)
        {
            if (_isRunning || mode == CursorEffectMode.Default || mode == CursorEffectMode.Hidden)
            {
                return;
            }

            _currentMode = mode;
            _isRunning = true;

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "CursorHighlightOverlayThread"
            };
            _workerThread.Start();
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
        }

        if (_workerThread != null && _workerThread.IsAlive)
        {
            _workerThread.Join(1000);
            _workerThread = null;
        }
    }

    private void WorkerLoop()
    {
        EnsureClassRegistered();

        var hInstance = GetModuleHandle(null);
        var hwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            "OpenCamCursorHighlightClass",
            "OpenCamCursorHighlight",
            WS_POPUP,
            -1000, -1000, OverlaySize, OverlaySize,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            _isRunning = false;
            return;
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = OverlaySize;
        bmi.bmiHeader.biHeight = -OverlaySize; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        var hBitmap = CreateDIBSection(memDc, ref bmi, 0, out var pBits, IntPtr.Zero, 0);
        var oldBmp = SelectObject(memDc, hBitmap);

        ShowWindow(hwnd, SWP_SHOWWINDOW);

        var pixelBuffer = new byte[OverlaySize * OverlaySize * 4];

        bool wasLeftDown = false;
        int rippleFrame = 0;
        const int rippleTotalFrames = 18; // 約 300ms 動畫

        try
        {
            while (_isRunning)
            {
                GetCursorPos(out var pt);

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
                RenderCircle(pixelBuffer, rippleFrame, rippleTotalFrames);
                Marshal.Copy(pixelBuffer, 0, pBits, pixelBuffer.Length);

                // 更新視窗位置與內容
                var wndPoint = new POINT { X = pt.X - HalfSize, Y = pt.Y - HalfSize };
                var srcPoint = new POINT { X = 0, Y = 0 };
                var wndSize = new SIZE { cx = OverlaySize, cy = OverlaySize };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(hwnd, screenDc, ref wndPoint, ref wndSize, memDc, ref srcPoint, 0, ref blend, ULW_ALPHA);
                SetWindowPos(hwnd, HWND_TOPMOST, wndPoint.X, wndPoint.Y, OverlaySize, OverlaySize, SWP_NOACTIVATE | SWP_NOSIZE | SWP_SHOWWINDOW);

                Thread.Sleep(16); // ~60 FPS
            }
        }
        finally
        {
            ShowWindow(hwnd, SW_HIDE);
            SelectObject(memDc, oldBmp);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            DestroyWindow(hwnd);
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

            RegisterClassEx(ref wcx);
            s_classRegistered = true;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
