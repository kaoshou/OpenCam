# Audio-only Preflight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將錄製前確認與五秒試錄改為純音訊，並在獨立狀態區持續顯示準備、試錄與正式啟動狀態。

**Architecture:** 新增獨立音訊擷取契約與平台參數，不改正式影片引擎的輸出契約。Recorder 使用音訊專用 session、受控暫存檔與既有音訊播放輸出；UI 將音訊確認及正式啟動呈現為連續的狀態流程，不以視窗關閉推論後端成功。

**Tech Stack:** C#/.NET 8、Avalonia、現有 FFmpeg/ffprobe、Windows WASAPI/DirectShow、macOS 既有音訊 helper、xUnit。不新增應用套件。

**Spec:** `docs/superpowers/specs/2026-10-20-audio-only-preflight-design.md`（使用者已確認）。

## Global Constraints

- 正式錄影仍錄製原設定的畫面與音訊，不改編碼器、錄影範圍或專案格式。
- 音訊檢查／試錄不擷取螢幕、不啟動影片編解碼、不輪詢 RGBA；平台 helper 為取得系統音訊所需的顯示器選取不等同輸出影像。
- 保留 operation/server/revision/lease 與停止確認；不得放寬 timeout、音量新鮮度或來源安全條件。
- 五秒計時只從音訊引擎確實啟動後開始，且由後端時間狀態驅動。
- 沒有選取任何音源或設定略過檢查時仍顯示正式啟動準備狀態。
- 中英文依規格表同步；未啟用來源明示不會錄製；靜音不是裝置失敗。
- 不推送、不升版、不發布；不修改主 checkout 原有 dirty 文件。使用既有開發 worktree，直接實作，不另派代理。

## Review Focus

1. 兩音源速率／聲道不同或其中一個靜音，另一來源仍應可試錄及回放（Task 1、3）。
2. 初始化部分成功後另一裝置失敗，已開啟裝置必須釋放且不能假報就緒（Task 2）。
3. 試錄收尾、取消與重新檢查交錯，不回放上一輪音訊或提早交還擷取權（Task 3）。
4. 正式啟動已送出但視窗關閉／IPC 回覆延遲，不留下使用者不知道的背景錄影（Task 5）。
5. 小視窗、長英文錯誤與裝置名稱，不遮住取消及確認操作（Task 4）。

## 檔案責任與共同驗證方式

新契約放 Core，音訊 FFmpeg 工作放 Media，平台輸入參數放各 Platform，生命週期及暫存資料放 Recorder，畫面與啟動呈現放 UI。既有 `PreflightTrialStorage` 保留有界寫入、hash、持有權及清理；不將試錄偽裝為 `RecordingProject`。

以下測試命令在開發 worktree 執行，使用可用 .NET 8 與 FFmpeg。定向測試先 RED 再 GREEN；編譯缺失可作新介面的初次 RED，但行為測試必須在實作後驗證斷言。不得為讓測試通過而更改正式錄影參數。

## Task 1: 音訊專用輸入與輸出參數

**Files:** 新增 `src/ScreenRecorder.Core/Interfaces/IPreflightAudioPlatformProvider.cs`、`src/ScreenRecorder.Media/Preflight/PreflightAudioFormat.cs`；修改兩平台 `WindowsFFmpegProvider.cs`、`MacOsFFmpegProvider.cs`；新增 `tests/ScreenRecorder.Media.Tests/PreflightAudioArgumentsTests.cs`。

**Interfaces:** 新介面 `string BuildPreflightAudioInputArguments(RecordingConfiguration configuration, bool synthetic, string? systemAudioInput, string? microphoneAudioInput)`。既有 provider 額外實作它，不變更 `BuildInputArguments` 行為。新 `PreflightAudioFormat.OutputArguments(bool recordTrial)` 輸出檢查用 null sink 或試錄 Matroska/PCM s16le 音訊至 stdout；指定 48 kHz stereo，無影片輸出。

