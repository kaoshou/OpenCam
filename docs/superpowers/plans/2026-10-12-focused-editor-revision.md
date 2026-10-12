# Focused Editor Revision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 撤回昨日實驗分支的產品方向，從 0.3.1 基準完成使用者指定的八項編輯器／安裝修正。

**Architecture:** 保留原始錄製素材；加入非巢狀複合片段並透過共用映射供預覽、時間軸與輸出使用。編輯器離開與首頁互斥集中守門，手動輸出使用有驗證與安全覆寫的明確檔案目的地。既有自動輸出與正常錄影流程保持。

**Tech Stack:** C# / .NET 8、Avalonia 11.2.5、xUnit、既有 FFmpeg 與 IPC、Inno Setup；不新增第三方套件。

**Spec:** `docs/superpowers/specs/2026-10-12-focused-editor-revision-design.md`（使用者已確認）

## Global Constraints

- 從 `646df8792ee720b504953de63365098bde3de2ca` 建立隔離工作目錄；執行前先使用 using-git-worktrees 技能。
- `codex/editor-direct-manipulation`、未提交修改及原有 dirty docs 完整保留。不得 reset、刪除或整批 cherry-pick 昨日實作。
- 不導入素材匯入、疊加圖層、直接拖曳變形、即時效果或圓角新功能。原基準既有屬性功能保留。
- Windows 應用程式名稱為 `OpenCam`；既有 AppId 不變。
- schema 1 可讀；新的複合片段使用 schema 3；schema 2 及未知格式拒絕，不降級、不靜默恢復成舊備份。
- 原素材不改寫、合併不立即轉檔。單次編輯可復原；不承諾跨重啟操作歷史。
- 不改版本、不推送、不合併、不建立標籤、不觸發 Actions、不 Release。
- 本計畫的 commit 只限新開發分支，每次明列檔案，不包含使用者既有變更。
- 實機無法執行的測試標示待驗收；不為了宣稱完成而略過失敗，不再次以無限長自動驗收延後交付。

## Review Focus

1. 不同 time base 的合併與連續裁切：累積時長不能因逐段取整而漂移（Tasks 2、3）。
2. 未知格式的有效備份／草稿：不能讓備份回退繞過格式拒絕或覆蓋新版檔案（Task 2）。
3. 編輯視窗隱藏／退出失敗與全域快捷鍵：不可提早恢復首頁錄影（Task 5）。
4. 覆寫確認後目標遭替換或轉成連結：不能覆蓋錯誤檔案或原素材（Task 8）。
5. ListBox 回收、被動狀態刷新或拖曳排序：不可誤觸 seek，群組框也不能殘留到另一張卡片（Tasks 6、7）。

## 執行／驗證慣例

以下命令在新工作目錄执行。若 `dotnet` 不在 PATH，先確認本機 `/tmp/opencam-dotnet8.10THHC/sdk/dotnet` 是否仍存在，僅設定本次命令環境。FFmpeg 路徑同理，驗證其可執行與版本，不預設暫存目錄永遠存在。

每個 task 依序保留 RED 命令／失敗斷言、最小修補、GREEN 結果。新增測試檔案列在 Files 中；測試利用既有 fixture，不另外建立大型測試平台。以下斷言示例規定測試結果，fixture 命名由現有測試約定決定。

## Task 1：隔離基準及 Windows 安裝名稱（需求 1）

**Files:** Modify `installer/OpenCam.iss`; Create `tests/ScreenRecorder.Media.Tests/InstallerProductNameTests.cs`.

**Interfaces:** 不改 runtime API；安裝包的 AppName 與 UninstallDisplayName 必須解析為 `OpenCam`。

