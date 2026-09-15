Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$code = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

public class Capturer
{
    [DllImport(`"user32.dll`")]
    public static extern bool SetProcessDPIAware();

    [DllImport(`"user32.dll`")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(`"user32.dll`")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport(`"user32.dll`")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport(`"user32.dll`", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBmp, uint nFlags);

    [DllImport(`"user32.dll`")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport(`"user32.dll`")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport(`"user32.dll`", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(`"user32.dll`")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static List<IntPtr> FindWindows(string keyword)
    {
        SetProcessDPIAware();
        List<IntPtr> found = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            if (!IsWindowVisible(hWnd)) return true;
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            string title = sb.ToString();
            if (title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void SendF2(IntPtr hWnd)
    {
        // WM_KEYDOWN = 0x0100
        // VK_F2 = 0x71
        PostMessage(hWnd, 0x0100, 0x71, 0);
        System.Threading.Thread.Sleep(100);
        // WM_KEYUP = 0x0101
        PostMessage(hWnd, 0x0101, 0x71, 0);
    }

    public static bool CaptureWindow(IntPtr hWnd, string savePath)
    {
        SetProcessDPIAware();
        if (hWnd == IntPtr.Zero) return false;

        ShowWindow(hWnd, 9); // SW_RESTORE
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(500);

        RECT rect;
        if (!GetWindowRect(hWnd, out rect)) return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0) return false;

        using (Bitmap bmp = new Bitmap(width, height))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    bool pwSuccess = PrintWindow(hWnd, hdc, 2);
                    if (!pwSuccess)
                    {
                        PrintWindow(hWnd, hdc, 0);
                    }
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            bmp.Save(savePath, ImageFormat.Png);
        }
        return true;
    }
}
"@

Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$appPath = "c:\Work\螢幕錄影專案\src\ScreenRecorder.UI\bin\Release\net8.0\ScreenRecorder.UI.exe"
$outDir = "c:\Work\螢幕錄影專案\docs\images"
$settingsPath = Join-Path $env:LOCALAPPDATA "ScreenRecorder\user_settings.json"

function Capture-Workflow([string]$mainOut, [string]$settingsOut, [int]$lang) {
    Get-Process -Name "ScreenRecorder.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name "ScreenRecorder.Recorder" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600

    $settingsObj = @{
        Language = $lang
        RecordSystemAudio = $true
        RecordMicrophone = $false
        Fps = 30
    }
    $settingsObj | ConvertTo-Json | Set-Content -Path $settingsPath -Encoding UTF8

    Write-Host "Starting App (Lang=$lang)..."
    $proc = Start-Process -FilePath $appPath -PassThru

    $mainHwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Milliseconds 600
        $wins = [Capturer]::FindWindows("OpenCam")
        if ($wins.Count -gt 0) { 
            $mainHwnd = $wins[0]
            break 
        }
    }

    if ($mainHwnd -ne [IntPtr]::Zero) {
        Write-Host "Found Main Window, capturing settings"
        [Capturer]::SetForegroundWindow($mainHwnd)
        Start-Sleep -Milliseconds 300
        [Capturer]::SendF2($mainHwnd)
        Start-Sleep -Milliseconds 1500

        $settingsHwnd = [IntPtr]::Zero
        for ($k = 0; $k -lt 10; $k++) {
            $wins = [Capturer]::FindWindows("OpenCam")
            foreach ($w in $wins) {
                if ($w -ne $mainHwnd) {
                    $settingsHwnd = $w
                    break
                }
            }
            if ($settingsHwnd -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 400
        }

        if ($settingsHwnd -ne [IntPtr]::Zero) {
            Write-Host "Found Settings Window"
            Start-Sleep -Milliseconds 500
            [Capturer]::CaptureWindow($settingsHwnd, $settingsOut)
        }
    }

    Stop-Process -Id $proc.Id -Force
    Get-Process -Name "ScreenRecorder.Recorder" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600
}

Capture-Workflow -mainOut "$outDir\preview_main_zhtw.png" -settingsOut "$outDir\preview_settings_zhtw.png" -lang 0
Capture-Workflow -mainOut "$outDir\preview_main_enus.png" -settingsOut "$outDir\preview_settings_enus.png" -lang 1
Write-Host "Done"
