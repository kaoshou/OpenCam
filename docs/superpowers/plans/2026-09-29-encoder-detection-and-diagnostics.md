# 編碼器偵測與診斷 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 統一編碼器探測、消除探測程序管理缺陷，重用可靠結果並顯示真正使用的編碼器，不破壞錄影音訊、續錄或救援。

**Architecture:** Media 層提供共用有界子程序執行器及偵測快取；UI 與 Recorder 各有獨立實例、相同政策。Recorder 在第一次成功啟動後固定此工作階段的實際編碼器，經既有遙測傳給 UI；Core 僅承載平台無關契約與資料。

**Tech Stack:** C# / .NET 8、Avalonia、xUnit、既有 FFmpeg 與 Serilog；不增加 NuGet 相依。

**Spec:** [Windows 錄影效能與游標穩定性設計](../specs/2026-09-29-windows-capture-performance-design.md)，本計畫僅實作第 4 節及相關不變條件。

狀態：使用者已確認並選擇同一代理依序實作；在隔離分支進行，不推送或發布。程式基準 `fd1f1f891602d80138adecb482483b634299d1e4`，設計文件提交 `04ec571`。實際結果另見驗證報告，本文的測試要求不是 PASS 宣告。

## Global Constraints

- 保留 MKV 工作檔、驗證後 MP4 無損封裝、既有救援與防覆寫保護。
- 保留四種音訊模式、系統聲音與麥克風的獨立控制及波形顯示。
- 不移除 HMAC、程序身分檢查、重播防護、認證期限、連線與訊息大小限制。
- 不偷偷降低解析度、FPS、畫質，亦不永久隱藏游標作為閃爍修正。
- 本次設計不變更版本號、依賴版本、FFmpeg 來源或授權；發布另依使用者指示及驗收結果處理。
- 已存在的兩份未追蹤計畫文件不改動、不納入本次文件提交。
- 不實作 IPC 查詢分流、`ddagrab` 或修改 GDI／macOS 擷取及音訊管線；不宣稱本階段已解決實際游標閃爍或降低多少 CPU。
- 讀取根目錄 `AGENTS.md` 及 Spec 後再執行；先建立隔離 worktree。下列指令的工作目錄均為該 worktree 根目錄。
- 每個任務先跑失敗測試、再最小修改、再回歸；提交僅包含該任務檔案，不使用 `git add .`。本計畫不授權 push、tag 或 Release。

## Review Focus

1. 子程序輸出單行數 MB、子程序未退出：持續排空且記憶體有界，逾時只終止自己啟動的程序。Task 1 的 `FloodWithoutNewlines_IsBoundedAndDrained`、`Timeout_LeavesUnrelatedProcessAlive` 負責。
2. 首位呼叫者取消而第二位仍需結果：不能把取消永久快取為硬體不可用，也不能留下同時執行的探測。Task 2 的 `CancelledCaller_DoesNotPoisonNextCaller` 負責。
3. FFmpeg 在同一路徑更新，或硬體先探測成功卻正式啟動失敗：失效舊結果，但不能改掉已成功錄製片段的編碼器。Tasks 2、3 的 identity／startup-failure／resume 測試負責。
4. UI 與 Recorder 偵測結果不同、舊遙測缺少新欄位：實際值以 Recorder 為準，未知不冒充 CPU 或硬體；使用者的 Auto 選項不被覆寫。Task 4 的顯示及序列化測試負責。
5. 續錄或音源失效重啟失敗：已完成片段仍可停止封裝／救援，失敗候選不能混入合併清單，不能永久停在假錄影狀態。Task 3 的分段交易及資料保留測試負責。

---

## 固定政策與責任邊界