- [ ] 使用 worktree 技能建立新工作目錄，確認 HEAD 是指定基準；將本計畫及已核准 spec 明確複製進新分支，不複製任何昨日產品程式碼。
- [ ] 寫 `InstallerUsesPlainProductNameAndPreservesAppId`：assert `MyAppName == "OpenCam"`、`AppName`／解除安裝顯示名稱引用此值、AppId 與基準字串完全相同。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~InstallerProductNameTests`；確認 RED 是名稱多了中文。
- [ ] 只修改名稱及必要的 UninstallDisplayName；不改安裝位置、捷徑識別或版本。
- [ ] 重跑上述測試預期 0 failed，明列上述兩檔及設計文件 commit：`fix: use OpenCam as Windows installer product name`。

## Task 2：複合片段、格式相容及可復原編輯（需求 4）

**Files:** Modify `src/ScreenRecorder.Core/Projects/{RecordingProject,ProjectValidation,ProjectClipEdit,ProjectClipEditor,ProjectTimeline,ProjectExportFingerprint}.cs`; Create `src/ScreenRecorder.Core/Projects/{ProjectClipParts,ProjectClipProjection}.cs`; Modify `src/ScreenRecorder.Infrastructure/Projects/{JsonProjectStore,ProjectDraftStore}.cs`; Create `tests/ScreenRecorder.Core.Tests/{ProjectCompoundClipTests,ProjectCompoundStoreTests}.cs`.

**Interfaces:**
- `ProjectClip.Parts: ImmutableArray<ProjectClipPart>`，空陣列為原普通片段；有值時為複合片段，普通 SourceId/InPts/OutPts 不作為虛構素材使用。
- `ProjectClipPart(Guid Id, Guid SourceId, long InPts, long OutPts, ProjectClipSettings Settings)`；Settings 包含現有 Volume、Muted、FadeInTicks、FadeOutTicks、Crop、Scale、PositionX、PositionY，沒有圓角或昨日新增屬性。
- `ProjectClipEdit.Merge(Guid ClipId, bool WithPrevious)`，只處理直接相鄰兩個邏輯片段。
- `ProjectClipProjection.Expand(RecordingProject project) -> ImmutableArray<ProjectClipSpan>`；`ProjectClipSpan(Guid LogicalClipId, Guid PartId, ProjectClip Leaf)`。Leaf 是原單來源格式，Id 為穩定 PartId；非巢狀，GroupId 清空，原效果保留。普通片段的 PartId 等於 Clip.Id。
- `ProjectTimeline.Clips` 保持一個邏輯片段一個 span；`Locate` 回傳邏輯 ClipId 及真正 SourceId/PTS。內部計算沿用精確累積有理數。

- [ ] 新增測試：不同來源 2 秒 + 3 秒合併，assert `Clips.Length == 1`、`Parts.Length == 2`、總長 5 秒、展開的兩份 Settings 等於原值；undo 為原兩段，redo 為一段，來源指紋不變。
- [ ] 新增 RED：重複合併不巢狀、首尾操作拒絕、跨群組拒絕、同群組合併保留群組、移動／改名／刪除單一複合片段；不同 time base 跨內部邊界 split/trim/remove-range 後來源區間與總長正確。
- [ ] 新增 store RED：schema 1 讀取不寫檔；首次寫入 3 前保留舊原文；schema 2／未來 schema 在主檔、草稿和備份拒絕且 bytes 不變；collection、來源、depth、4 MiB manifest 限制仍生效。
- [ ] Run `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter 'FullyQualifiedName~ProjectCompound'`，確認上述功能缺失的 RED。
- [ ] 實作上述模型、Merge 分派與來源邊界編輯。內部區間總數連同普通片段上限 10000；去除／切開部分區間時按原素材時間映射並限制 fade，不累積逐段取整誤差。相同普通來源連續區間可保留二 parts，不強制最佳化。
- [ ] 更新 fingerprint 依展開後影響影音的來源／設定計算，純改名／群組不影響內容 fingerprint。混合屬性不得默認覆寫。
- [ ] 重跑 compound 與既有 ProjectClipEdit、ProjectTimeline、ProjectStore、ProjectDraftStore、ProjectExportFingerprint 測試，預期 0 failed；明列本 task 檔案 commit：`feat: add undoable non-destructive compound clips`。

## Task 3：複合片段影音、縮圖、波形與快速輸出（需求 4）

**Files:** Modify `src/ScreenRecorder.Media/Projects/{ProjectRenderPlan,ProjectStillSelection,ProjectExportStrategy,ProjectPreviewJobs,ProjectMediaExportJobs,ProjectRemuxJobs}.cs`; Modify `src/ScreenRecorder.Recorder/Services/{ProjectFrameService,ProjectWaveformService,ProjectPreviewCoordinator,ProjectFfmpegFastExporter,ProjectFfmpegNormalizedAudioExporter,ProjectFfmpegExporter}.cs`; Modify `src/ScreenRecorder.Infrastructure/Projects/ProjectFrameCache.cs`; Create `tests/ScreenRecorder.Media.Tests/ProjectCompoundMediaTests.cs`.

**Interfaces:** 消費 Task 2 的 Expand 與 Locate。RenderPlan 的素材迭代使用 Leaf，對外 frame／waveform 回應仍使用 LogicalClipId；既有 IProjectAudioOutput 和作業生命週期不改。快取 key 包含完整 parts 配方，避免命中未合併或舊裁切畫面。

- [ ] 自建不同顏色／聲音的兩段媒體；測試合併前後 `FrameCount`、`AudioSampleCount`、接點來源及整體順序相同，seek 跨接點定位正確。測試尾端 seek、無聲+有聲、不同 time base。
- [ ] 測試複合片段縮圖取第一個可見區間，波形覆蓋全段；未加效果的完整錄製合併後仍選原可行快輸出路徑，不因 Parts 非空而降為重編碼。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectCompoundMediaTests`，確認 RED。
- [ ] 將素材解析統一接到 Expand，將邏輯 ID 與 part ID 映射明確保留；不建立第二套播放器或昨日 overlay composition 引擎。
- [ ] 重跑新增及既有 ProjectRenderPlan、ProjectFastExport、ProjectConcatExport、ProjectPreviewStreaming、ProjectFrameCache 測試，預期 0 failed；驗證實際輸出可解碼、聲音時長與素材 hash 不變。
- [ ] 明列本 task 檔案 commit：`feat: play and export compound clips through shared media mapping`。

