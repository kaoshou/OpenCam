# Project Save and Export Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 明確區分錄製保存、編輯儲存與 MP4 輸出，補上清單排序與已確認的編輯器介面。

**Architecture:** 保持 Recorder 擁有正式專案與素材；把編輯工作狀態、正式保存基準與中繼資料復原草稿分開。UI 透過現有認證 IPC 做選擇與呈現；錄製提交仍立即耐久保存，排序共用既有編輯命令。

**Tech Stack:** .NET 8、C#、Avalonia、現有 JSON project store、xUnit、FFmpeg。

**Spec:** `docs/superpowers/specs/2026-10-09-project-save-export-workflow-design.md`

## Global Constraints

- 暫緩剪輯預先處理／預先轉檔快取，不覆寫原始 MKV，也不為此複製影片。
- 本次不更改版本，不合併、不發布、不觸發 GitHub Actions；推送留待整合驗證後按既有授權處理。
- 工作分支 `codex/recording-project-persistence`，VERSION 保持 0.2.7。
- 不提交使用者的 `docs/acceptance/2026-10-08-project-phase-b.md`，不重置其他未提交修改。
- 錄影、啟動、停止及輸出中禁止切換專案與剪輯。
- Native Windows 驗收缺席必須標示 REQUIRES MANUAL VALIDATION。
- 此計畫只新增儲存工作流程；快速輸出與預覽兩份既有計畫的失敗項目必須獨立結案。

## Review Focus

1. 正式保存成功、草稿清理前當機：舊草稿不能復活（Task 2）。
2. 復原回到保存內容但修訂號不同：dirty 不能只比較修訂號（Task 1）。
3. 續錄後放棄修改：素材及新增片段不能因舊歷史快照消失（Task 3）。
4. 同名稱同大小的 MP4 遭替換：不能誤用既有輸出（Task 4）。
5. 清單拖曳期間狀態改為不可編輯：drop 必須重新檢查且取消（Task 5）。

## 執行與驗證方式

建議 Native：同一實作者順序完成每項，最後一次獨立整體審查。每項先新增可重現失敗的測試、執行確認 RED，再實作、確認 GREEN，最後僅提交該項檔案／差異。不可先把全部工作樹一起提交。

測試命令中的 `dotnet` 使用本機已確認的 .NET 8 SDK；需要 local socket 的測試按工具權限流程執行。測試輸出寫入 `.superpowers/sdd/2026-10-09-project-save-export-workflow/`，紀錄實際結果，不能以本計畫預期結果替代。

### Task 1: 編輯工作狀態與正式保存基準

**Files:** 修改 `src/ScreenRecorder.Core/Projects/ProjectEditHistory.cs`、`RecordingProject.cs`；新增 `ProjectEditSaveState.cs`；測試 `tests/ScreenRecorder.Core.Tests/ProjectEditSaveStateTests.cs`。

**Interfaces:** 新增 `ProjectEditSaveState(RecordingProject saved)`；提供 `bool IsDirty(RecordingProject working)`、`void AcceptSaved(RecordingProject saved)`、`RecordingProject DiscardEdits(RecordingProject working)`。比较名稱與片段內容，不以 monotonic revision 作 dirty 判定；Discard 保留 working 的素材/session inventory。新增 `RecordingProject.AutoExportOnStop` 預設 true、`Guid? ResolvedDraftId`。

- [ ] 新增 RED 測試：`EditDoesNotAlterSavedBaseline`、`UndoToSavedContentIsClean`、`DiscardPreservesSourcesAndSessions`、`LegacyProjectDefaultsToAutoExport`；斷言正式基準不變、undo 後 IsDirty=false、所有來源 ID 保留、缺欄位預設 true。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter FullyQualifiedName~ProjectEditSaveStateTests`，確認因缺少契約失敗。
- [ ] 實作上述型別與既有 history 接點；偏好變更不能把未保存名稱／clips 一起提交。
- [ ] 重跑測試並執行 Core 全套；確認 0 failed。
- [ ] 僅提交本項修改，訊息 `feat: separate saved project state from working edits`。

### Task 2: 有界原子復原草稿

**Files:** 修改 `src/ScreenRecorder.Core/Projects/IProjectStore.cs`、`src/ScreenRecorder.Infrastructure/Projects/JsonProjectStore.cs`；新增 Core `ProjectEditDraft.cs`、Infrastructure `ProjectDraftStore.cs`；新增 `tests/ScreenRecorder.Core.Tests/ProjectDraftStoreTests.cs`。

**Interfaces:** `ProjectEditDraft(Guid Id, Guid ProjectId, long BaseRevision, long Sequence, string Name, ImmutableArray<ProjectClip> Clips)`；`IProjectHandle` 增加 `ReadDraftAsync(CancellationToken)` → `Task<ProjectEditDraft?>`、`SaveDraftAsync(ProjectEditDraft,CancellationToken)`、`DiscardDraftAsync(Guid,CancellationToken)` → `Task`。使用既有專案锁与 bound directory，不接受 UI 提供草稿路徑。

- [ ] RED 測試 `DraftRoundTripsWithoutMediaCopy`、`StaleDraftCannotOverwriteNewerDraft`、`CommittedDraftIsNotRecoveredAfterCleanupCrash`、`DiscardTombstoneSurvivesCleanupFailure`、`CorruptOrForeignDraftCannotReplaceProject`、`DraftWriteFailurePreservesPreviousCopy`；驗證只一草稿一備份、來源 checksum 不變。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter FullyQualifiedName~ProjectDraftStoreTests` 確認 RED。
- [ ] 原子寫入 `project.edits.json` 與一備份，套用現有 manifest 大小／深度上限；丟棄寫 tombstone 後清理，正式保存寫 ResolvedDraftId 後清理。序列化寫入、比較 base revision 與 sequence。重用既有安全檔案存取，不新增未受限路徑 API。
- [ ] 相容 schema 1 的新增可選欄位；未經新程式修改的舊檔能載入。由舊程式改寫造成 base mismatch 時只警告，不自動套用草稿。
- [ ] 重跑上述測試與 ProjectStoreTests／安全路徑測試，確認 0 failed；提交本項。

