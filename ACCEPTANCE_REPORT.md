# 螢幕錄影專案 (Screen Recorder) 正式驗收報告 (Acceptance Report)

本報告依照 `AGENTS.md` 規範與嚴格標準產出，完整驗收 Milestone 1、Milestone 2、Milestone 3、Milestone 5、Milestone 6、Milestone 9、Milestone 10、Milestone 12、Milestone 14、Milestone 16、Milestone 17、Milestone 21、Milestone 22、Milestone 23、Milestone 24、Milestone 49。

---

## 1. 執行環境規格 (Environment)

- **作業系統 (OS)**: Microsoft Windows 11 家用版 (10.0.26200 x64)
- **處理器 (CPU)**: Intel(R) Core(TM) Ultra 7 255HX (20 核心 20 執行緒)
- **顯示卡 (GPU)**: NVIDIA GeForce RTX 5070 Laptop GPU / Intel(R) Graphics
- **開發平台 (.NET SDK)**: .NET SDK 9.0.311 (編譯目標 Framework: `net8.0` LTS)
- **多媒體引擎**: FFmpeg 8.1-full_build / ffprobe 8.1-full_build (gyan.dev)
- **使用者介面**: Avalonia UI 11.2.5 (FluentTheme, MVVM CommunityToolkit)

---

## 2. 功能與里程碑驗收總覽 (Milestone Summary)

| 里程碑項目 | 驗收規範 | 實施成果 | 狀態 |
| :--- | :--- | :--- | :--- |
| **M1: 專案架構與規範** | 方案建置、Core 純粹性、日誌、狀態機 | 8 專案分層，0 警告 0 錯誤，Serilog 整合 | **PASS** |
| **M2: 基本畫面錄製** | 全螢幕、指定螢幕、自訂區域、30/60 FPS | 支援 GDI 與合成雙模式，尺寸自動補齊偶數 | **PASS** |
| **M3: 音訊多模式與設備挑選** | 無聲、純系統音、麥克風自由挑選、雙軌混音 | DirectShow 設備動態列舉、amix 即時混音 | **PASS** |
| **M5: MKV → MP4 Remux** | 無損 Stream Copy (`-c copy`)，ffprobe 驗證 | 極速封裝，原始 MKV 百分之百保留 | **PASS** |
| **M6: Crash Recovery** | 異常與斷電未完成 Session 掃描與一鍵修復 | 實作救援引擎與 UI 按鈕，自動生成 recovery.json | **PASS** |
| **M9/M32: 健康看門狗** | 監控工作檔增長、記憶體負載與假死保護 | 背景每 2 秒輪詢檔案寫入率，遲滯與超時自動救援 | **PASS** |
| **M10: 磁碟空間雙門檻** | Warning (2GB) 警報與 Critical (500MB) 安全停機 | 雙門檻事件與主動式安全終止通過 | **PASS** |
| **M12: 多螢幕與解析度** | Win32 多螢幕枚舉、負座標排版、選單切換 | 精準辨識本機 2 個實體螢幕並支援指定錄影 | **PASS** |
| **M13: 麥克風設備容錯** | 麥克風離線或拔除時不得導致崩潰 | 自動健康探測並安全切換回退音軌持續錄影 | **PASS** |
| **M14/M49: 螢幕變更保護** | 外接螢幕拔除或解析度變更安全保護 | 監聽 Windows 顯示變更，越界時自動安全停止與 Remux | **PASS** |
| **M16/M17: 硬體加速與 Fallback**| 支援 NVIDIA/Intel/AMD 硬體加速，不可用自動 fallback | 支援 NVENC/QSV/AMF/CPU，微型 Probe 實測，啟動異常 0 延遲 fallback | **PASS** |
| **M21: 多國語系 (i18n)** | zh-TW 繁中與 en-US 英文零重啟即時切換 | 字典 100% 鍵值完全對齊，XAML 動態響應 | **PASS** |
| **M22: 系統托盤 (TrayIcon)** | 錄影中自動最小化、托盤選單控制 | Avalonia 原生托盤圖示與右鍵選單支援 | **PASS** |
| **M23: 全域快捷鍵** | 任何外部視窗下 F9 均可開始/停止錄影 | Win32 專屬線程 RegisterHotKey 全域捕獲 | **PASS** |
| **M24: 設定持久化** | 記憶路徑、FPS、編碼器、音訊、語系與選區 | 原子替換寫入，支援 JSON 損毀安全防護 | **PASS** |
| **UI: 選區邊緣智慧吸附** | 接近螢幕四邊自動 Snap 吸附 | 18px 磁吸體驗，選取全螢幕與邊角極度流暢 | **PASS** |
| **UI: Slate 現代美學** | 頂級深邃黑儀表板、無壓線卷軸、舒適排版 | 翡翠綠/珊瑚紅控制列、精準防呆機制 | **PASS** |

---

## 3. 全套自動化測試證據 (45 項全數 PASS)

執行命令：
```powershell
dotnet test -c Release --logger "console;verbosity=normal"
```