## Task 4：合併按鈕與混合屬性（需求 4）

**Files:** Modify `src/ScreenRecorder.UI/Projects/ProjectWorkspaceTimeline.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceProperties.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceViewModel.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml`, `src/ScreenRecorder.Core/Localization/LocalizationService.cs`; Create `tests/ScreenRecorder.Media.Tests/ProjectCompoundEditorTests.cs`.

**Interfaces:** `MergeWithPreviousCommand`、`MergeWithNextCommand` 使用 Task 2 Merge；`CanMergePrevious/Next` 分別守相鄰與群組條件；`HasMixedProperties` 表示各 part 值不同，不使用第一段假扮全體。

- [ ] 寫 RED：第一段向前及最後段向後停用；成功後單一卡片、單一時間軸片段、總長不變、名稱採最前段、選到新段起點；混合屬性顯示本地化提示，未按套用前 parts 不變。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectCompoundEditorTests`。
- [ ] 加入有小 icon 的兩個合併動作，接既有 command/IPC。明確套用的屬性對各 part 使用相同值，fade 按各 part 可用長度限制並在 UI 說明；一次套用可整體 undo。
- [ ] 重跑上述及 ProjectInspector、ProjectEditorInteraction 測試，預期 0 failed；明列檔案 commit：`feat: expose compound clip merge and mixed property editing`。

## Task 5：統一離開與首頁互斥（需求 5、6）

**Files:** Modify `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceSaveLifecycle.cs`, `src/ScreenRecorder.UI/Projects/RecordingContentController.cs`, `src/ScreenRecorder.UI/ViewModels/MainViewModel.RecordingContent.cs`, `src/ScreenRecorder.UI/Views/MainWindow.axaml.cs`, `src/ScreenRecorder.Recorder/Services/ProjectIpcDispatcher.cs`; Create `tests/ScreenRecorder.Media.Tests/ProjectEditorExitGuardTests.cs`.

**Interfaces:** `ProjectWorkspaceView.TryLeaveEditorAsync() -> Task<bool>` 為所有離開入口；`MainViewModel.IsContentEditorOpen` 供 command can-execute 與快捷鍵守門；後端沿用已確認的內容編輯狀態拒絕衝突命令。

- [ ] 三個入口共用參數化 RED：有 dirty/draft 時各顯示一次提示；取消不關閉／不隱藏；儲存失敗不離開；選不儲存確實放棄本次編輯；成功離開後預覽聲音停止。
- [ ] RED：編輯器可見時，滑鼠及 F9／F10／新開專案／救援／首頁輸出均不能啟動；儲存或 StopPreview 失敗維持鎖定；重複離開只有一個 dialog，預覽 IPC 未確認不可解除。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectEditorExitGuardTests`。
- [ ] 實作單一路徑及 try/finally 重入旗標，僅安全完成後解鎖；不得使用 Hide 繞過未儲存檢查，也不得影響錄影中原有關閉保護。
- [ ] 重跑新增、RecordingContentEditorState、RecordingProjectHomeFlow、ProjectExplicitSave 測試，預期 0 failed；明列檔案 commit：`fix: guard all editor exits and block conflicting home actions`。