### Task 3: Coordinator、IPC 與明確儲存操作

**Files:** 修改 `src/ScreenRecorder.Recorder/Services/ProjectRecordingCoordinator.cs`、`ProjectIpcDispatcher.cs`、`src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs`、現有 IPC command registry、`src/ScreenRecorder.UI/Projects/ProjectClient.cs`、`ProjectWorkspaceViewModel.cs`、`ProjectWorkspaceProperties.cs`、`RecordingContentController.cs`、`ProjectWorkspaceView.axaml.cs`、`src/ScreenRecorder.UI/Views/MainWindow.axaml.cs`；新增 `ProjectWorkspaceSaveLifecycle.cs`、`tests/ScreenRecorder.Media.Tests/ProjectExplicitSaveTests.cs`。

**Interfaces:** Coordinator 增加 `Task DiscardEditsAsync(CancellationToken)`、`Task ResolveDraftAsync(bool restore,CancellationToken)`、`Task SetAutoExportOnStopAsync(bool enabled,CancellationToken)`；對應 IPC 命令沿用 ProjectId、ExpectedRevision、OperationId 檢查。Snapshot 增加明確 `HasUnsavedEdits`、`HasRecoverableDraft`、`DraftError`、`AutoExportOnStop`，IsDirty 使用 HasUnsavedEdits。UI 定義 `UnsavedDecision { Save, Discard, Cancel }` 及 `Task<bool> ResolveUnsavedAsync(CancellationToken)`。

- [ ] RED 測試 `EditUndoRedoDoNotSaveManifest`、`SaveAcknowledgementControlsDirtyState`、`CancelBlocksCloseSwitchRecordExport`、`ReturnHomeKeepsWorkingEdits`、`ResumeThenDiscardKeepsNewRecording`、`DraftRecoveryRequiresChoice`、`InvalidPropertyInputBlocksSave`、`RepeatedCommandDoesNotApplyTwice`。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectExplicitSaveTests`，記錄 RED。
- [ ] 移除編輯／close 的無條件 Flush；編輯在 500ms debounce 後寫最新草稿，關閉前完成當次保存／捨棄選擇，取消保留狀態。背景草稿錯誤可見且不結束 Recorder。
- [ ] 所有實際關閉／切換／續錄／輸出入口共用 ResolveUnsavedAsync；返回首頁只隱藏編輯器。錄製完成沿用耐久提交，更新保存基準與 history，不以草稿覆蓋新增素材。preview/export 用明確選定的工作快照。
- [ ] 更新中英文提示與 UI 名稱下方單一儲存狀態；重跑 targeted 與 recording lifecycle 全套，確認 0 failed；提交本項。

### Task 4: 停止自動輸出選項與重複輸出檢查

**Files:** 修改 `RecordingContentController.cs`、`src/ScreenRecorder.UI/ViewModels/MainViewModel.RecordingContent.cs`、`src/ScreenRecorder.UI/Views/MainWindow.axaml`、`src/ScreenRecorder.Recorder/Services/RecordingContentExportJob.cs`；新增 Core `ProjectExportFingerprint.cs`、`ProjectExportReceipt.cs`、Infrastructure `ProjectExportReceiptStore.cs`、測試 Core `ProjectExportFingerprintTests.cs`、Media `ProjectStopExportPolicyTests.cs`。

**Interfaces:** `ProjectExportFingerprint.Compute(RecordingProject project, string exportProfile)` → `string`，以 canonical SHA256 計算畫布、依序 clips/effects、引用來源 checksum、輸出 profile；不含 Name、revision、view state、AutoExportOnStop。`ProjectExportReceipt(string ContentFingerprint,string Path,long Length,string Sha256)`；receipt store 提供 `ReadAsync/WriteAsync`，僅成功輸出後保存。UI `ExistingExportDecision { OpenExisting, ExportAgain, Cancel }`。

- [ ] RED 測試 `AutoExportDefaultPreservesSimpleFlow`、`StopWithoutExportDurablySavesRecording`、`PolicyPersistsWithoutSavingEdits`、`OnlyContentChangesInvalidateFingerprint`、`SameSizeReplacementInvalidatesReceipt`、`FailedExportPreservesSources`、`RepeatedExportNeverOverwritesFile`。
- [ ] 執行 Core fingerprint 與 Media policy 測試確認 RED。
- [ ] 將 finish recording 與 export 明確分開；checkbox 禁用於忙碌／錄影狀態。關閉時顯示「錄製已保存，尚未輸出 MP4」。手動兩入口共用 output check；串流 SHA256 核對並可取消，核對不通過重新輸出。receipt 原子保存於專案內固定檔名且不可作任意路徑讀寫入口。
- [ ] 先保存／解決編輯，再建立 immutable export snapshot；不連動已暫緩的 pre-render cache。
- [ ] 重跑上述測試及 RecordingContentSimpleFlowTests、RecordingContentExportLifecycleTests，確認 0 failed；提交本項。

### Task 5: 左側清單拖曳與一致排序

**Files:** 新增 `src/ScreenRecorder.UI/Projects/ProjectWorkspaceListDrag.cs`；修改 `ProjectWorkspaceList.cs`、`ProjectWorkspaceTimeline.cs`、`ProjectWorkspaceView.axaml`、`ProjectWorkspaceView.axaml.cs`；新增 `tests/ScreenRecorder.Media.Tests/ProjectClipListDragTests.cs`。

**Interfaces:** 新增 `ProjectClipDropTarget(Guid ClipId, Guid? BeforeClipId)`；`TryResolveDrop(Guid draggedId, Guid? targetId, bool after, out ProjectClipDropTarget target)` → `bool`；最後呼叫既有 `MoveClipAsync(Guid,Guid?)`，不新增第二套排序狀態。

- [ ] RED 測試 `CompactAndThumbnailUseSameMove`、`GroupMovesAsUnit`、`DropInsideGroupSnapsToGroupBoundary`、`NoOpAddsNoUndo`、`OneDragOneUndo`、`BusyTransitionCancelsDrop`、`EscCancelsDrag`。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectClipListDragTests` 確認 RED。
- [ ] 6 DIP 門檻開始拖曳；上下 24 DIP 區域啟動有界 autoscroll，釋放／取消／離開停止 timer 與 capture；顯示 insertion line。drop 再查 CanEditTimeline、pending property draft、ProjectId/revision，過期取消。
- [ ] 重跑測試及 timeline/group tests；人工確認頭尾移動、虛擬清單大量項目、兩種模式；無 GUI 時記錄 BLOCKED，不假裝驗收完成；提交本項。