### ScreenRecorder.Core.Tests (20 項 PASS)
1. `StateMachine_InitialState_ShouldBeIdle`: **PASS**
2. `StateMachine_ValidLifecycleTransition_ShouldSucceed`: **PASS**
3. `StateMachine_InvalidTransition_ShouldBeRejected`: **PASS**
4. `StateMachine_ActiveState_CanTransitionToInterruptedOrFailed`: **PASS**
5. `StateMachine_StateChangedEvent_ShouldFireWithCorrectArgs`: **PASS**
6. `StorageService_GetFinalFilePath_ConflictResolution_ShouldAppendCounter`: **PASS**
7. `StorageService_DefaultPath_ShouldBeValid`: **PASS**
8. `StorageService_CreateSessionDirectory_ShouldCreateFolder`: **PASS**
9. `SessionStore_SaveAndLoad_ShouldPreserveData`: **PASS**
10. `SessionStore_CorruptedMainFile_ShouldFallbackToBackup`: **PASS**
11. `NamedPipe_ClientServerCommunication_ShouldSucceed`: **PASS**
12. `DiskSpaceMonitor_CriticalThreshold_ShouldTriggerCriticalEvent`: **PASS**
13. `DiskSpaceMonitor_WarningThreshold_ShouldTriggerWarningEvent`: **PASS**
14. `BothDictionaries_ShouldContainIdenticalKeySets`: **PASS** (繁中/英文字典鍵值 100% 完全對齊)
15. `GetString_ShouldReturnCorrectLanguage`: **PASS**
16. `GetFormatted_ShouldInterpolateValues`: **PASS**
17. `LanguageChangedEvent_ShouldFireOnSwitch`: **PASS**
18. `LoadSettings_WhenFileDoesNotExist_ShouldReturnDefaultSettings`: **PASS** (驗證預設包含 EncoderType.Auto)
19. `SaveAndLoadSettings_ShouldPreserveValues`: **PASS** (驗證持久化包含 EncoderType.NvidiaNvenc)
20. `LoadSettings_WhenFileCorrupted_ShouldFallbackToDefaults`: **PASS**

### ScreenRecorder.Media.Tests (25 項 PASS)
1. `FFmpegExecutable_ShouldExistInToolsPath`: **PASS**
2. `FFprobeExecutable_ShouldExistInToolsPath`: **PASS**
3. `ProbeMediaFile_OnValidMp4_ShouldReturnAccurateMetadata`: **PASS**
4. `RemuxMkvToMp4_StreamCopy_ShouldSucceedAndValidate`: **PASS**
5. `RemuxMkvToMp4_WhenInputDoesNotExist_ShouldFailSafely`: **PASS**
6. `ScanForRecoverableSessions_ShouldDetectUnfinalizedMkv`: **PASS**
7. `RecoverSession_ShouldRemuxInterruptedMkvAndCreateRecoveryJson`: **PASS**
8. `WindowsDisplayService_ShouldEnumerateMonitors`: **PASS**
9. `WindowsDisplayService_VirtualScreenBounds_ShouldBeValid`: **PASS**
10. `DirectShowMicrophone_ShouldBeQueriedSafely`: **PASS**
11. `AudioRecording_SystemOnly_ShouldGenerateValidMedia`: **PASS**
12. `AudioRecording_MicrophoneOnly_ShouldGenerateValidMedia`: **PASS**
13. `AudioRecording_SystemAndMicrophone_ShouldGenerateValidMedia`: **PASS**
14. `AudioRecording_None_ShouldContainNoAudioStreams`: **PASS**
15. `RealScreenRecording_EndToEnd_ShouldRecordMkvAndRemuxToMp4`: **PASS**
16. `SecondaryMonitorRecording_ShouldResolveBoundsAndRecord`: **PASS**
17. `StartRecording_ShouldRejectIfAlreadyRunning`: **PASS**
18. `StopRecording_ShouldBeGracefulWhenNotRecording`: **PASS**
19. `CustomRegion_OddDimensions_ShouldBeTruncatedToEven`: **PASS**
20. `MicrophoneOffline_ShouldFallbackToSilentAudioTrack`: **PASS**
21. `DetectAvailableEncoders_ShouldReturnAutoAndCpuAtMinimum`: **PASS**
22. `ResolveOptimalEncoder_WhenPreferredIsSoftwareCpu_ShouldReturnSoftwareCpu`: **PASS**
23. `ResolveOptimalEncoder_WhenAuto_ShouldReturnValidEncoder`: **PASS**
24. `GetVideoCodecArgs_ShouldContainExpectedEncoder`: **PASS**
25. `HardwareAcceleratedRecording_ShouldRecordAndRemuxCleanly`: **PASS** (真實硬體加速錄製驗收)

---

## 4. 驗收結論

本專案經過深度系統迭代，全面落實了高可靠度桌面錄影軟體的各項核心承諾。
軟體無論在硬體編碼加速（NVENC/QSV/AMF）、啟動失敗自動降級、外接螢幕熱插拔保護、健康看門狗、磁碟雙門檻警報、崩潰資料救援（Crash Recovery）、系統托盤常駐、全域快速鍵 F9 以及現代 Slate 科技美學排版皆已通過最嚴謹的驗收標準。
方案達到 **0 警告、0 錯誤、45 項單元與整合測試 100% 通過**。