## Task 6：群組視覺辨識（需求 3）

**Files:** Modify `src/ScreenRecorder.UI/Projects/Editor/ProjectClipCard.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml`; Create `src/ScreenRecorder.UI/Projects/Editor/ProjectGroupAppearance.cs`, `tests/ScreenRecorder.Media.Tests/ProjectGroupAppearanceTests.cs`; Modify `src/ScreenRecorder.Core/Localization/LocalizationService.cs`.

**Interfaces:** `ProjectGroupAppearance.Resolve(IReadOnlyList<ProjectClip> clips, Guid clipId) -> GroupAppearance`；record 包含 `int? Ordinal, bool First, bool Last, int PaletteIndex`，无群組沒有色框或標籤。

- [ ] 寫 RED：相鄰同群組 ordinal/palette 相同，首尾判斷正確、無群組無裝飾；重新排序後 ordinal 可重排但同群組一致；卡片 recycling 清除上一筆裝飾。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectGroupAppearanceTests`。
- [ ] 實作低彩度主題 brush、側線、首尾圓角及群組標籤，保留選取框優先層級；精簡模式顯示短標籤及 tooltip，不增加過高列高。
- [ ] 重跑新增與 ProjectWorkspaceLayout、ProjectClipListDrag 測試；分別渲染明暗／精簡／一般模式確認標籤不被裁掉。明列檔案 commit：`feat: visually identify clip groups in the editor list`。

## Task 7：使用者選取定位與全螢幕進度（需求 2、7）

**Files:** Modify `src/ScreenRecorder.UI/Projects/ProjectWorkspaceViewport.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceTimeline.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceListDrag.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml`; Create `src/ScreenRecorder.UI/Projects/ProjectWorkspaceFullscreenSeek.cs`, `tests/ScreenRecorder.Media.Tests/ProjectFullscreenSeekTests.cs`, `tests/ScreenRecorder.Media.Tests/ProjectUserSelectionSeekTests.cs`.

**Interfaces:** `ProjectWorkspaceViewModel.SelectClipForPlaybackAsync(Guid clipId) -> Task` 停止播放、依 LogicalClipId 定位與刷新 still；`SeekPreviewAsync(long ticks, bool resumePlayback) -> Task` 在一次操作完成時提交；fullscreen drag 的暫存位置不綁到每次 decoder seek。

- [ ] RED：點不同卡片或時間軸片段定位起點且暫停；同一片段再次點選也可回到起點；刷新 snapshot、restore selection、drag reorder 不發送 seek。
- [ ] RED：一般模式無額外 seekbar；全螢幕顯示；拖動過程不反覆 decode，放開定位一次；原播放／暫停狀態保持；鍵盤可定位；焦點或拖曳期間不隱藏；取消拖曳不提交。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~ProjectFullscreenSeekTests|FullyQualifiedName~ProjectUserSelectionSeekTests'`。
- [ ] 加入只由使用者 activation 觸發的 handler，不在 OnSelectedClipChanged 無条件 seek。全螢幕進度條掛原預覽控制列，不導入昨日直接操作層；IPC 舊回應不得覆蓋較新定位。
- [ ] 重跑新增及 ProjectPreviewControls、ProjectPlaybackLifecycle、ProjectClipListDrag 測試，預期 0 failed；明列檔案 commit：`feat: seek selected clips and show fullscreen-only progress`。

## Task 8：MP4 儲存檔名與安全交付（需求 8）

