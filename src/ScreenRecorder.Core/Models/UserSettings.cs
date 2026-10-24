// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using System.Text.Json.Serialization;

namespace ScreenRecorder.Core.Models;

public class UserSettings
{
    private WebPlayerLanguageOptions _webPlayerLanguages = new();
    public WebPlayerLanguageOptions WebPlayerLanguages
    {
        get => WebPlayerLanguageOptions.Normalize(_webPlayerLanguages);
        set => _webPlayerLanguages = WebPlayerLanguageOptions.Normalize(value);
    }
    public List<string> RecentProjectPaths { get; set; } = [];
    public bool NativeCaptureEnabled { get; set; }
    public bool EditablePointerEnabled { get; set; }
    private WindowsCaptureMode _windowsCaptureMode = WindowsCaptureMode.Automatic;
    public WindowsCaptureMode WindowsCaptureMode
    {
        get => _windowsCaptureMode;
        set => _windowsCaptureMode = Enum.IsDefined(value) ? value : WindowsCaptureMode.CompatibleGdi;
    }

    public AppLanguage Language { get; set; } = AppLanguage.ZhTw;
    public HardwareEncoderType EncoderType { get; set; } = HardwareEncoderType.Auto;
    public CursorEffectMode CursorEffect { get; set; } = CursorEffectMode.Default;
    private TeachingPointerOptions _presentationPointer = TeachingPointerOptions.Default;
    public TeachingPointerOptions PresentationPointer
    {
        get => _presentationPointer;
        set => _presentationPointer = TeachingPointerOptions.Normalize(value);
    }
    private int _fps = 10;
    public int Fps
    {
        get => _fps;
        // Migrate the removed recording choice, not existing session metadata.
        set => _fps = value == 60 ? 30 : value;
    }
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
    private RecordingPreflightMode _recordingPreflightMode = RecordingPreflightMode.StartOnly;
    private bool _hasExplicitPreflightMode;
    public RecordingPreflightMode RecordingPreflightMode
    {
        get => _recordingPreflightMode;
        set
        {
            _hasExplicitPreflightMode = true;
            _recordingPreflightMode = Enum.IsDefined(value) ? value : RecordingPreflightMode.StartOnly;
        }
    }

    // Read old settings without writing the obsolete toggle back. An explicit
    // mode wins regardless of JSON property order; old opt-outs remain off.
    [JsonPropertyName("RecordingPreflightEnabled")]
    public bool LegacyRecordingPreflightEnabled
    {
        set
        {
            if (!_hasExplicitPreflightMode)
                _recordingPreflightMode = value ? RecordingPreflightMode.StartOnly : RecordingPreflightMode.Disabled;
        }
    }
    public bool MicrophoneSilenceReminderEnabled { get; set; } = true;

    // 自訂快捷鍵 (0=None, 1=Alt, 2=Ctrl, 4=Shift, 8=Win)
    public uint StartStopHotkeyModifiers { get; set; } = 0;
    public string StartStopHotkeyKey { get; set; } = "F9";
    public uint PauseResumeHotkeyModifiers { get; set; } = 0;
    public string PauseResumeHotkeyKey { get; set; } = "F10";
    public uint BookmarkHotkeyModifiers { get; set; } = 6;
    public string BookmarkHotkeyKey { get; set; } = "M";

    // 視訊品質與行為自訂 (CRF: 18=Ultra, 23=Standard, 28=Compact)
    public string VideoQualityPreset { get; set; } = "Standard";
    public bool OpenFolderOnFinished { get; set; } = false;
    public bool DeleteWorkingFileAfterSuccessfulRemux { get; set; } = false;

    // 磁碟安全保護門檻
    public double DiskWarningThresholdGb { get; set; } = 2.0;
    public double DiskCriticalThresholdMb { get; set; } = 500.0;
}