- 每個探測程序執行上限 **3 秒**；每次完整偵測的工作預算 **9 秒**，含等待偵測鎖、版本查詢、探測及正常清理。預算到期不再開始候選，最後清理額外最多 **2 秒**。未完成清理即回報失敗、不再啟動其他候選。等待鎖逾時不終止別的呼叫者持有的程序，只記錄本次期限耗盡並回傳有原因的 CPU 選擇；不把未執行候選寫入失敗快取。
- 同一偵測器一次只執行一個探測。CPU 明確選取直接返回，**零探測子程序**。UI 預先列舉與 Recorder 的選擇不能共享記憶體快取。
- stdout、stderr 同時使用固定大小字元區塊讀取，不以 `ReadLine` 儲存無界長行；各保留最後 **4,096 個字元**。記錄到日誌前移除控制字元，版本字串限 **256 個字元**；不記錄環境變數或認證資料。
- 清理使用與呼叫者取消無關的期限；終止自己持有的 `Process`／子樹，等待退出與讀取工作結束。不得 `Kill` 所有 `ffmpeg`。清理未完成不得回報 Available。
- FFmpeg 身分以正規化完整路徑、檔案長度、`LastWriteTimeUtc` 及該檔案的 `-version` 首行記錄。每次偵測入口重查檔案資訊；資訊變動重讀版本並失效全部候選。版本查詢使用同一執行器及上述預算；失敗不沿用舊可用結果。不新增每次錄影雜湊整個二進位檔的負擔。
- Available 快取 **10 分鐘**；ProbeFailed／TimedOut／StartFailed 快取 **30 秒**；Cancelled／清理未完成不快取。正式硬體啟動失敗立即移除成功結果，該候選冷卻 **30 秒**；新 FFmpeg 身分不沿用冷卻。使用注入的 `TimeProvider` 驗證時間，預設 `TimeProvider.System`。
- Auto 依平台 provider 的順序選第一個 Available：Windows NVENC → QSV → AMF，macOS VideoToolbox；沒有成功候選才 CPU。指定硬體只驗證該候選，失敗回 CPU，不悄悄選另一家硬體。成功選到第一候選後不必為錄影啟動繼續探測；UI 完整列舉仍可探測其餘候選。
- 只有第一段尚未成功開始時，允許硬體正式啟動失敗後 **一次 CPU 回退**；取消不是硬體失敗，不回退。成功段後固定實際編碼器，不重探、不跨編碼器回退。
- 現有首次啟動 IPC 的 8 秒／macOS 有音訊 40 秒，以及後續 30 秒狀態確認機制先維持；測試必須覆蓋探測超過首輪期限仍不解鎖／重送開始。這不是第二階段的 IPC 壅塞修正。

## Task 1: 有界探測子程序與跨平台測試工具

**Files:**

- Create: `src/ScreenRecorder.Media/Encoders/EncoderProbeRunner.cs`（程序、輸出與清理）。
- Create: `src/ScreenRecorder.Media/Encoders/EncoderProbeResult.cs`（結果與政策）。
- Create: `tests/ScreenRecorder.TestChild/ScreenRecorder.TestChild.csproj`、`Program.cs`（僅供測試的 .NET 子程序）。
- Modify: `ScreenRecorder.sln`、`tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj`（建置並把測試子程序及 runtime 檔案複製到測試輸出；不加入產品封裝）。
- Test: `tests/ScreenRecorder.Media.Tests/EncoderProbeRunnerTests.cs`。

**Interfaces:**

- Consumes: `ProcessStartInfo`，必須 `UseShellExecute=false`、重導 stdout/stderr；呼叫方不傳 shell 命令。
- Produces: `enum EncoderProbeStatus { Available, ProbeFailed, TimedOut, Cancelled, StartFailed }`。
- Produces: `record EncoderProbeResult(EncoderProbeStatus Status, int? ExitCode, TimeSpan Elapsed, string StdoutTail, string StderrTail, bool CleanupCompleted)`。
- Produces: `IEncoderProbeRunner.RunAsync(ProcessStartInfo startInfo, TimeSpan executionTimeout, CancellationToken cancellationToken = default) : Task<EncoderProbeResult>`；由 `EncoderProbeRunner` 實作，清理期限固定 2 秒。
- Produces: `EncoderProbePolicy` 的 `ProbeTimeout`、`DetectionBudget`、`CleanupTimeout`、`SuccessCacheLifetime`、`FailureCacheLifetime`、`TailCharacterLimit` 常數／唯讀值，值如上。

