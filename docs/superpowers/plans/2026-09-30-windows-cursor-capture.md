# Windows 游標閃爍：可選 Desktop Duplication 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供 Windows 新版擷取測試模式，驗證能否消除「實際螢幕游標閃爍、MP4 游標正常」問題，同時保留現有錄影音訊與救援能力。

**Architecture:** 沿用 FFmpeg 程序與現有分段生命週期，在 Windows 平台增加不可變擷取計畫、DXGI 輸出對應及 ddagrab 輸入。舊 GDI 維持預設；首段確認前可有限次備援，首段確認後固定擷取後端與編碼器。

**Tech Stack:** .NET 8、Avalonia、既有 FFmpeg、Windows D3D11/DXGI 原生介面、xUnit。不新增 NuGet 套件。

**Spec:** `docs/superpowers/specs/2026-09-29-windows-capture-performance-design.md` 第 6–8 節。該書面設計已同意；本計畫待審閱，不代表產品功能已完成。

## Global Constraints

- 基準 v0.2.4 / `13b1723bf3fe6f29454ab2f58ab358f8fbedefaf`；不自動改版號、推送、合併或發布。
- 相容擷取（GDI）維持預設；macOS 不顯示或套用 Windows 擷取設定。
- 保留 MKV、安全 remux、救援、防覆寫、四種音源及波形；不改 IPC 認證或分流機制。
- 不偷偷降低解析度、FPS、畫質，亦不永久隱藏游標作為閃爍修正。
- 不變更依賴版本、FFmpeg 來源或授權。無支援能力時明確回退，不下載另一套 FFmpeg。
- 目前無受影響 Windows 實機；自動化通過不代表游標問題已解決。使用者不需現在補測。
- 保留主工作目錄兩份既有未追蹤文件；實作前建立隔離 worktree。

## Review Focus

1. 多 GPU 上 UI 螢幕編號不等於 DXGI output_idx：須以裝置名稱、實體矩形與 adapter 身分對應；無法證明時回退。（Task 2）
2. 暫停後顯示器重排／拔除：不可依舊索引錄到另一個螢幕。（Task 4）
3. 影片進度仍增加但桌面已鎖定或輸出失效：不可無限重複舊畫格且宣稱正常。（Task 4）
4. 成功首畫格後保存 session 失敗：不可當成無資料的啟動失敗重試。（Task 4）
5. 舊設定、未知 enum、跨平台同步設定：不可意外啟用新後端或呼叫 Windows DLL。（Task 1、5）

## 具體取捨

- 第一版採 `ddagrab -> hwdownload -> format=bgra`，再走既有編碼器轉色與編碼。先確保相容，不宣稱零拷貝或 CPU 必降。
- 使用 lavfi 作為視訊 input 0，保留音訊 input 索引及既有混音。避免第一版將所有音訊重排為新 filter graph。
- lavfi 中的 ddagrab 使用自身建立的預設 D3D11 裝置：第一版僅啟用能證明對應該裝置 adapter 的輸出。其他 GPU、旋轉輸出、跨螢幕區域及不明拓樸回退 GDI；不虛構 `adapter_idx` 選項，也不假設全域 `-filter_hw_device` 必定傳入 lavfi。
- 此限制不等於只支援雙螢幕：預設 adapter 上任意數量、可唯一對應的輸出均可選。
- 偏好「新版」與實際「GDI」分開保存／顯示。回退不得偷偷覆寫使用者偏好。

## Task 1：設定與不可變擷取合約

**Files:** 新增 Core `Enums/WindowsCaptureMode.cs`、`Models/CaptureSelection.cs`；修改 `Models/RecordingConfiguration.cs`、`UserSettings.cs`、`RecordingSession.cs`、`RecorderTelemetry.cs`。測試放 `tests/ScreenRecorder.Core.Tests/RecordingConfigurationTests.cs` 及新增 `CaptureSelectionTests.cs`。

**Interfaces:**

