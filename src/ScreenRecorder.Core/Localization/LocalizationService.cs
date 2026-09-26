// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Localization;

public class LocalizationService : ILocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private AppLanguage _currentLanguage = AppLanguage.ZhTw;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                LanguageChanged?.Invoke(this, value);
            }
        }
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        var dict = _currentLanguage == AppLanguage.ZhTw ? ZhTwDictionary : EnUsDictionary;
        if (dict.TryGetValue(key, out var val))
        {
            return val;
        }

        // Fallback to ZhTw
        if (ZhTwDictionary.TryGetValue(key, out var fallbackVal))
        {
            return fallbackVal;
        }

        return key;
    }

    public string GetFormatted(string key, params object[] args)
    {
        var template = GetString(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    public static readonly Dictionary<string, string> ZhTwDictionary = new()
    {
        // 視窗標題與通用
        ["AppTitle"] = "OpenCam - 螢幕錄影工具",
        ["Confirm"] = "確認",
        ["Cancel"] = "取消",
        ["Close"] = "關閉",

        // 頂部控制列
        ["StartRecording"] = "開始錄影",
        ["StopRecording"] = "停止錄影",
        ["PauseRecording"] = "暫停",
        ["ResumeRecording"] = "繼續",
        ["OpenFolder"] = "開啟資料夾",
        ["Recovery"] = "修復救援",
        ["About"] = "關於",
        ["Language"] = "語言",
        ["TooltipStart"] = "開始螢幕錄影",
        ["TooltipStop"] = "安全結束錄製並無損封裝為 MP4",
        ["TooltipPause"] = "暫停錄影",
        ["TooltipResume"] = "繼續錄影",
        ["TooltipStartFormat"] = "開始螢幕錄影 [快捷鍵: {0}]",
        ["TooltipStopFormat"] = "安全結束錄製並無損封裝為 MP4 [快捷鍵: {0}]",
        ["TooltipPauseFormat"] = "暫停錄影 [快捷鍵: {0}]",
        ["TooltipResumeFormat"] = "繼續錄影 [快捷鍵: {0}]",
        ["TooltipFolder"] = "開啟錄影輸出資料夾 [快捷鍵: Ctrl+O]",
        ["TooltipRecovery"] = "掃描並無損轉出之前未正常結束的錄影檔案",
        ["TooltipAbout"] = "關於本程式、作者與第三方函式庫授權聲明 [快捷鍵: F1]",
        ["TooltipLanguage"] = "切換顯示語言 (Language)",

        // 準備中橫幅
        ["PreparingBanner"] = "正在初始化錄影管線與音訊捕捉，請稍候（錄影開始前請暫勿切換畫面）...",
        ["StartingBadge"] = "啟動中...",

        // 錄影目標摘要
        ["TargetFullScreen"] = "錄影目標：【全螢幕】",
        ["TargetMonitor"] = "錄影目標：【指定螢幕 - {0}】",
        ["TargetCustomRegion"] = "錄影目標：【自訂區域 - {0}x{1} (坐標 X:{2}, Y:{3})】",

        // 配置卡片
        ["ConfigTitle"] = "錄影配置",
        ["RangeTitle"] = "錄影範圍",
        ["RangeFullScreen"] = "全螢幕 (Full Screen)",
        ["RangeMonitor"] = "指定螢幕:",
        ["IdentifyDisplays"] = "辨識螢幕",
        ["IdentifyDisplaysTooltip"] = "在每個螢幕短暫顯示對應編號",
        ["DisplayIdentificationSelected"] = "目前選取",
        ["DisplayIdentificationIncomplete"] = "有 {0} 個螢幕無法可靠辨識；未顯示可能錯誤的編號。",
        ["RangeCustomRegion"] = "自訂矩形區域 (Custom Region)",
        ["AdjustRegion"] = "調整選區...",
        ["TooltipAdjustRegion"] = "點選以拖曳或縮放自訂錄影區域 [支援 Enter 確認與 ESC 取消]",
        ["FpsTitle"] = "幀率 (FPS):",
        ["AudioTitle"] = "音訊設定",
        ["AudioSystem"] = "錄製系統聲音",
        ["AudioSystemUnsupportedMac"] = "macOS 系統聲音（此版本尚未支援）",
        ["AudioMic"] = "錄製麥克風 (Microphone)",
        ["MicDevice"] = "麥克風設備:",
        ["StorageTitle"] = "儲存位置",
        ["ChangeFolder"] = "變更...",
        ["TooltipChangeFolder"] = "選擇其他錄影存放資料夾 [磁碟空間將即時切換]",

        // 狀態監控儀表板
        ["MonitorTitle"] = "錄影狀態監控",
        ["AudioMeterSystem"] = "系統聲音",
        ["AudioMeterMicrophone"] = "麥克風",
        ["AudioMeterOff"] = "未啟用",
        ["AudioMeterLive"] = "收音中",
        ["AudioMeterSilent"] = "無聲",
        ["AudioMeterPaused"] = "已暫停",
        ["AudioMeterUnavailable"] = "無法監測",
        ["StatusIdle"] = "待命中",
        ["StatusRecording"] = "錄影中",
        ["StatusPaused"] = "已暫停",
        ["StatusPreparing"] = "初始化中",
        ["FileSizeLabel"] = "已錄大小",
        ["DiskRemainingLabel"] = "磁碟剩餘",
        ["SafeContainerNotice"] = "MKV 連續安全寫入 · 停止自動無損 Remux MP4",

        // 狀態列動態訊息
        ["StatusReady"] = "準備就緒，點擊「開始錄影」即可錄製",
        ["StatusInitializing"] = "正在初始化錄影管線，請稍候並暫勿操作畫面...",
        ["StatusStartUnconfirmed"] = "尚無法確認錄影是否已開始；請保持程式開啟，或按「停止」安全結束。",
        ["StatusScreenPermissionRequired"] = "需要 macOS 螢幕錄製權限；請在「系統設定 → 隱私權與安全性 → 螢幕與系統音訊錄製」允許 OpenCam，然後重新啟動。",
        ["StatusRecordingActive"] = "正在錄影中 (安全工作容器 MKV 連續寫入)",
        ["StatusDiskSpaceWarning"] = "磁碟剩餘空間偏低：{0:F1} GB；低於危急門檻時將安全停止錄影",
        ["StatusRecorderHealthWarning"] = "錄影狀態異常：{0}",
        ["StatusPausedMsg"] = "錄影已暫停 (已保留前段內容)，點擊「繼續」繼續錄製",
        ["StatusResuming"] = "正在繼續錄影管線，請稍候...",
        ["StatusStopping"] = "正在安全關閉 MKV 並執行 MP4 無損封裝 (Remux)...",
        ["StatusSuccess"] = "錄影成功！已轉出 MP4 檔案",
        ["StatusFailed"] = "啟動失敗: {0}",
        ["StatusStopFailed"] = "停止失敗: {0}",
        ["StatusRecoverableDetected"] = "偵測到 {0} 個未正常結束之錄影工作階段！點擊「修復救援」即可一鍵轉出 MP4。",
        ["StatusNoRecoverable"] = "目前沒有需要救援的錄影工作階段。",
        ["StatusRecovering"] = "正在執行工作階段崩潰復原，請稍候...",
        ["StatusRecoverSuccess"] = "救援完成！成功救回 {0} 個工作階段並已無損轉為 MP4。",
        ["StatusRecoverPartial"] = "救援完成：完整救回 {0} 個、部分救回 {1} 個、失敗 {2} 個。部分救援可能缺少損壞的尾段，原始 MKV 均已保留。",
        ["StatusRecoverFailed"] = "救援失敗：{0} 個工作階段未能轉出。原始 MKV 仍已保留，請查看日誌。",
        ["StatusLocationUpdated"] = "儲存位置已更新為: {0}",
        ["StatusSetLocationFailed"] = "設定儲存目錄失敗: {0}",
        ["StatusOpenFolderFailed"] = "無法開啟儲存位置：{0}",
        ["StatusUnexpectedDisconnect"] = "錄影核心無預警中斷！已錄製內容已安全保留，可點擊「修復救援」轉出 MP4。",
        ["RecordingCloseWarningTitle"] = "錄影進行中",
        ["RecordingCloseWarningMessage"] = "目前正在錄影，請先停止錄影後再關閉程式。",
        ["RecordingCloseWarningDetail"] = "這次關閉要求已取消，OpenCam 會繼續錄影。",
        ["ReturnToRecording"] = "返回錄影",
        ["RecordingCloseUnconfirmedMessage"] = "錄影狀態尚未確認，且安全停止未成功。",
        ["RecordingCloseUnconfirmedDetail"] = "可保持程式開啟繼續等待；強制結束可能中斷影片，之後需使用修復救援。",
        ["ForceQuitButton"] = "強制結束…",
        ["ForceQuitConfirmTitle"] = "確認強制結束",
        ["ForceQuitConfirmMessage"] = "這會立即結束 OpenCam 與錄影程序，錄到一半的 MKV 可能需要修復救援。",
        ["ForceQuitConfirmDetail"] = "只有在安全停止失敗、錄影狀態無法確認時才使用。此動作無法復原。",
        ["ForceQuitCancel"] = "取消，保持開啟",
        ["ForceQuitConfirmAction"] = "確認強制結束",
        ["VersionAndAbout"] = "OpenCam v0.2.1 · 關於",

        // 關於視窗
        ["AboutTitle"] = "關於 OpenCam",
        ["AppName"] = "OpenCam",
        ["VersionLabel"] = "版本: v0.2.1",
        ["AuthorLabel"] = "作者:",
        ["AuthorName"] = "鄭郁翰 (Yu-Han Cheng)",
        ["GithubLabel"] = "GitHub 專案首頁",
        ["ProjectLicenseLabel"] = "專案授權:",
        ["ProjectLicenseName"] = "AGPL-3.0-or-later",
        ["ProjectLicenseDetail"] = "OpenCam 原始碼依 GNU AGPL 第 3 版或更新版授權；完整條款及原始碼請見 GitHub 專案的 LICENSE 檔。無擔保提供。第三方元件各依其自身授權。",
        ["AppDescription"] = "一款簡單、穩定且具備極致可靠性的桌面螢幕錄影工具。\n核心特色包含：安全容器強制連續寫入、停止時無損轉碼 MP4、崩潰/斷電自動救援、支援多螢幕與自訂範圍、A/V 高精度同步。",
        ["ThirdPartyTitle"] = "第三方開源函式庫授權清單",
        ["LicenseFfmpegDesc"] = "依據 GNU Lesser General Public License (LGPL) 2.1 / GPL 3.0 授權使用其多媒體編碼與封裝管線。",
        ["LicenseAvaloniaDesc"] = "Copyright (c) .NET Foundation and Contributors. 採用 MIT License 授權。",
        ["LicenseMvvmDesc"] = "Copyright (c) .NET Foundation and Contributors. 採用 MIT License 授權。",
        ["LicenseSerilogDesc"] = "Copyright (c) Serilog Contributors. 採用 Apache License 2.0 授權。",
        ["LicenseXunitDesc"] = "Copyright (c) .NET Foundation and Contributors. 採用 Apache License 2.0 授權。",
        ["LicenseFfmpegCodeDesc"] = "本軟體多媒體核心採用 FFmpeg (ffmpeg.org) 程式碼，依 GPLv3 / LGPLv3 授權。",
        ["HotkeyCloseTip"] = "按 ESC 或 Enter 關閉",
        ["AboutReliabilityTitle"] = "核心可靠性架構 (Reliability First)",
        ["DisclaimerTitle"] = "免責聲明與使用條款",
        ["Disclaimer1Title"] = "1. 無保證條款：",
        ["Disclaimer1Text"] = "本軟體依「現狀」（AS IS）提供，不附任何明示或暗示之保證，包含但不限於商業適售性、特定用途之適用性、不侵權之保證。",
        ["Disclaimer2Title"] = "2. 風險自負：",
        ["Disclaimer2Text"] = "使用者應自行承擔使用本軟體之全部風險。",
        ["Disclaimer3Title"] = "3. 損害免責：",
        ["Disclaimer3Text"] = "對於本軟體導致之任何直接、間接、附帶、衍生性或懲罰性損害（含資料毀損、商業中斷、收益損失、商譽損害等），作者與貢獻者概不負責。",
        ["Disclaimer4Title"] = "4. 法規合規：",
        ["Disclaimer4Text"] = "涉及個人資料、敏感商業文件處理時，使用者應自行確保符合所在地之個人資料保護法、公司資安政策、以及相關法規（含我國個人資料保護法、營業秘密法）。",

        // 選區視窗
        ["RegionWindowTitle"] = "選擇錄影區域",
        ["RegionFitScreen"] = "滿版螢幕",
        ["TooltipRegionFitScreen"] = "快速將選取範圍貼齊放大至當前螢幕全螢幕",
        ["RegionConfirm"] = "確認選取 (Enter)",
        ["RegionCancel"] = "取消 (ESC)",
        ["RegionDragTip"] = "拖曳中間區域可平移選取範圍",
        ["RegionResizeTip"] = "拖曳四周邊框或四角錨點自由放大縮小 | 按下 ESC 取消 | Enter 或雙擊確認",
        ["RegionSizeFormat"] = "尺寸: {0} x {1} (坐標 X:{2}, Y:{3})",
        ["MonitorPrimaryFormat"] = "螢幕 {0} (主顯示器)",
        ["MonitorSecondaryFormat"] = "螢幕 {0} (副顯示器)",
        ["DefaultMicOption"] = "系統預設麥克風 (Default Microphone)",

        // 系統托盤與偏好設定
        ["MinimizeOnRecord"] = "開始錄影時自動最小化視窗",
        ["TrayShowWindow"] = "顯示主視窗",
        ["TrayStart"] = "開始錄影",
        ["TrayStop"] = "停止錄影",
        ["TrayExit"] = "結束程式",

        // 視訊編碼器與硬體加速
        ["EncoderTitle"] = "視訊編碼器:",
        ["EncoderAuto"] = "自動選擇 (優先硬體加速)",
        ["EncoderCpu"] = "CPU 軟體編碼 (libx264 相容穩定)",
        ["EncoderNvenc"] = "NVIDIA NVENC (硬體加速)",
        ["EncoderQsv"] = "Intel Quick Sync (QSV 硬體加速)",
        ["EncoderAmf"] = "AMD AMF (硬體加速)",
        ["DisplayChangedNotice"] = "偵測到螢幕解析度或顯示設定變更，已安全收斂停止並保存當前錄影。",
        ["WatchdogWarningNotice"] = "錄影寫入或影音管線出現遲滯，看門狗正全力維護錄影資料安全。",

        // 滑鼠游標效果
        ["CursorTitle"] = "游標樣式:",
        ["CursorDefault"] = "原始系統游標 (標準呈現)",
        ["CursorHighlight"] = "醒目黃色光圈 (教學推薦)",
        ["CursorClickRipple"] = "光圈 + 點擊波紋 (專業教學)",
        ["CursorHidden"] = "隱藏滑鼠游標 (不錄游標)",

        // 工具列與設定視窗
        ["Settings"] = "設定",
        ["TooltipSettings"] = "偏好設定與自訂快捷鍵 [快速鍵: F2]",
        ["SettingsTitle"] = "OpenCam 偏好設定",
        ["SettingsTabPreferencesNav"] = "偏好設定",
        ["SettingsTabAboutNav"] = "關於本程式",
        ["CloseButton"] = "關閉",
        ["SettingsTabHotkeys"] = "全域快捷鍵",
        ["SettingsTabGeneral"] = "一般與語言",
        ["SettingsTabQuality"] = "錄影品質與行為",
        ["SettingsTabStorage"] = "儲存與磁碟守護",

        // 快捷鍵自訂
        ["HotkeyStartStopLabel"] = "開始 / 停止錄影快捷鍵:",
        ["HotkeyPauseResumeLabel"] = "暫停 / 繼續錄影快捷鍵:",
        ["HotkeyModifierLabel"] = "修飾鍵:",
        ["HotkeyKeyLabel"] = "主按鍵:",
        ["HotkeyConflictError"] = "兩組快捷鍵不可完全相同，請重新配置！",
        ["HotkeyRegistrationFailed"] = "快捷鍵註冊失敗，可能已被其他應用程式佔用。",

        // 語言與介面
        ["LanguageLabel"] = "顯示語言 (Language):",
        ["LanguageOptionZhTw"] = "繁體中文 (Traditional Chinese)",
        ["LanguageOptionEnUs"] = "English (US)",

        // 畫質與行為
        ["VideoQualityLabel"] = "影片編碼畫質:",
        ["QualityUltra"] = "超高畫質（銳利清晰、檔案較大）",
        ["QualityStandard"] = "標準畫質（推薦平衡）",
        ["QualityCompact"] = "高壓縮節省空間（檔案較小）",
        ["OpenFolderOnFinishedLabel"] = "錄影停止並封裝完成後自動開啟目標資料夾",
        ["DeleteWorkingFileAfterRemuxLabel"] = "MP4 封裝完成後自動清理 MKV 工作檔",
        ["DeleteWorkingFileNotice"] = "安全提示：依專案高可靠規範，預設保留 MKV 原始檔以防外力損毀時能即時救援。",

        // 磁碟安全門檻
        ["DiskWarningThresholdLabel"] = "低剩餘空間警告門檻 (GB):",
        ["DiskCriticalThresholdLabel"] = "危急空間安全停止錄影門檻 (MB):",
        ["DiskThresholdNotice"] = "磁碟防護：當目標磁碟低於危急門檻時，OpenCam 會自動安全停止並保存已錄資料，防範硬碟塞滿崩潰。",

        // 設定按鈕與提示
        ["RecommendedTag"] = "推薦",
        ["SaveSettings"] = "儲存設定",
        ["ResetToDefaults"] = "還原預設值",
        ["SettingsSavedSuccess"] = "偏好設定已成功套用！"
    };

    public static readonly Dictionary<string, string> EnUsDictionary = new()
    {
        // 視窗標題與通用
        ["AppTitle"] = "OpenCam - Screen Recorder",
        ["Confirm"] = "Confirm",
        ["Cancel"] = "Cancel",
        ["Close"] = "Close",

        // 頂部控制列
        ["StartRecording"] = "Record",
        ["StopRecording"] = "Stop",
        ["PauseRecording"] = "Pause",
        ["ResumeRecording"] = "Resume",
        ["OpenFolder"] = "Open Folder",
        ["Recovery"] = "Recovery",
        ["About"] = "About",
        ["Language"] = "Language",
        ["TooltipStart"] = "Start screen recording",
        ["TooltipStop"] = "Safely stop recording and remux to MP4",
        ["TooltipPause"] = "Pause screen recording",
        ["TooltipResume"] = "Resume screen recording",
        ["TooltipStartFormat"] = "Start screen recording [Hotkey: {0}]",
        ["TooltipStopFormat"] = "Safely stop recording and remux to MP4 [Hotkey: {0}]",
        ["TooltipPauseFormat"] = "Pause screen recording [Hotkey: {0}]",
        ["TooltipResumeFormat"] = "Resume screen recording [Hotkey: {0}]",
        ["TooltipFolder"] = "Open output recordings folder [Hotkey: Ctrl+O]",
        ["TooltipRecovery"] = "Scan and recover interrupted recording sessions",
        ["TooltipAbout"] = "About this application, author, and licenses [Hotkey: F1]",
        ["TooltipLanguage"] = "Switch interface language",

        // 準備中橫幅
        ["PreparingBanner"] = "Initializing recording pipeline and audio capture, please wait...",
        ["StartingBadge"] = "Starting...",

        // 錄影目標摘要
        ["TargetFullScreen"] = "Target: [Full Screen]",
        ["TargetMonitor"] = "Target: [Monitor - {0}]",
        ["TargetCustomRegion"] = "Target: [Custom Region - {0}x{1} (X:{2}, Y:{3})]",

        // 配置卡片
        ["ConfigTitle"] = "Recording Configuration",
        ["RangeTitle"] = "Capture Range",
        ["RangeFullScreen"] = "Full Screen",
        ["RangeMonitor"] = "Select Monitor:",
        ["IdentifyDisplays"] = "Identify displays",
        ["IdentifyDisplaysTooltip"] = "Briefly show the corresponding number on each display",
        ["DisplayIdentificationSelected"] = "Selected",
        ["DisplayIdentificationIncomplete"] = "Could not reliably identify {0} display(s); uncertain numbers were omitted.",
        ["RangeCustomRegion"] = "Custom Region",
        ["AdjustRegion"] = "Adjust Region...",
        ["TooltipAdjustRegion"] = "Drag or resize custom recording region [Enter: Confirm, ESC: Cancel]",
        ["FpsTitle"] = "Frame Rate (FPS):",
        ["AudioTitle"] = "Audio Settings",
        ["AudioSystem"] = "Record System Audio",
        ["AudioSystemUnsupportedMac"] = "macOS system audio (not supported in this version)",
        ["AudioMic"] = "Record Microphone",
        ["MicDevice"] = "Microphone Device:",
        ["StorageTitle"] = "Output Location",
        ["ChangeFolder"] = "Change...",
        ["TooltipChangeFolder"] = "Change output directory [Disk space updates automatically]",

        // 狀態監控儀表板
        ["MonitorTitle"] = "Status Monitor",
        ["AudioMeterSystem"] = "System audio",
        ["AudioMeterMicrophone"] = "Microphone",
        ["AudioMeterOff"] = "Off",
        ["AudioMeterLive"] = "Live",
        ["AudioMeterSilent"] = "Silent",
        ["AudioMeterPaused"] = "Paused",
        ["AudioMeterUnavailable"] = "Unavailable",
        ["StatusIdle"] = "IDLE",
        ["StatusRecording"] = "RECORDING",
        ["StatusPaused"] = "PAUSED",
        ["StatusPreparing"] = "PREPARING",
        ["FileSizeLabel"] = "File Size",
        ["DiskRemainingLabel"] = "Available Disk",
        ["SafeContainerNotice"] = "Safe MKV Continuous Write · Lossless Remux MP4",

        // 狀態列動態訊息
        ["StatusReady"] = "Ready. Click 'Record' to start capturing.",
        ["StatusInitializing"] = "Initializing recording pipeline, please wait...",
        ["StatusStartUnconfirmed"] = "Recording startup is not yet confirmed. Keep OpenCam open, or press Stop to end it safely.",
        ["StatusScreenPermissionRequired"] = "macOS screen-recording permission is required. Allow OpenCam in System Settings > Privacy & Security > Screen & System Audio Recording, then restart the app.",
        ["StatusRecordingActive"] = "Recording in progress (Continuous write to safe MKV)",
        ["StatusDiskSpaceWarning"] = "Low disk space: {0:F1} GB remaining. Recording will stop safely at the critical threshold.",
        ["StatusRecorderHealthWarning"] = "Recorder health warning: {0}",
        ["StatusPausedMsg"] = "Recording paused. Click 'Resume' to continue.",
        ["StatusResuming"] = "Resuming recording pipeline, please wait...",
        ["StatusStopping"] = "Safely closing MKV and performing lossless MP4 remux...",
        ["StatusSuccess"] = "Recording finished! MP4 file exported successfully.",
        ["StatusFailed"] = "Failed to start: {0}",
        ["StatusStopFailed"] = "Failed to stop: {0}",
        ["StatusRecoverableDetected"] = "Detected {0} interrupted session(s)! Click 'Recovery' to export to MP4.",
        ["StatusNoRecoverable"] = "No interrupted recording sessions found.",
        ["StatusRecovering"] = "Performing session crash recovery, please wait...",
        ["StatusRecoverSuccess"] = "Recovery completed! Successfully restored {0} session(s) to MP4.",
        ["StatusRecoverPartial"] = "Recovery completed: {0} fully recovered, {1} partially recovered, and {2} failed. Partial results may omit a damaged tail; all original MKV files were preserved.",
        ["StatusRecoverFailed"] = "Recovery failed for {0} session(s). Original MKV files were preserved; check the logs for details.",
        ["StatusLocationUpdated"] = "Storage location updated to: {0}",
        ["StatusSetLocationFailed"] = "Failed to update storage directory: {0}",
        ["StatusOpenFolderFailed"] = "Unable to open the output location: {0}",
        ["StatusUnexpectedDisconnect"] = "Recording core disconnected! Captured data safely preserved. Click 'Recovery' to export MP4.",
        ["RecordingCloseWarningTitle"] = "Recording in Progress",
        ["RecordingCloseWarningMessage"] = "Stop the recording before closing OpenCam.",
        ["RecordingCloseWarningDetail"] = "The close request was canceled and recording will continue.",
        ["ReturnToRecording"] = "Return to Recording",
        ["RecordingCloseUnconfirmedMessage"] = "Recording status is unknown and a safe stop did not succeed.",
        ["RecordingCloseUnconfirmedDetail"] = "Keep OpenCam open and wait, or force quit. Force quitting may interrupt the video and require recovery.",
        ["ForceQuitButton"] = "Force Quit…",
        ["ForceQuitConfirmTitle"] = "Confirm Force Quit",
        ["ForceQuitConfirmMessage"] = "This immediately closes OpenCam and its recorder. The unfinished MKV may need recovery.",
        ["ForceQuitConfirmDetail"] = "Use only when safe stop failed and recording status cannot be confirmed. This cannot be undone.",
        ["ForceQuitCancel"] = "Cancel and Keep Open",
        ["ForceQuitConfirmAction"] = "Confirm Force Quit",
        ["VersionAndAbout"] = "OpenCam v0.2.1 · About",

        // 關於視窗
        ["AboutTitle"] = "About OpenCam",
        ["AppName"] = "OpenCam",
        ["VersionLabel"] = "Version: v0.2.1",
        ["AuthorLabel"] = "Author:",
        ["AuthorName"] = "Yu-Han Cheng (鄭郁翰)",
        ["GithubLabel"] = "GitHub Project",
        ["ProjectLicenseLabel"] = "Project license:",
        ["ProjectLicenseName"] = "AGPL-3.0-or-later",
        ["ProjectLicenseDetail"] = "OpenCam source is licensed under GNU AGPL version 3 or later. See LICENSE in the GitHub repository for the full terms and source code. Provided without warranty. Third-party components retain their own licenses.",
        ["AppDescription"] = "A simple, stable, and highly reliable desktop screen recording tool.\nKey Features: Continuous MKV safe container writing, lossless MP4 remuxing on graceful stop, crash and power-loss recovery, full/multi-monitor/custom region capture, and precise A/V sync.",
        ["ThirdPartyTitle"] = "Third-Party Open Source Licenses",
        ["LicenseFfmpegDesc"] = "Multimedia encoding and muxing pipeline licensed under GNU Lesser General Public License (LGPL) 2.1 / GPL 3.0.",
        ["LicenseAvaloniaDesc"] = "Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License.",
        ["LicenseMvvmDesc"] = "Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License.",
        ["LicenseSerilogDesc"] = "Copyright (c) Serilog Contributors. Licensed under the Apache License 2.0.",
        ["LicenseXunitDesc"] = "Copyright (c) .NET Foundation and Contributors. Licensed under the Apache License 2.0.",
        ["LicenseFfmpegCodeDesc"] = "This software uses code of FFmpeg (ffmpeg.org) licensed under the GPLv3 / LGPLv3.",
        ["HotkeyCloseTip"] = "Press ESC or Enter to close",
        ["AboutReliabilityTitle"] = "Reliability First",
        ["DisclaimerTitle"] = "Disclaimer & Terms of Use",
        ["Disclaimer1Title"] = "1. No Warranty: ",
        ["Disclaimer1Text"] = "This software is provided 'AS IS', without any express or implied warranties, including but not limited to the implied warranties of merchantability and fitness for a particular purpose.",
        ["Disclaimer2Title"] = "2. Assumption of Risk: ",
        ["Disclaimer2Text"] = "The user assumes all risks associated with the use of this software.",
        ["Disclaimer3Title"] = "3. Limitation of Liability: ",
        ["Disclaimer3Text"] = "The authors shall not be liable for any damages arising out of the use of this software, including loss of data.",
        ["Disclaimer4Title"] = "4. Legal Compliance: ",
        ["Disclaimer4Text"] = "Users are responsible for ensuring compliance with their local privacy and security laws when recording.",

        // 選區視窗
        ["RegionWindowTitle"] = "Select Recording Region",
        ["RegionFitScreen"] = "Fit Screen",
        ["TooltipRegionFitScreen"] = "Quickly expand selection to fit the entire current screen",
        ["RegionConfirm"] = "Confirm (Enter)",
        ["RegionCancel"] = "Cancel (ESC)",
        ["RegionDragTip"] = "Drag inside area to move capture region",
        ["RegionResizeTip"] = "Drag borders or corners to resize | ESC to cancel | Enter or double-click to confirm",
        ["RegionSizeFormat"] = "Size: {0} x {1} (Pos X:{2}, Y:{3})",
        ["MonitorPrimaryFormat"] = "Monitor {0} (Primary)",
        ["MonitorSecondaryFormat"] = "Monitor {0} (Secondary)",
        ["DefaultMicOption"] = "Default System Microphone",

        // 系統托盤與偏好設定
        ["MinimizeOnRecord"] = "Minimize window when recording starts",
        ["TrayShowWindow"] = "Show Window",
        ["TrayStart"] = "Start Recording",
        ["TrayStop"] = "Stop Recording",
        ["TrayExit"] = "Exit",

        // 視訊編碼器與硬體加速
        ["EncoderTitle"] = "Video Encoder:",
        ["EncoderAuto"] = "Auto (Hardware Accelerated)",
        ["EncoderCpu"] = "CPU Software (libx264 Compatible)",
        ["EncoderNvenc"] = "NVIDIA NVENC (Hardware)",
        ["EncoderQsv"] = "Intel Quick Sync (QSV Hardware)",
        ["EncoderAmf"] = "AMD AMF (Hardware)",
        ["DisplayChangedNotice"] = "Display settings changed. Current recording safely finalized and saved.",
        ["WatchdogWarningNotice"] = "Recording write latency detected. Watchdog is safeguarding stream data.",

        // 滑鼠游標效果
        ["CursorTitle"] = "Mouse Cursor:",
        ["CursorDefault"] = "Default System Cursor",
        ["CursorHighlight"] = "Highlight Halo (Recommended)",
        ["CursorClickRipple"] = "Halo + Click Ripple (Tutorial)",
        ["CursorHidden"] = "Hide Cursor (Clean View)",

        // Toolbar & Settings Window
        ["Settings"] = "Settings",
        ["TooltipSettings"] = "Preferences and Custom Hotkeys [Hotkey: F2]",
        ["SettingsTitle"] = "OpenCam Preferences",
        ["SettingsTabPreferencesNav"] = "Preferences",
        ["SettingsTabAboutNav"] = "About OpenCam",
        ["CloseButton"] = "Close",
        ["SettingsTabHotkeys"] = "Global Hotkeys",
        ["SettingsTabGeneral"] = "General & Language",
        ["SettingsTabQuality"] = "Video Quality & Actions",
        ["SettingsTabStorage"] = "Storage & Disk Guard",

        // Hotkeys Customization
        ["HotkeyStartStopLabel"] = "Start / Stop Recording Hotkey:",
        ["HotkeyPauseResumeLabel"] = "Pause / Resume Recording Hotkey:",
        ["HotkeyModifierLabel"] = "Modifier:",
        ["HotkeyKeyLabel"] = "Key:",
        ["HotkeyConflictError"] = "Hotkeys cannot be identical. Please choose distinct keys!",
        ["HotkeyRegistrationFailed"] = "Failed to register hotkey. It may be in use by another application.",

        // Language & UI
        ["LanguageLabel"] = "Display Language:",
        ["LanguageOptionZhTw"] = "繁體中文 (Traditional Chinese)",
        ["LanguageOptionEnUs"] = "English (US)",

        // Quality & Actions
        ["VideoQualityLabel"] = "Video Encoding Quality:",
        ["QualityUltra"] = "Ultra Quality (sharper, larger files)",
        ["QualityStandard"] = "Standard Quality (recommended balance)",
        ["QualityCompact"] = "Compact Size (smaller files)",
        ["OpenFolderOnFinishedLabel"] = "Automatically open destination folder when recording finishes",
        ["DeleteWorkingFileAfterRemuxLabel"] = "Automatically clean up MKV working file after MP4 remux",
        ["DeleteWorkingFileNotice"] = "Safety Notice: By default, keeping MKV ensures maximum crash recovery capability.",

        // Disk Protection
        ["DiskWarningThresholdLabel"] = "Low Disk Space Warning Threshold (GB):",
        ["DiskCriticalThresholdLabel"] = "Critical Disk Space Auto-Stop Threshold (MB):",
        ["DiskThresholdNotice"] = "Disk Protection: OpenCam automatically stops and finalizes recording when disk space is critically low.",

        // Buttons & Messages
        ["RecommendedTag"] = "Recommended",
        ["SaveSettings"] = "Save Settings",
        ["ResetToDefaults"] = "Restore Defaults",
        ["SettingsSavedSuccess"] = "Preferences applied successfully!"
    };
}