**Files:** Create `src/ScreenRecorder.Core/Projects/ProjectExportTarget.cs`, `src/ScreenRecorder.Infrastructure/Projects/ProjectExportDestination.cs`, `src/ScreenRecorder.UI/Projects/ProjectExportFileDialog.cs`; Modify `src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml.cs`, `src/ScreenRecorder.UI/Projects/ProjectWorkspaceViewModel.cs`, `src/ScreenRecorder.UI/Projects/ProjectClient.cs`, `src/ScreenRecorder.UI/ViewModels/MainViewModel.RecordingContent.cs`, `src/ScreenRecorder.UI/Views/MainWindow.axaml.cs`; Modify `src/ScreenRecorder.Recorder/Services/{ProjectIpcDispatcher,IRecordingContentExporter,RecordingContentExportJob,ProjectFfmpegExporter,ProjectFfmpegFastExporter,ProjectFfmpegNormalizedAudioExporter}.cs`; Create `tests/ScreenRecorder.Media.Tests/ProjectNamedExportTests.cs`.

**Interfaces:**
- `ProjectExportTarget(string FilePath, Guid? ReplacementAuthorization)`；新增明確 `ProjectRequest.ExportTarget`，不把原本 Path 的資料夾默默解釋為檔案。
- `PrepareNamedExport` 檢查目的地並回傳是否已存在及有期限的單次確認 token；token 綁定 project/revision/server、完整路徑及現有檔案 identity/hash；UI 顯示覆寫確認後才傳入 ExportTarget。沒有既有檔案時仍以排他方式建立。
- `IRecordingContentExporter` 增加 named export entry，原目錄自動輸出 entry 保持給自動停止使用。內部各策略共用 `ProjectExportDestination` 的 staging／verified publish，不能各自直接覆寫。

- [ ] RED：首頁與編輯器叫 SaveFilePicker，預填專案名 `.mp4`；取消零工作，使用者指定檔名為真正輸出檔；自動停止仍自動命名。
- [ ] RED：拒絕素材路徑、manifest／journal、連結、錯誤副檔名；既有檔未確認不變；確認後換檔／改檔／換 parent/link 拒絕；暫存輸出驗證失敗或取消時原檔 hash 不變。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectNamedExportTests`。
- [ ] 實作原生檔案另存視窗、一次性授權及相同目錄暫存驗證。沿用 BoundDirectory 對目錄及檔案 identity 的保護；無法保證安全取代的平台情境必須回報失敗，不能降為 delete+move。
- [ ] 重跑新增及 ProjectManualExport、ProjectExistingExport、RecordingContentExportIntegration、ProjectPathSecurity 測試，預期 0 failed；明列檔案 commit：`feat: choose MP4 filename with verified safe output publication`。

## Task 9：整體回歸與使用者交付

**Files:** Modify `MANUAL_TEST_CHECKLIST.md`; Create `docs/verification/2026-10-12-focused-editor-revision.md`；如既有說明與此次行為矛盾，僅修正對應 `docs/USER_GUIDE.zh-TW.md`、`docs/USER_GUIDE.en-US.md` 段落。

**Interfaces:** 本 task 不新增 runtime API，也不增加產品功能。

- [ ] Run `dotnet restore ScreenRecorder.sln`、`dotnet build ScreenRecorder.sln -c Release`、`dotnet test ScreenRecorder.sln -c Release --no-build`，逐一記錄 exit code、passed/failed/skipped，不把 skip 當通過。
- [ ] 執行自建素材端到端：錄製片段 fixture → 兩次合併 → 裁切／復原 → 保存重開 → 續錄 → 預覽跨接點 → 明確檔名輸出；assert 原素材 hash 不變、可解碼、影音順序／時長正確。fixture 不宣稱是實體螢幕錄製。
- [ ] 短時間原生操作可行時檢查八項；GUI／Windows 不可用則記錄 blocker，直接交付可測版與八項人工清單，不重新開啟長時間 stress 工作。
- [ ] 全分支審查聚焦資料遺失、預覽生命週期、格式拒絕、輸出路徑及誤解鎖；問題修正後重跑受影響測試。不得在未完成媒體映射時宣稱合併功能完成。
- [ ] 明列驗證文件 commit：`docs: record focused editor verification and manual checks`。回報已完成／待實機驗收項目與本機啟動路徑；不自動推送／合併／發布。

## 自審結果

八項需求對應：1→Task 1；2→Task 7；3→Task 6；4→Tasks 2–4；5、6→Task 5；7→Task 7；8→Task 8。五個 Review Focus 均有對應測試。昨日功能沒有作為相依導入；現有未剪輯快速輸出、自動輸出及來源保護都保留回歸。新模型、mediaplan 與 UI 邏輯 ID／part ID 需一併完成才可交付「合併可用」聲明。