### Task 6: 鉛筆按鈕、標題與整合驗收

**Files:** 修改 `src/ScreenRecorder.UI/Projects/Editor/EditorToolContent.cs`、`ProjectWorkspaceView.axaml`、`src/ScreenRecorder.UI/Views/MainWindow.axaml`、`src/ScreenRecorder.Core/Localization/LocalizationService.cs`、`tests/ScreenRecorder.Media.Tests/ProjectEditorChromeTests.cs`；新增 `docs/acceptance/2026-10-09-project-save-export-workflow.md`。

**Interfaces:** 新增 `edit-recording` 圖示 key；重用 rename command。rename Button 的 tooltip 與 AutomationProperties.Name 為「更改專案名稱」／「Rename project」，圖示專用淡底細框按鈕接在名稱後方；title「編輯錄製內容」／「Edit Recording」。

- [ ] RED 測試檢查標題、首頁 icon key、rename 是有可及性名稱的 Button、空專案不顯示已儲存，按鈕 disabled/focus 樣式存在。
- [ ] 執行 `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectEditorChromeTests`，確認 RED。
- [ ] 實作 16 DIP 線條圖示、32 DIP rename 按鈕與 6 DIP 名稱間距；名稱容器寬度受 header 剩餘空間約束並 ellipsis，按鈕不能被文字擠掉。保留 hover、pressed、keyboard focus、disabled 差異。
- [ ] `dotnet build ScreenRecorder.sln -c Release`、`dotnet test ScreenRecorder.sln -c Release`；與基準比較，不能掩蓋既有快速輸出失敗。執行網站既有測試命令（從 package.json 取得），記錄結果。
- [ ] 使用自建素材檢查 Save/Discard/Cancel、恢復、續錄、排序、輸出與長名稱中英文配置；Native Windows 無環境保留待驗收清單。截圖只限測試版，不能展示其他視窗內容。
- [ ] 寫驗收報告；完成一次獨立整體 code review 並處理可靠性問題，再提交本項。不因本機通過而宣稱跨平台實機完成，不觸發遠端建置。

## 自我檢查

規格 1 由 Task 4 覆蓋；規格 2 由 Task 1–3 覆蓋；規格 3 由 Task 5 覆蓋；規格 4 由 Task 6 覆蓋；格式安全、錯誤路徑與驗收由 Task 2、3、6 覆蓋。所有新方法由本計畫定義；現有方法以編譯前實際簽章核對，不為配合計畫繞過既有安全限制。

狀態：計畫已完成自我檢查，待使用者審閱與確認執行方式；目前尚未開始本計畫的產品程式修改。
