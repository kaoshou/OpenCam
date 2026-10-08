# Record First, Edit After Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Preserve the already selected inline/native execution method. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用者直接錄影，暫停／停止後可進入內容編輯；停止自動產生 MP4，素材與編輯記錄保留，不先要求建立專案。

**Architecture:** Recorder 仍是錄影與專案的唯一寫入者，自動建立錄製內容後沿用安全來源儲存。輸出採固定 revision 快照及可查詢的背景工作；主視窗與編輯器共用一個內容控制器，不各自控制 Recorder。錄影擷取後端與平台音訊策略不變。

**Tech Stack:** .NET 8、Avalonia 11.2.5、既有認證 IPC、IProjectStore／ProjectRecordingCoordinator、FFmpeg／ffprobe、xUnit。

**Spec:** `docs/superpowers/specs/2026-10-09-record-first-edit-after-design.md`；剪輯器細節繼續遵循 `docs/superpowers/specs/2026-10-08-recording-project-editor-design.md`。

## Global Constraints

- 僅本機；不推送、不合併、不改版本、不發布。沿用 `codex/recording-project-persistence` worktree，不恢復被放棄的 editing-preview。
- 移除首頁上方錄影專案整排；控制列順序「開始錄影」「暫停／繼續」「停止錄影」「內容編輯」「修復救援」。
- 無新建精靈；在目前錄影儲存位置下的 `OpenCam Recordings` 自動建立時間名稱與唯一識別的錄製內容。
- 停止仍自動 MP4；暫停期间的剪輯必須反映在成品。停止後的編輯僅保存，手動輸出另產生新檔，不覆蓋既有 MP4。
- 自動保存與來源驗證完成才能編輯；預覽停止確認與保存完成才能續錄。一般續錄追加尾端，在此補錄為另一項明確交易。
- 新錄製內容來源不可因舊 `DeleteWorkingFileAfterSuccessfulRemux=true` 被刪除；不偷偷改写使用者既有設定值。
- 原始錄製參數、熱鍵、平台擷取／音訊策略、錄製中關閉保護及舊救援保留。
- 中英文、最小主視窗 820×580，以及既定編輯器尺寸／深淺色必測。Windows 實機在本機 Mac 環境下仍標示未驗證。

## Review Focus

1. 開始回覆丟失：重送不得建立第二份內容／啟動第二段，Task 1/3 測同 operationId 重播。
2. 停止輸出很久或取消：IPC 查詢不能被長時間鎖住，Task 2/3 用可控未完成 exporter 測輪詢、取消與拒絕續錄。
3. 暫停剪除／重排後停止：不能走舊未剪輯 concat，Task 2/6 比對快照順序與實際成品畫格。
4. 來源已保存但 MP4 失敗：不能把「錄影失敗」與「成品失敗」混為一談，Task 2/4 測仍可編輯、來源不變、明確重試。
5. 使用者開著編輯視窗返回首頁：不能因視窗開啟而永久鎖住停止，也不能另開獨立 Recorder，Task 4/6 測共享狀態與唯一工作階段。

## 範圍、依賴與完成界線

本計畫負責已確認的「入口與錄製內容生命週期修訂」，不是用搬按鈕代替全部剪輯功能。真正預覽／時間軸／補錄／屬性仍是原規格的必要部分，沒有縮減。

原生媒體工作正在 `2026-10-08-native-editor-integration.md` Task 3/4 中進行；播放器尚未選定、產品精剪 exporter 尚未完成。下列 Task 2 可先以可控 exporter 驗證工作生命週期，但 **Task 6 與使用者可驗收的主入口切換，必須等安全的真實播放器及精剪輸出接線完成**。不能以假成功 exporter、純清單、空預覽或舊未剪輯 Remux 當交付。

具體媒體後端接線仍需依先前選型門檻取得證據後制定其計畫；本文件只固定它與流程層之間的契約，不臆造已存在的能力。若依賴未滿足，保留功能未完成狀態，先做其餘安全本機工作。

## 檔案責任

- Recorder `RecordingContentFactory.cs`：自動命名、目錄、建立及開始失敗保留政策。
- Recorder `ProjectRecordingCoordinator.cs`：現有單寫者內串接新建／續錄／完成素材／輸出狀態，不增加第二把互相反鎖的生命週期鎖。
- Recorder `RecordingContentExportJob.cs`、`IRecordingContentExporter.cs`：固定快照、背景工作狀態與輸出能力邊界。
- Infrastructure `IPC/ProjectMessages.cs`、Recorder `ProjectIpcDispatcher.cs`：可重播命令與快速狀態查詢。
- UI `Projects/RecordingContentController.cs`：主視窗／編輯器共用命令、快照與輪詢；既有 ProjectClient 的 server lifetime 綁定不移除。
- UI MainWindow／ProjectWorkspace／Settings：入口、狀態及來源保留說明，不管理素材路徑或直接執行 FFmpeg。