- [ ] 新增 `SelectedSourcesOnly`、`MixedFormatsKeepBothSources`、`NoVideoInputOrMapping` 測試：四種選項、合成來源、實際平台字串，None 拒絕開啟音訊擷取；輸入索引從音訊起算，不能沿用影片的 index 1/2。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter FullyQualifiedName~PreflightAudioArgumentsTests`，確認 RED。
- [ ] 實作獨立音訊輸入組裝，沿用正式路徑的裝置身分解析與混音語義；不使用影像輸入占位、不新增虛構音源。Windows 保留正式 DirectShow mic 選取，macOS 使用現有 PCM helper。
- [ ] 重跑上述測試及 `--filter FullyQualifiedName~PreflightEncodingTests`，確認新音訊參數與正式影片參數皆 GREEN。
- [ ] 僅提交本任務檔案，訊息 `feat: add audio-only preflight media arguments`。

## Task 2: 獨立音訊擷取與就緒狀態

**Files:** 新增 `src/ScreenRecorder.Core/Interfaces/IPreflightAudioCapture.cs`、`src/ScreenRecorder.Media/Preflight/FFmpegPreflightAudioCapture.cs`、`src/ScreenRecorder.Recorder/Services/PreflightAudioCaptureFactory.cs`、`src/ScreenRecorder.Recorder/Services/PreflightAudioSession.cs`；修改 `RecordingPreflight.cs`；新增 `tests/ScreenRecorder.Media.Tests/PreflightAudioCaptureTests.cs`。

**Interfaces:** `IPreflightAudioCapture : IAsyncDisposable` 提供 `Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)`、`Task StopAsync(CancellationToken ct)`、`PreflightSourceResult ReadMicrophone(DateTimeOffset now)`、`PreflightSourceResult ReadSystemAudio(DateTimeOffset now)`、`bool IsRunning`、`string? ErrorCode`。Start 完成代表程序及所選音源實際啟動，不是 Process.Start 返回。null output 表示不存檔；非 null 使用 Task 1 試錄輸出。Factory `IPreflightAudioCapture Create()` 每次提供獨立資源。`PreflightAudioSession` 實作既有 `IPreflightLiveSession`，`ReadFrame()` 永遠 null。

- [ ] 新增 `StartWaitsForAcknowledgement`、`UnselectedSourceNeverStarts`、`PartialInitializationReleasesSources`、`StoppedCaptureCannotReadLevels`；使用可控啟動訊號，不用 sleep 當就緒證據。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter FullyQualifiedName~PreflightAudioCaptureTests`，確認 RED。
- [ ] 實作平台來源與 FFmpeg drain/pipe 管理，沿用既有程序收尾、錯誤回報與有界停止模式；短鎖保護停止後讀取。每個已初始化資源都有失敗清理，不借用正式 singleton。沿用 `PreflightReadiness.Resolve` 樣本判定。
- [ ] 為 `PreflightStatus` 增加非位置建構參數的 `AudioReady` 屬性，代表已確認啟動且無錯誤，不要求有聲；不得將 `HasFrame=true` 冒充音訊就緒。保持既有 enum 數值。
- [ ] 重跑本任務測試及音源新鮮度測試，確認 GREEN；提交 `feat: implement isolated preflight audio capture`。

## Task 3: 五秒聲音試錄、驗證及回放

**Files:** 修改 `PreflightAudioSession.cs`、`PreflightTrialStorage.cs`；新增 `src/ScreenRecorder.Media/Preflight/PreflightAudioProbe.cs`、`src/ScreenRecorder.Recorder/Services/PreflightAudioPlayback.cs`、`tests/ScreenRecorder.Media.Tests/PreflightAudioTrialTests.cs`。