- [ ] **1. 寫真實子程序測試。** 子程序模式：success、exit-7、flood、delay、wait；透過 `dotnet <fixture.dll> <mode>` 執行，不依賴 bash、PowerShell 或 GPU。測試以 PID 記錄辨識自己的程序，`finally` 清理測試子程序。
  - `SuccessAndFailure_ReportExitCode`：`Assert.Equal(Available, success.Status)`；`Assert.Equal(ProbeFailed, failed.Status)`；`Assert.Equal(7, failed.ExitCode)`。
  - `FloodWithoutNewlines_IsBoundedAndDrained`：兩個串流各連續寫入 2 MiB，`Assert.Equal(Available, result.Status)`；`Assert.InRange(result.StdoutTail.Length, 1, 4096)`，stderr 同樣，兩端最後的標記均存在。
  - `SlowInitialization_WithinBudgetSucceeds`：延遲 1.8 秒後退出，3 秒期限下 `Assert.Equal(Available, result.Status)`；測試不得依賴真實 GPU。
  - `TimeoutAndCancellation_ReapOwnedProcess`：在取得子程序 ready 訊號後觸發取消／給 300 ms 期限；分別斷言 Cancelled／TimedOut、`Assert.True(result.CleanupCompleted)`、`Assert.True(child.HasExited)`。
  - `Timeout_LeavesUnrelatedProcessAlive`：另開獨立 wait fixture；探測逾時後 `Assert.False(unrelated.HasExited)`。
  - `MissingExecutable_IsStartFailed`、`RepeatedTimeouts_DoNotAccumulateChildren`：不存在路徑為 StartFailed；連續 10 次逾時所有已啟動 PID 均退出。牆鐘測試保留排程容差，不把精確毫秒當 CPU 效能證據。
- [ ] **2. 跑 RED。** `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~EncoderProbeRunnerTests`；應因缺少新型別或未實作行為失敗，記下原因。
- [ ] **3. 實作上述介面與 fixture。** 先啟動兩端讀取，再非同步等退出；退出碼 0 且清理完整才 Available。使用 caller token／內部期限分辨取消與逾時；處理程序在 Kill 前自行退出的競態。reader 尾端有界但仍持續讀到 EOF。
- [ ] **4. 跑 GREEN。** 重跑同一命令，新增測試全部成功、無掛住或未回收 fixture；另確認 `dotnet build ScreenRecorder.sln -c Release` 成功。
- [ ] **5. 範圍提交。** 明列本任務檔案 `git add`，`git commit -m "fix: bound encoder probe processes and drain output"`。

## Task 2: 統一偵測政策、快取與診斷

**Files:**

- Create: `src/ScreenRecorder.Core/Models/EncoderSelection.cs`、`src/ScreenRecorder.Core/Interfaces/IEncoderSelectionService.cs`。
- Modify: `src/ScreenRecorder.Media/Encoders/FFmpegEncoderDetector.cs`。
- Test: `tests/ScreenRecorder.Media.Tests/EncoderDetectorTests.cs`。

**Interfaces:**

- Consumes: Task 1 的 `IEncoderProbeRunner`／結果／政策；既有 `IFFmpegPlatformProvider.GetHardwareEncoderProbes()` 與 `IEncoderDetector` 不改簽名。
- Produces: `enum EncoderFallbackReason { None, NoValidatedHardware, RequestedHardwareUnavailable, HardwareStartupFailed }`；`record EncoderSelection(HardwareEncoderType Encoder, EncoderFallbackReason FallbackReason)`（Core Models；Encoder 不得為 Auto）。
- Produces: `interface IEncoderSelectionService : IEncoderDetector`，新增 `Task<EncoderSelection> SelectAsync(HardwareEncoderType preferred, CancellationToken cancellationToken = default)` 及 `void ReportStartupFailure(HardwareEncoderType encoder)`。
- Produces: `FFmpegEncoderDetector(IFFmpegPlatformProvider platformProvider, string? ffmpegPath = null, IEncoderProbeRunner? probeRunner = null, TimeProvider? timeProvider = null)`；實作新介面，舊 constructor 呼叫仍成立；`ResolveOptimalEncoderAsync` 委派 `SelectAsync`。