## Task 1: 自動建立錄製內容並啟動錄影

**Files:** Create `src/ScreenRecorder.Recorder/Services/RecordingContentFactory.cs`; modify `ProjectRecordingCoordinator.cs`; test `tests/ScreenRecorder.Media.Tests/RecordingContentLifecycleTests.cs`。

**Interfaces:** `RecordingContentFactory(IProjectStore store, TimeProvider clock)`；`CreateAsync(string outputDirectory, CancellationToken ct) -> Task<IProjectHandle>`。Coordinator 新增 `StartNewContentAsync(RecordingConfiguration config, Guid operationId, CancellationToken ct = default) -> Task<ProjectCommandResult>`；既有 `StartAsync` 繼續代表目前內容續錄。

- [ ] RED：`StartNewContentNeedsNoNameOrManifestPath` 直接傳錄影設定，assert 有 ProjectId、名稱、位於 output/OpenCam Recordings、持久化成功後才呼叫擷取。`SameTimestampCreatesDistinctContent` 用固定 clock 建兩份，assert 路徑／ID 不同。
- [ ] RED：`ReplayStartDoesNotCreateOrCaptureTwice` 同 operationId 重送兩次，assert create/start 各一次且相同 ID；同 ID 不同設定拒絕。`CreateFailureDoesNotStartCapture` assert 擷取未開始、沒有默默 fallback。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~RecordingContentLifecycleTests`；先看到新增行為失敗，再實作。
- [ ] 由 coordinator 的現有 gate／operation fingerprint 管理交易；開始新內容前保存並關閉目前 Ready 內容，Recording/Paused/SaveFailed/輸出中則拒絕。建立受控容器目錄後仍使用 store 的 bound-path、鎖定與原子保存。失敗保留已寫來源，不發布未確認片段。
- [ ] 記住首次儲存位置作為本次成品目錄，不把內部 sessions 目錄誤當 MP4 目的地；有效素材加入後才登錄最近內容。
- [ ] 聚焦測試 PASS，跑完整 solution；scoped commit `feat: create recording content automatically on start`。

## Task 2: 完成素材後啟動固定快照的 MP4 工作

**Files:** Create `src/ScreenRecorder.Recorder/Services/IRecordingContentExporter.cs`, `RecordingContentExportJob.cs`; modify `ProjectRecordingCoordinator.cs`; test `tests/ScreenRecorder.Media.Tests/RecordingContentExportLifecycleTests.cs`。

**Interfaces:**

- `RecordingExportState { Idle, Running, Succeeded, Failed, Canceled }`。
- `RecordingExportStatus(Guid ExportId, long Revision, RecordingExportState State, double Progress, string? FinalPath, string? Error)`。
- `RecordingExportResult(bool Success, string? FinalPath, string? Error)`。
- `IRecordingContentExporter.ExportAsync(IProjectHandle owner, RecordingProject snapshot, string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct) -> Task<RecordingExportResult>`。
- Coordinator `FinishAndExportAsync(Guid operationId, CancellationToken ct = default) -> Task<ProjectCommandResult>`、`RetryExportAsync(Guid operationId, CancellationToken ct = default)`、`CancelExportAsync(Guid exportId, CancellationToken ct = default)` 同回傳型別；`ExportStatus` 可讀。

- [ ] RED：`StopCommitsSourcesBeforeStartingExport` assert exporter 收到已保存 revision，素材完整；`EditedPauseUsesEditedSnapshot` A/B/C 刪 B 重排 C/A 後，assert exporter 得到 C/A，不是 session 原始順序。
- [ ] RED：`ExportFailurePreservesEditableSources` exporter 失敗後 Mode=Ready、ExportStatus=Failed、sources hash 與數量不變；`RetryDoesNotAppendSegmentsAgain` retry 不重複素材。
- [ ] RED：`LongExportDoesNotBlockStatusOrCancel` 用 TaskCompletionSource 暫停 exporter；assert status 可回應、續錄及新編輯被拒、取消會等工作結束且不刪既有輸出。`LostFinishReplyDoesNotStartSecondExport` 同 operationId 只排一次。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~RecordingContentExportLifecycleTests`，RED 後實作工作狀態。
- [ ] 長時間媒體執行不持有 coordinator／dispatcher gate；gate 內只提交狀態／快照及登記工作。工作持有來源 owner 的有效生命週期；關閉／處置等待或取消自己的工作。取消與完成競態只可有一個終態，不覆寫成功結果。
- [ ] 正常停止後由保存成功的完整時間軸啟動一項自動輸出。輸出中主視窗顯示進度並鎖定新擷取／剪輯；失敗或取消後保留素材且可編輯。空時間軸不生成空 MP4，明示沒有可輸出內容。
- [ ] 真實 exporter adapter 只能接受同一快照，需已通過安全來源存取、任意裁剪重編碼、相容資料才 stream copy、檔名不覆蓋及成品驗證。此 adapter 尚未完成時，不宣稱自動 MP4 已可用，不切換交付入口。
- [ ] 聚焦 PASS、完整 solution；scoped commit `feat: coordinate automatic export after recording completion`。