**Interfaces:** `PreflightAudioSession` 同時實作現有 `IPreflightTrialSession`；沿用其三個方法簽章。`PreflightAudioProbe.ValidateAsync(Stream source, CancellationToken ct)` 回傳 `Task<TimeSpan>`，由建構子注入 media executable／程序工廠。`PreflightAudioPlayback` 建構子接收受控讀取 stream factory 與 `Func<IProjectAudioOutput>`，提供 `Task PlayAsync(CancellationToken ct)`、`Task StopAsync(CancellationToken ct)`、`bool Playing`、`string? ErrorCode`。

- [ ] 新增 `TimerStartsAfterAudioAcknowledgement`、`CannotReplayBeforeSealAndProbe`、`TrialHasAudioAndNoVideo`、`ReplayUsesAudioOutputOnly`、`CancelThenRecheckCannotReplayOldTrial`、`SilentInputDoesNotLoseOtherSource`。assert 試錄五秒、有效音訊、零影片串流、取消不遺留資源；保留現有合理時長驗證界限，不擴大容忍度。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter FullyQualifiedName~PreflightAudioTrialTests`，確認 RED。
- [ ] 實作 Checking → TrialStarting → TrialRecording → TrialFinalizing → Reviewing；引擎 Start 完成後才開始五秒計時；停止且封存後才驗證。暫存採音訊 Matroska，不建立影片 recipe。Probe 用受控 stream 檢查音軌與時長，拒絕影片與無音軌檔案。
- [ ] FFmpeg 將受控音訊解碼為 48 kHz stereo float PCM，串流至既有 `IProjectAudioOutput`，不一次讀入整檔、不啟動影片 decoder；播放完成與取消均等待輸出裝置停止確認。
- [ ] 重跑本任務及試錄 storage/取消回歸，確認 GREEN；提交 `feat: add audio-only trial recording and playback`。

## Task 4: IPC 接線與精簡音訊確認畫面

**Files:** 修改 Recorder `Program.cs`、`PreflightCoordinator.cs`、`PreflightIpcDispatcher.cs`，UI `Preflight/RecordingPreflightWindow.axaml`、`.axaml.cs`、`RecordingPreflightViewModel.cs`、`Localization/LanguageManager.cs`；更新 `PreflightIpcTests.cs`、`RecordingPreflightUiTests.cs`、`PreflightTrialPhaseTests.cs`。

**Interfaces:** Coordinator session factory 改接 `PreflightAudioSession`；保留既有 command 名稱、ID/revision/lease 驗證。舊 frame command 不再被 UI 呼叫，不得轉向正式專案 frame。UI readiness 使用 Task 2 的 `AudioReady`，trial buttons 使用原 session phase。

- [ ] 新增 `AudioReadyDoesNotRequireFrame`、`NoFrameRequestsInAudioDialog`、`StatusAreaShowsEveryPhaseInBothLanguages`、`LongTextKeepsActionsReachable`；保留 stale reply、錯誤 server/revision、重複動作與 lease 測試。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter 'FullyQualifiedName~RecordingPreflightUiTests|FullyQualifiedName~PreflightIpcTests|FullyQualifiedName~PreflightTrialPhaseTests'`，確認新行為 RED。
- [ ] 移除 UI bitmap／frame polling，保留狀態輪詢；實作規格中的兩音源卡片、固定位置狀態區、五秒進度與按鈕。完整加入規格中英文文案，重用既有語系資源組織；不漏設定頁描述及無聲提醒文字。
- [ ] 接線 audio session。將依賴影片的舊 preflight 測試更新為等價音訊生命週期斷言，不刪安全案例；仍適用正式影片的既有 media 測試保留。僅移除確認無引用的舊 preflight 專用程式，不清理無關媒體功能。
- [ ] 重跑上述測試與 `--filter FullyQualifiedName~PreflightLeaseTests`，確認 GREEN；提交 `feat: replace preflight preview with audio confirmation UI`。

## Task 5: 正式啟動準備視窗與略過流程