- [ ] **1. 先新增失敗測試。** runner fake 記錄呼叫及結果，使用可手動前進的測試 `TimeProvider`，不實際等待 10 分鐘。
  - `ExplicitCpu_DoesNotProbe`：`Assert.Equal(SoftwareCpu, result.Encoder)`；`Assert.Empty(runner.Calls)`。
  - `Auto_UsesPlatformOrderIncludingVideoToolbox`：QSV 可用時選 QSV；mac provider 可用時 `Assert.Equal(AppleVideoToolbox, result.Encoder)`；選到可用候選後不執行後續候選。
  - `RequestedHardwareUnavailable_FallsBackWithReason`：`Assert.Equal(SoftwareCpu, result.Encoder)`；`Assert.Equal(RequestedHardwareUnavailable, result.FallbackReason)`。
  - `Cache_ExpiresSuccessAndFailureSeparately`：10 分鐘／30 秒邊界前呼叫數不增加，邊界後增加；`ReportStartupFailure` 立即不能再讀到該成功項目，30 秒後容許重試。
  - `ExecutableChangedAtSamePath_InvalidatesCandidates`：替換測試執行檔內容及 mtime，runner 收到新版本查詢與候選；新 path／新版本身分不沿用舊成功或冷卻。
  - `CancelledCaller_DoesNotPoisonNextCaller`：首位取消為 `OperationCanceledException`，第二位可成功；`Assert.Equal(1, runner.MaxConcurrentCalls)`，取消結果不寫入快取。
  - `DetectionBudget_StopsLaunchingCandidates`：9 秒工作預算用完，不開新候選；最後清理最多 2 秒；清理失敗拋明確錯誤、不宣稱找到 CPU 或硬體。
  - `Diagnostics_SeparateFailureKindsAndBoundText`：記錄 Type、status、exit code、duration、cache hit/miss、selection/fallback；不把 TimedOut 寫成 Unsupported，stderr 尾端至多 4096 字元、無控制字元及環境內容。
- [ ] **2. 跑 RED。** `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~EncoderDetectorTests`；新行為應失敗，不能以只確認 enum 有定義的既有測試代替。
- [ ] **3. 實作契約、單一候選管線及快取。** 使用同一 runner 執行 `-version` 與既有 256×256／30 FPS／0.05 秒 null-output 探測；參數來源限 provider 的既定 codec／extra args。偵測前重新檢查身分、鎖內複查快取；取消清理完才向 detector caller 拋取消。版本查詢失敗使本輪不重用硬體成功值，但可回報有原因的 CPU 選擇。
- [ ] **4. 跑 GREEN。** 重跑 detector 與 Task 1 測試；保留 Auto、CPU 清單及 UI 篩選相容性，確認 VideoToolbox 不再被 Windows-only priority 排除。
- [ ] **5. 範圍提交。** 明列本任務檔案，`git commit -m "fix: unify encoder detection caching and diagnostics"`。

## Task 3: 接入錄影生命週期並保護續錄片段

**Files:**

- Modify: `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`、`src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs`、`src/ScreenRecorder.Recorder/Program.cs`。
- Create: `src/ScreenRecorder.Recorder/Services/RecordingEngineFactory.cs`（建立引擎、注入 Recorder 共用 detector）。
- Modify: `src/ScreenRecorder.Core/Models/RecordingSession.cs`。
- Test: `tests/ScreenRecorder.Media.Tests/FFmpegStartupHandshakeTests.cs`、新增 `RecordingEncoderLifecycleTests.cs`；回歸 `AudioLevelsLifecycleTests.cs`、`AnonymousAudioTransportTests.cs`、`RecordingRecoveryTests.cs`、`RecordingStartCoordinatorTests.cs`。

**Interfaces:**