- `WindowsCaptureMode { CompatibleGdi = 0, ModernExperimental = 1 }`；UserSettings、RecordingConfiguration 同名屬性預設為 0。
- `CaptureBackend { PlatformDefault = 0, Gdi = 1, DesktopDuplication = 2 }`。
- `CaptureFallbackReason { None, FilterUnavailable, MappingUncertain, UnsupportedTopology, StartupFailed }`。
- `CaptureSelection(CaptureBackend Backend, CaptureFallbackReason FallbackReason, string? DeviceName, long? AdapterLuid, int? OutputIndex, CaptureRegion Bounds)`：record，僅資料，不攜帶命令字串／原生指標。Session、Telemetry 增加 nullable `CaptureSelection`。

- [ ] 新增 RED 測試：`MissingModeDefaultsToGdi`、`UnknownModeNormalizesToGdi`、`PausedSettingsCannotChangeCaptureMode`、`OldSessionWithoutCaptureSelectionLoads`。斷言保留音源／游標暫停修改，擷取偏好不變。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Core.Tests --filter 'FullyQualifiedName~CaptureSelection|FullyQualifiedName~RecordingConfiguration'`，確認新測試先失敗。
- [ ] 實作合約與設定正規化；不得信任 IPC 傳來的實際選擇，實際值由 Recorder 生成。
- [ ] 重跑上述測試至 PASS，提交本 Task 明確檔案。

## Task 2：Windows 輸出對應與決策

**Files:** 新增 `src/ScreenRecorder.Platform.Windows/Capture/DxgiOutputCatalog.cs`、`WindowsCapturePlanner.cs`；測試新增 `tests/ScreenRecorder.Media.Tests/WindowsCapturePlannerTests.cs`。

**Interfaces:**

- `DxgiOutputInfo(long AdapterLuid, int OutputIndex, string DeviceName, CaptureRegion Bounds, bool Attached, bool IdentityRotation, bool IsDefaultAdapter)`。
- `IDxgiOutputCatalog.GetOutputs(): IReadOnlyList<DxgiOutputInfo>`；Windows 原生實作透過 D3D11 預設硬體裝置取得 adapter 身分並列舉輸出，確保 COM／device/context 全數釋放。非 Windows 不呼叫原生 API。
- `WindowsCapturePlanner.Select(RecordingConfiguration config, CaptureRegion normalizedBounds, IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<DxgiOutputInfo> outputs, bool filterAvailable): CaptureSelection`，純函式，建構子不執行原生程式。

- [ ] RED 測試：UI 索引 2 對應 DXGI output 0；左側負座標轉相對座標；3 個以上輸出；不同 DPI 使用實體矩形；重複／鏡像矩形無唯一身分；跨輸出；非預設 adapter；旋轉；離線；整數加法溢位。斷言不明時 GDI + 精確原因，不默認主螢幕。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter FullyQualifiedName~WindowsCapturePlanner`，確認 RED。
- [ ] 實作查詢及純決策；只有完全包含且裝置名稱／矩形唯一吻合的預設 adapter 輸出採 DDA。尺寸先沿用引擎既有偶數規則；相對 offset 用 checked arithmetic。
- [ ] 重跑測試，增加 Windows-only opt-in 原生列舉檢查；無桌面時標 SKIP，不假冒 PASS。提交本 Task。

## Task 3：FFmpeg 擷取計畫與能力檢查

**Files:** 新增 `src/ScreenRecorder.Media/Capture/ICapturePlanProvider.cs`、`CaptureLaunchPlan.cs`；新增 Windows `Capture/WindowsCapturePlanProvider.cs`、`DdagrabCapabilityProbe.cs`；修改 `WindowsFFmpegProvider.cs`。新增測試 `WindowsCaptureArgumentsTests.cs`、`DdagrabCapabilityProbeTests.cs`。

**Interfaces:**