## Task 3: 新流程 IPC 與重試契約

**Files:** Modify `src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs`, `src/ScreenRecorder.Recorder/Services/ProjectIpcDispatcher.cs`; test `tests/ScreenRecorder.Media.Tests/ProjectIpcTests.cs`。

**Interfaces:** 新增命令 `StartNewRecordingContent`, `FinishRecordingContent`, `RetryRecordingContentExport`, `CancelRecordingContentExport`；`ProjectRequest.ExportId: Guid?`；`ProjectSnapshot.Export: RecordingExportStatus?`（共用 IPC 可序列化狀態型別放 Core/Projects，而非引用 Recorder assembly）。`GetProjectStatus` 返回目前保存／輸出狀態，不等待輸出完成。

- [ ] RED：`ProjectIpc_NewContentReplayHasStableIdentity`；`ProjectIpc_ExportStatusRemainsQueryable`；`ProjectIpc_CancelRejectsOtherExportId`；`ProjectIpc_RestartNeverReplaysOldStart`。檢查 project ID、server instance、operation fingerprint、錯誤 payload、舊 revision 及 64KiB 限制。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectIpc_`，確認 FAIL 後將命令映射到 Task 1/2 API。
- [ ] 新建命令允許目前無 project ID，但仍驗證 server lifetime 及 operation ID；後續命令必須匹配 owner。輸出由 start/finish 命令排程，不能在 dispatcher 長鎖內 await 整段重編碼。
- [ ] 聚焦 PASS、完整 solution；scoped commit `feat: expose record-first lifecycle over authenticated IPC`。

## Task 4: 共用內容控制器與錄影工具列入口

**Files:** Create `src/ScreenRecorder.UI/Projects/RecordingContentController.cs`; modify `Projects/ProjectWorkspaceViewModel.cs`, `ProjectWorkspaceView.axaml`, `.axaml.cs`, `ViewModels/MainViewModel.cs`, `Views/MainWindow.axaml`, `.axaml.cs`, `Core/Localization/LocalizationService.cs`; tests `RecordingContentControllerTests.cs`, `ProjectMainEntryTests.cs`, `ProjectWorkspaceLayoutTests.cs` under Media.Tests。

**Interfaces:** `RecordingContentController(IProjectClient client)` 擁有唯一 `ProjectWorkspaceViewModel Workspace`；`StartNewAsync(config)`, `ResumeAsync(config)`, `PauseAsync()`, `StopAsync()`, `RefreshAsync()` 皆 `Task`；`CanOpenEditor: bool`。Workspace 新增對 Task 3 命令的呼叫並沿用現有冪等重試機制。MainViewModel 建立一個 controller，同一 Workspace 傳給編輯視窗，不再每次開視窗新建 client／模型。

- [ ] RED：`MainStartCreatesContentWithoutDialog` 透過真正 command/handler 驗證，不以檢查 XAML 字串代替；`EditorButtonFollowsStop` 檢查實際原生控制項順序與最小尺寸可見範圍。
- [ ] RED：`PausedEditorUsesCurrentContent` 開啟已有已保存片段；`TwoWindowsShareStopAndResumeState` 相同 owner、僅一項開始命令；`RecordingAndSavingDisableEditor` 含啟動中、輸出中及未確認狀態。
- [ ] RED：`ResumeWaitsForPreviewStopAndSave` 停止確認延遲／失敗時不擷取；`IdleEditorShowsRecentContentWithoutCreateWizard` 無目前內容時顯示最近／開啟入口，而非新建要求。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~RecordingContentControllerTests|FullyQualifiedName~ProjectMainEntryTests|FullyQualifiedName~ProjectWorkspaceLayoutTests'`，RED 後實作。
- [ ] 移除頂部 DockPanel 子列；新增「內容編輯／Edit content」按鈕在 Stop 後。錄影指令／快捷鍵路由至共用 controller；移除以 IsProjectWorkspaceOpen 一刀切阻止停止／暫停的判斷，改看真實內容狀態。
- [ ] 保留錄影參數、波形、裝置／磁碟安全停止監控；所有已確認狀態採同一 telemetry / IPC 來源，禁止 UI 自己猜測完成。自動輸出失敗显示素材已保存及重試，而不是成功 MP4。
- [ ] 編輯視窗移除必經新建入口；保留保存、最近內容、開啟、續錄與手動輸出。預覽停止契約由真正播放器提供，不能用空實作通過。
- [ ] 聚焦 PASS、完整 solution；只有真實媒體依賴滿足才將這條路徑作為可測交付；scoped commit `feat: open content editing directly from recording controls`。