- Consumes: Task 2 的 `IEncoderSelectionService`／`EncoderSelection`。
- Preserves: `IScreenRecorderEngine.ActiveEncoder`；只有 `StartRecordingAsync` 成功返回後才讀取其值，不能把它的預設 CPU 值當成已啟動結果。不新增另一份引擎選擇狀態。
- Produces: 引擎 constructor 尾端增加 `IEncoderSelectionService? encoderSelectionService = null, HardwareEncoderType? pinnedEncoder = null`，原參數順序不變；pinnedEncoder 不接受 Auto。
- Produces: `IRecordingEngineFactory.Create(HardwareEncoderType? pinnedEncoder) : IScreenRecorderEngine`；`RecordingEngineFactory` 注入 provider、共用 selection service 與原有可選音訊服務。Orchestrator constructor 尾端依序加入 `IEncoderSelectionService? encoderSelectionService = null, IRecordingEngineFactory? engineFactory = null`；未注入時建立一個 service／factory 重用。Program 註冊 service 與 factory 為 singleton；三個分段入口都由 orchestrator 選擇／固定 codec 後交給 factory，不在每段建新的 detector。
- Produces: `RecordingSession.EncoderSelection { get; set; }` 型別 `EncoderSelection?`；舊 session.json 缺欄位時 null，不憑使用者 Auto 推定實際編碼器。

- [ ] **1. 寫引擎與 orchestrator 的失敗測試。** 引擎用真實 fixture 驗證首畫格／程序退出，orchestrator 用 factory fake 驗證狀態、呼叫次數及檔案保留。
  - `Startup_UsesSharedSelectionAndReportsActualEncoder`：刪除舊探測後仍可啟動；握手前 `Assert.Null(session.EncoderSelection)`，握手後與 `engine.ActiveEncoder` 相同；由 orchestrator 執行 hardware 失敗只回 CPU 一次並呼叫 `ReportStartupFailure`。
  - `Cancellation_DoesNotStartCpuFallback`：取消時 `Assert.Equal(1, launchCount)`，音訊與程序清理完成；不發出永久硬體失敗冷卻。
  - `Resume_ReusesPinnedEncoderWithoutProbes`：Auto 首段實際 QSV，續錄及音源失效重啟 factory 均收到 QSV；detector 次數不增加，`session.Configuration.EncoderType` 仍為 Auto。
  - `ResumeFailure_PreservesPausedSessionAndSegments`：首段 bytes/hash 不變；`Assert.Equal(Paused, state)`；有效片段清單不變；沒有 CPU launch；可以重試續錄或停止／救援既有片段。
  - `AudioLossRestartFailure_SafelyFinalizesExistingSegments`：保留既有 emergency-stop 語意，失敗候選不加入 concat 清單；舊片段、session 元資料不遺失，不能顯示仍正常錄影。
  - `FailedAttemptPaths_AreNeverReusedOrOverwritten`：每次 launch/retry 用新編號；預先存在的檔案 byte-for-byte 不變。失敗嘗試保留供診斷／救援但不列入有效合併清單，後續重試使用新路徑。
  - `StartupBeyondInitialIpcDeadline_RemainsLockedAndDoesNotResend`：使用可控 IPC handler 延遲，首輪逾時後只查狀態、不第二次 Start；確認前保持設定鎖定，最終成功不回報失敗；延續既有 coordinator 測試模式、不等待整個生產期限。