- `CaptureLaunchPlan(CaptureSelection Selection, string VideoInputArguments)`：只在程序內由可信平台元件產生。
- `ICapturePlanProvider.PrepareAsync(RecordingConfiguration config, CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token): Task<CaptureLaunchPlan>`。
- `ICapturePlanProvider.BuildInputArguments(CaptureLaunchPlan plan, RecordingConfiguration config, bool hasDirectShowMic, string? systemAudioPipeArg, string? microphoneAudioPipeArg): string`；舊平台維持既有 IFFmpegPlatformProvider 路徑，不强迫 macOS 實作此介面。
- `DdagrabCapabilityProbe.IsAvailableAsync(string ffmpegPath, CancellationToken token): Task<bool>`：沿用有界程序執行模式，3 秒檢查 `-hide_banner -h filter=ddagrab`，2 秒清理期限；核對退出碼和所需選項，不只看 exit 0。取消傳出；程序清理失敗停止啟動，不當成可安全回退。

- [ ] RED 測試：缺 filter／缺必要選項／非零／輸出過量／逾時／取消；舊 GDI 參數保持一致；4 音源模式 input 索引；預設及隱藏游標；惡意或非有限數值不得進參數；平台預設不走 DDA。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter 'FullyQualifiedName~WindowsCaptureArguments|FullyQualifiedName~DdagrabCapabilityProbe'`，確認 RED。
- [ ] DDA 視訊輸入固定形式：`-f lavfi -i "ddagrab=output_idx=N:framerate=FPS:draw_mouse=M:video_size=WxH:offset_x=X:offset_y=Y:output_fmt=bgra:dup_frames=1,hwdownload,format=bgra"`；數值全部來自已驗證計畫。正式首畫格握手仍是成功依據。
- [ ] 抽出 Windows provider 的音訊組合供兩種視訊輸入共用，保留 WASAPI、DirectShow、波形與輸出參數。能力檢查在開啟音訊管線前完成；不另錄預檢桌面造成額外啟動負擔。
- [ ] 重跑測試及既有 Windows provider 真實合成 FFmpeg 分段測試至 PASS；提交本 Task。

## Task 4：首段安全回退、續錄與輸出失效

**Files:** 修改 `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`、`src/ScreenRecorder.Recorder/Services/RecordingEngineFactory.cs`、`RecordingOrchestrator.cs`、`src/ScreenRecorder.Recorder/Program.cs`；新增 `CaptureStartupException.cs`、Windows `Capture/WindowsCaptureHealthMonitor.cs`。測試新增 `RecordingCaptureLifecycleTests.cs`，擴充 `RecordingEncoderLifecycleTests.cs`、`RecorderHealthTrackerTests.cs`。

**Interfaces:**

- `IRecordingEngineFactory.Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null)`；所有 fake 工廠同步改合約。
- `IScreenRecorderEngine.ActiveCapture: CaptureSelection?`，只有確認啟動才發布；引擎 optional `ICapturePlanProvider`。
- `CaptureStartupException` 限新擷取正式初始化失敗，與取消、磁碟／權限、音源、保存／清理錯誤區分；未知 FFmpeg 錯誤不得直接誤報硬體編碼失敗。
- `WindowsCaptureHealthMonitor.Check(CaptureSelection selection): bool`：核對目前輸出身分／矩形及可存取的互動桌面；由既有 watchdog 每 2 秒檢查 DDA。停止／暫停時解除；不放在同步 UI 呼叫路徑。

- [ ] RED 故障注入：DDA 首段失敗後一次 GDI；新路徑不覆寫；清理失敗不得再開程序；取消不得回退；確認首段後保存失敗不得重試；續錄輸出移位／索引重排不錄錯螢幕；鎖定／拔除即使 FFmpeg 進度增加也啟動既有安全停止。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter 'FullyQualifiedName~RecordingCaptureLifecycle|FullyQualifiedName~RecordingEncoderLifecycle|FullyQualifiedName~RecorderHealthTracker'` 確認 RED。
- [ ] 將擷取計畫準備放在音訊啟動之前；沿用首畫格確認、獨立 segment 路徑、failed_attempt 隔離與引擎所有權清理。首次最多 DDA/選定 encoder → GDI/同 encoder → GDI/CPU 三次，最後 CPU 只限明確編碼啟動失敗；清理失敗中止全部重試。未確認但有位元組的嘗試仍保留，不加入自動合併。
- [ ] 首段後 pin backend、目標装置、bounds 與 encoder；續錄重新確認同一身分，可重新解析 output_idx，不得換目標／後端。不相容則保留片段並回可恢復暫停或安全失敗狀態。
- [ ] 用既有安全停止處理桌面／拓樸失效；DDA 自動重複靜態畫格本身不是失效，不以畫面沒變就停止。檢查錯誤不得維持假正常。
- [ ] 重跑故障注入、安全救援及雙段 stream-copy／ffprobe／解碼測試至 PASS，提交本 Task。

