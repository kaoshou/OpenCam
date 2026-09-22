// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;

namespace ScreenRecorder.Core.Models;

public class UserSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.ZhTw;
    public HardwareEncoderType EncoderType { get; set; } = HardwareEncoderType.Auto;
    public CursorEffectMode CursorEffect { get; set; } = CursorEffectMode.Default;
    public int Fps { get; set; } = 30;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; } = false;
    public string? SelectedMicrophoneId { get; set; }
    public string CaptureRangeMode { get; set; } = "Monitor"; // Monitor, CustomRegion
    public int SelectedMonitorIndex { get; set; } = 0;
    public int RegionX { get; set; } = 100;
    public int RegionY { get; set; } = 100;
    public int RegionWidth { get; set; } = 1280;
    public int RegionHeight { get; set; } = 720;
    public string? CustomOutputDirectory { get; set; }
    public bool MinimizeOnRecord { get; set; } = false;

    // 自訂快捷鍵 (0=None, 1=Alt, 2=Ctrl, 4=Shift, 8=Win)
    public uint StartStopHotkeyModifiers { get; set; } = 0;
    public string StartStopHotkeyKey { get; set; } = "F9";
    public uint PauseResumeHotkeyModifiers { get; set; } = 0;
    public string PauseResumeHotkeyKey { get; set; } = "F10";

    // 視訊品質與行為自訂 (CRF: 18=Ultra, 23=Standard, 28=Compact)
    public string VideoQualityPreset { get; set; } = "Standard";
    public bool OpenFolderOnFinished { get; set; } = false;
    public bool DeleteWorkingFileAfterSuccessfulRemux { get; set; } = false;

    // 磁碟安全保護門檻
    public double DiskWarningThresholdGb { get; set; } = 2.0;
    public double DiskCriticalThresholdMb { get; set; } = 500.0;
}