- [ ] **2. 跑 RED。** `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~RecordingEncoderLifecycleTests|FullyQualifiedName~FFmpegStartupHandshakeTests|FullyQualifiedName~RecordingStartCoordinatorTests'`；確認是新語意失敗。
- [ ] **3. 替換引擎私有探測。** 移除 `QuickProbeEncoder`／`DetectBestHardwareEncoder`；未 pinned 的直接呼叫走 shared service 選擇，pinned 直接使用。引擎每次只執行一次 launch，不再內部 CPU 重試；正式啟動失敗向 orchestrator 回報，該層負責安全重試及 `ReportStartupFailure`，不可重複回退。探測先完成才啟動音訊 producer，避免新增探測等待期間音訊管線積壓；不改原音訊 API、PCM 格式及匿名管線協定。
- [ ] **4. 將啟動重試與分段接入改為成功後提交。** 一次工作階段每次嘗試使用新的 `segment_NNN.mkv`，預先存在路徑拒絕使用；首次硬體失敗改用新的 CPU 候選路徑，不覆寫失敗檔。候選路徑在開始前寫入 `WorkingFilePath` 並保存以利 crash 救援，只有握手成功才加入 `SegmentFilePaths`、固定 `EncoderSelection`；錯誤則還原有效 WorkingFilePath／狀態並保留失敗檔。首次選擇／CPU 回退由 orchestrator 傳入 factory 的 pinnedEncoder，成功後以引擎 ActiveEncoder 與該次回退原因寫入 session；單段引擎本身亦拒絕覆寫已存在輸出。用故障注入測試覆蓋候選 metadata 已保存但握手／成功保存尚未完成時的 crash：既有有效片段仍可救援，不能因新增候選或新欄位讀取失敗。
- [ ] **5. 統一處理三個入口。** initial start、user resume、audio-device-loss restart 均透過 factory；初次 selection 由 Recorder service 決定、成功後存 session。可回復的啟動錯誤不由事件 handler 提前 ForceTransition(Failed)；續錄 catch 停止／dispose 失敗引擎、恢復 Paused。音源失效重啟失敗沿用安全停止；正式錄影中的致命錯誤仍不能吞掉。取消路徑同樣清理，使用獨立清理 token 保存必要 session 元資料。
- [ ] **6. 跑 GREEN 與回歸。** 重跑 RED 指令及上述音訊／救援測試；新增兩段實際 FFmpeg 合成錄影→stream-copy 合併→ffprobe 測試，斷言片段同 codec／尺寸／音訊結構、最終有兩段預期時長且可解碼。macOS 原生匿名音訊測試不得因 factory／重試失去繼承描述元。
- [ ] **7. 範圍提交。** 明列本任務檔案，`git commit -m "fix: preserve encoder continuity across recording segments"`。

## Task 4: 顯示真實編碼器、更新說明與完整驗證

**Files:**