## Task 5：中英 UI 與回歸驗收

**Files:** 修改 UI `ViewModels/SettingsViewModel.cs`、`MainViewModel.cs`、`Views/SettingsWindow.axaml`、`MainWindow.axaml`；Core `Localization/LocalizationService.cs`；相關 settings／session 配置複製點；`README.md`、`docs/USER_GUIDE.zh-TW.md` 與 `docs/USER_GUIDE.en-US.md`（實作前先核實既有 guide 路徑，不新建重複指南）；新增 `docs/verification/2026-09-30-windows-capture.md`。測試新增 `CaptureStatusTests.cs`，擴充 SettingsViewModelTests。

**Interfaces:** 沿用 Task 1 的 WindowsCaptureMode／CaptureSelection；中英文文案由 LocalizationService 取得，不以偏好冒充實際模式。

- [ ] RED UI 測試：Windows 待機可切換；Preparing／Recording／Paused 均鎖定；macOS 不顯示亦不套用；舊 settings 預設 GDI；新 session 不沿用上一場實際模式；GDI 回退明確顯示原因。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter 'FullyQualifiedName~CaptureStatus|FullyQualifiedName~SettingsViewModel'` 確認 RED；加入兩個選項「相容擷取（GDI）／Compatible capture (GDI)」與「新版擷取（測試）／Modern capture (experimental)」，並告知適用於嘗試改善部分電腦游標閃爍、不是保證修復。
- [ ] 重跑相關測試，檢查所有 configuration 複製／IPC 驗證／保存路徑；保留錄影中原有參數鎖定、暫停時音源與游標可改。
- [ ] 執行 `dotnet restore ScreenRecorder.sln`、`dotnet build ScreenRecorder.sln -c Release --no-restore`、`dotnet test ScreenRecorder.sln -c Release --no-restore`、`npm test --prefix website`、`npm run build --prefix website`、`bash tests/native/OpenCamSystemAudioTests.sh`、`git diff --check`。本機若仍只有 .NET 10，顯式以 `DOTNET_ROLL_FORWARD=Major` 執行並記錄限制，產品不改 target。
- [ ] Windows 人工矩陣：1080p30、原始游標，GDI/DDA 各三次；同時觀察實體螢幕和 MP4。覆蓋 Intel／AMD／NVIDIA、負座標、3+ 螢幕、旋轉回退、跨 GPU／跨螢幕回退、4 音源各 5 分鐘、暫停切換音源／游標、30 分鐘同步、失去顯示器、鎖定、程序中斷救援。不具環境的項目標 BLOCKED；不要求使用者當下測試。
- [ ] 保存測試命令、結果、日誌及 ffprobe 摘要，分列「自動化通過」與「實機待驗證」。提交本 Task；分支整體審查後交付，不自行推送／release。

## 自檢與交付界線

已對照原設計：正確螢幕對應、圖像格式、音訊索引、回退、續錄、生命週期、顯示失效、雙語設定與實機限制均有對應 Task。預設 adapter／旋轉限制明列為首版安全回退，不承諾全部 GPU 都走 DDA。原設計第二階段 IPC 分流不屬本計畫，也不作為本次修復的前置依賴。

實作驗收前，唯一成果是本計畫；不得將它描述為游標已修復。發布仍需另獲指示。

## 參考

- [FFmpeg ddagrab 文件](https://ffmpeg.org/ffmpeg-filters.html#ddagrab)：D3D11 影格、游標與輸出選項。
- [FFmpeg ddagrab 原始碼](https://ffmpeg.org/doxygen/trunk/vsrc__ddagrab_8c_source.html)：未提供 device context 時建立預設 D3D11 裝置。執行時需核對發布包實際 FFmpeg 能力，不能只依上游 trunk。