**Files:** 修改 `MainViewModel.cs`、`MainViewModel.RecordingContent.cs`、`Views/MainWindow.axaml.cs`、`Services/RecordingStartCoordinator.cs`、確認視窗；新增 `src/ScreenRecorder.UI/Preflight/RecordingPreparationState.cs`；更新 `RecordingStartConfirmationTests.cs`、`PreflightPreferenceTests.cs`。

**Interfaces:** 新 enum `RecordingPreparationState { Preparing, Confirming, Started, Failed, Unconfirmed }`。在既有 `RecordingStartCoordinator.StartAsync` 最後增加 optional `Action<RecordingPreparationState>? reportState = null`。UI 共用同一準備狀態 presenter，音訊確認返回設定後不先關閉視窗，而是轉入 preparing；無音源或關閉檢查時建立只有狀態區的精簡視窗。

- [ ] 新增 `NoAudioSkipsCheckButShowsPreparation`、`PreferenceOffStillShowsPreparation`、`DelayedStartKeepsWindowOpen`、`UnconfirmedStartCannotCloseOrRestart`、`ResumeAndInsertUsePreparation`。assert 成功 ACK 前不關閉、不解鎖設定，未知不視為成功或停止。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests --filter 'FullyQualifiedName~RecordingStartConfirmationTests|FullyQualifiedName~PreflightPreferenceTests'`，確認新行為 RED。
- [ ] 將既有開始／續錄／插入路徑接入 presenter，保留各自原權限、輸出與 source-owner 驗證；音訊確認停止失敗時不得呼叫正式 Start。不得僅以取消本機 token 假裝取消已送出的 Start。
- [ ] 協定命令送出前可取消；送出後關閉請求不得銷毀追蹤狀態，維持「正在確認錄製狀態」及原安全處置，直到明確結果。正式開始後關閉準備視窗、恢復既有錄製控制。
- [ ] 重跑本任務與 `--filter FullyQualifiedName~RecordingStartCoordinatorTests`，確認 GREEN；提交 `feat: preserve recording preparation status through startup`。

## Task 6: 文件、整合回歸與交付

**Files:** 修改 `README.md`、既有雙語使用指南及相關網站說明（先以 `rg -n 'Preflight|錄製前|試錄' README.md docs website` 定位現行來源，不改歷史 Release notes）；新增 `docs/adr/0009-audio-only-preflight.md`、`docs/verification/2026-10-21-audio-only-preflight.md`。

**Interfaces:** 不新增。驗證依 Tasks 1–5 已定義的來源與狀態契約。

- [ ] 補齊設定、純音訊試錄不驗證影片編碼器、無音源略過但保留準備狀態之文件與 ADR；記錄替代方案「隱藏影片」被拒原因。
- [ ] 執行完整 `dotnet test ScreenRecorder.sln -c Release`，要求零失敗並明確列出略過；執行既有 player/CI scripts/website 測試及網站 build，使用 repository 實際 package scripts，不猜測命令名稱。
- [ ] 執行 `git diff --check`、版本一致性檢查；確認版本未變、無新套件、未提交主 checkout 既有 dirty 文件。
- [ ] 實機可用時驗證 Windows/macOS 四音源、不同速率、裝置遺失、取消、五秒回放、正式開始及慢啟動。不可用則逐項標未驗證，不把合成測試視為實機 PASS。
- [ ] 自行對照規格檢查所有狀態與安全邊界，整理結果及剩餘限制；提交文件與驗證紀錄，交付使用者，不自行推送或發布。

## Self-review

規格各節已分配：音訊限定與來源 Tasks 1–2；五秒、驗證、回放 Task 3；版面、多語、協定 Task 4；準備與略過 Task 5；正式行為回歸及實機限制 Task 6。五項 Review Focus 均有指定測試。新介面簽章由前置任務定義，後續不借用影片 recipe；無依賴升級、無發布步驟。計畫待使用者確認後才開始產品實作。