- Modify: `src/ScreenRecorder.Core/Models/RecorderTelemetry.cs`、`src/ScreenRecorder.Core/Localization/LocalizationService.cs`、`src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs`。
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`、`src/ScreenRecorder.UI/Views/MainWindow.axaml`。
- Create: `src/ScreenRecorder.UI/Services/EncoderStatusFormatter.cs`（純格式化，便於不啟動 UI 的測試）。
- Test: 新增 `tests/ScreenRecorder.Media.Tests/EncoderStatusTests.cs`，修改 `tests/ScreenRecorder.Core.Tests/SessionStoreTests.cs`、`LocalizationTests.cs`。
- Modify: `README.md`、`docs/USER_GUIDE.zh-TW.md`、`docs/USER_GUIDE.en-US.md`。
- Create: `docs/verification/2026-09-29-encoder-diagnostics.md`（執行時填入實際證據；無環境為 BLOCKED，不填預期 PASS）。

**Interfaces:**

- Consumes: `RecordingSession.EncoderSelection`，經既有已認證 IPC 遙測回傳。
- Produces: `RecorderTelemetry.EncoderSelection { get; set; }` 型別 `EncoderSelection?`；不加入原始 stderr 或硬體識別資訊。
- Produces: `EncoderStatusFormatter.Format(EncoderSelection? selection, ILocalizationService strings) : string`；MainViewModel `ActualEncoderText` 為唯讀顯示屬性，收到新 session／idle 狀態先清空舊實際值；暫停保留本場已固定結果。
- 顯示文字：`實際編碼器：尚未決定` / `Actual encoder: pending`；成功後用 `libx264 (CPU)`、`NVENC`、`QSV`、`AMF`、`VideoToolbox`；CPU 回退另顯示 `未驗證到可用硬體` / `No validated hardware encoder`、`指定硬體暫不可用` / `Requested hardware unavailable` 或 `硬體啟動失敗，使用 CPU` / `Hardware startup failed; using CPU`。不是直接顯示 Auto。

- [ ] **1. 寫顯示／序列化失敗測試。** `NullSelection_IsPendingInBothLanguages`：兩語輸出精確符合上述 pending 字串；`RecorderSelection_OverridesUiProbeWithoutChangingPreference`：UI 偵測 QSV 但 Recorder CPU 時 `Assert.Contains("libx264", vm.ActualEncoderText)`，SelectedEncoder 仍 Auto；`NewSession_ClearsOldEncoder`：新場準備時不顯示上一場 codec；`UnknownEncoder_IsNotReportedAsHardware`：非定義 enum 顯示 pending；`LegacySessionAndTelemetry_RemainReadable`：缺欄位反序列化成功且 selection null；所有新增中英文鍵完整對應。
- [ ] **2. 跑 RED。** `dotnet test ScreenRecorder.sln -c Release --filter 'FullyQualifiedName~EncoderStatusTests|FullyQualifiedName~SessionStoreTests|FullyQualifiedName~LocalizationTests'`，新測試因缺少欄位／格式化／文案失敗。
- [ ] **3. 接入遙測及顯示。** 在右側狀態區的音量／檔案卡片附近加精簡文字，長回退原因可換行；不改編碼選單或音量輪詢機制。語言切換立即重算；不持久化 UI 偵測結果為使用者設定。
- [ ] **4. 更新兩語使用說明與 README。** 說明 Auto 不保證硬體、何處查看實際結果／日誌、探測失敗分類及續錄失敗仍可停止救援；明列 Windows 真實 CPU／游標驗收尚待完成。不得寫「內顯不支援」、「已修復所有閃爍」或既未量測的性能數字。
- [ ] **5. 跑全套驗證並保存證據。** 依序執行下列命令，全部 exit 0 才可標示本機自動化通過；若環境缺 SDK／依賴，記錄 BLOCKED，不能改掉測試來變綠。

  ```bash
  dotnet restore ScreenRecorder.sln
  dotnet build ScreenRecorder.sln -c Release --no-restore
  dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false
  npm ci --prefix website
  npm test --prefix website
  npm run build --prefix website
  bash tests/native/OpenCamSystemAudioTests.sh
  git diff --check
  ```

  原生音訊 shell 測試限 macOS；其他平台列 BLOCKED／由既有 macOS CI 補驗，不在 Windows 假裝執行成功。現有 `.github/workflows/build-and-release.yml` 的 verify-portable／verify-windows／verify-macos 都要涵蓋新 fixture；本次不推送觸發 CI，待使用者授權後執行及回報。
- [ ] **6. 本機 UI 與實機驗收。** macOS 中英文各檢視待命、開始、錄影、暫停、停止；實際 FFmpeg 執行 1080p／30 FPS 合成檔並 ffprobe，不把 synthetic 當桌面驗收。可用 Mac 實測 VideoToolbox、四種音訊模式、一次暫停改音源／游標及救援；無收音或授權時標明缺口。Windows 依 Spec 的 CPU 比較與硬體矩陣保留 BLOCKED；待有機器再比較，GDI 游標仍列未修復／待第三階段。
- [ ] **7. 記錄結果並範圍提交。** 驗證文件列提交、OS、命令、通過／失敗數、產物位置及未驗項；確認版本仍 0.2.3、無產品封裝／原始使用者日誌。`git commit -m "feat: show actual encoder and document diagnostic limits"`。

## 完成標準與交接

- 四個任務各自通過新失敗案例及現有回歸，完整結果可核對；程式可在目前 macOS 啟動且新增欄位不擠壓音量／按鈕。沒有硬體的測項仍是 BLOCKED。
- 只證明探測、快取、診斷及安全續錄的改善；尚未完成第二階段 IPC 分流與第三階段游標擷取修正。不聲稱使用者的 CPU 問題已根治。
- 實作前先審閱本計畫並選擇執行方式。建議同一代理依序實作，因 shared selection、引擎與 session 生命週期緊密相依；完成後做獨立程式審查。未獲計畫確認不改產品程式。

## 計畫自檢

- Spec 第 4 節：程序／分類／清理 → Task 1；快取／平台優先序／診斷 → Task 2；正式回退／續錄固定與檔案保護 → Task 3；實際值及中英文 → Task 4。
- 五個 Review Focus 均有具名測試；保留 macOS／音訊／救援與 IPC 安全回歸。缺少 Windows 實機不會被自動化成功抵銷。
- 未修改產品程式；以上測試均為待實作要求，不是已通過的測試結果。