## Task 5: 舊清理設定、最近內容及救援相容

**Files:** Modify `UI/Views/SettingsWindow.axaml`, `UI/ViewModels/SettingsViewModel.cs`, `Core/Localization/LocalizationService.cs`, `UI/ViewModels/MainViewModel.cs`; tests `SettingsViewModelTests.cs`, `RecordingContentLifecycleTests.cs`, `ProjectCommitRecoveryTests.cs`。

- [ ] RED：`ManagedSourcesSurviveLegacyDeletePreference` 設定 true，錄影停止與輸出成功後來源仍在且可重開；`SavingPreferencesDoesNotRewriteLegacyDeleteValue` 儲存其他設定後舊值不變；`OnlyCommittedContentEntersRecentList` 空失敗不加入，已提交來源可再找到。
- [ ] Run 相應測試 filters，RED 後實作。新版設定以「保留原始素材供後續編輯，會增加儲存空間」取代可誤刪永久來源的可用選項；不改動舊 session 的救援政策。
- [ ] 測試正常關閉不被誤報救援、缺來源明確阻止讀取、未完成 journal 只提交一次；不讓 UI 直接刪除 sources。
- [ ] 聚焦 PASS、完整 solution；scoped commit `fix: preserve editable sources across legacy cleanup settings`。

## Task 6: 真實成品與原生操作故事驗收

**Files:** Extend `tests/ScreenRecorder.Media.Tests/ProjectRecordingIntegrationTests.cs`; add `tests/ScreenRecorder.Media.Tests/RecordingContentExportIntegrationTests.cs`; create `docs/acceptance/2026-10-09-record-first-edit-after.md`。

- [ ] 先檢查媒體依賴：真實播放器／預覽停止、縮圖波形時間軸、補錄交易、固定快照精剪輸出均完成。未完成不得把 stub 注入正式主程式，也不得宣稱本計畫已達完成條件。
- [ ] 用帶可識別影像／聲音的 A/B/C 真素材，暫停剪 B、重排 C/A/B，停止後比對成品實際畫格／音訊順序與時長，並核對所有來源 hash 不變。另測未剪輯相容快路徑與不同尺寸／聲音屬性的重編碼路徑。
- [ ] 實機從主入口直接錄影 → 暫停 → 編輯 → 續錄 → 停止自動 MP4 → 再編輯只保存 → 關閉重開 → 在此補錄／普通追加 → 停止新成品；全程不要求新建專案，不覆蓋舊成品。
- [ ] 驗證磁碟不足、取消、損壞來源、輸出失败、丟回覆及關閉防護。明確記錄來源保存、成品成功／失败、音訊停止各自證據。
- [ ] Run `dotnet test ScreenRecorder.sln -c Release`，再執行相關真實錄製／媒體探針。記錄 PASS/SKIP/FAIL，不把現有跳過算實機成功。
- [ ] 使用 CUA 檢視原生最新版：中英文、深淺色、小視窗／長名稱與核准三欄版型。保留使用者自行測試的程式入口，不用瀏覽器模型當驗收。
- [ ] 最後一次整體唯讀審查，修復 Critical/Important 並重跑驗證；僅本機 scoped commits。Windows 無實機則標示未驗證；不能宣稱雙平台全部完成。

## Self-review / handoff

- 規格入口與共享狀態對應 Task 4；自動資料與失敗保留 Task 1/5；MP4／固定 revision／重試 Task 2/3/6；舊設定與救援 Task 5；完整故事 Task 6。
- 型別對齊：Export status/result enum 放 Core/Projects 供 IPC 與 Recorder 共用；exporter 執行介面留 Recorder services，UI 不引用媒體實作。
- 已知依賴缺口明列：目前沒有產品播放器或精剪 exporter；不是省略它們，也不能在未完成時切換交付。續接原媒體選型與接線工作，不再次要求使用者選版型。
- 五項 Review Focus 均有對應延遲、重播或實際成品測試；沒有以 mock 冒充影音驗收。
- 沿用使用者已選的本機 inline/native 執行；本計畫待使用者審閱確認後開始。
