# GIF 與單一片段匯出 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 整片新增 GIF，單一片段提供滑鼠移入展開的 MP4／GIF 匯出子選單，完整保留剪輯語意與來源安全。

**Architecture:** Core 定義格式、範圍及不可變投影；Recorder 持有原 owner/revision 並執行選定投影。MP4 保留原策略，GIF 共用畫面合成但使用獨立無音訊編碼與驗證。UI 共用一條準備／確認／送出流程，不接觸底層轉檔程序。

**Tech Stack:** C#/.NET 8、Avalonia、既有 FFmpeg/ffprobe、xUnit；不加應用依賴。

**Spec:** ../specs/2026-10-21-gif-and-clip-export-design.md

## Global Constraints

- GIF 不設定影片長度上限。目標範圍達 5 分鐘時警告，使用者仍可繼續。
- GIF 寬度：480、640、960 像素、原始大小；預設 640，保持比例。
- GIF 幀率：10、15、20 FPS；預設 15 FPS。預設循環播放。
- 寬度是上限、不放大小畫布；原始大小指專案畫布。首版固定循環、選項不持久化。
- 固定顯示「GIF 不包含音訊。」／「GIF does not include audio.」；新文案均有 zh/en。
- 單一邏輯片段包括合併的保留子片段，不擴張群組；輸出從零開始，不改專案、來源或 Undo。
- 正式錄製、自動匯出、MP4、網頁、字幕及書籤語意不變；無 schema／版本號變更。
- bound-handle、owner、revision、lease、安全發布及取消清理不可弱化。無整片 RAM 快取。
- 使用者已確認此計畫並授權本工作階段實作；包含原本影片「匯出…」按鈕的 GIF 入口。排程維持停用，不推送、不發布。
- AGENTS.md 的初期 GIF 不在範圍已由此次明確需求擴充，其資料安全與驗收規則仍適用。

## Review Focus

- 對話框期間切專案／刪片段／保存改版：拒絕過期目標，不匯出另一片段（Task 1、4）。
- 非整秒、多素材、15 FPS 長片：以累積時間量化，不能逐片漂移或帶出裁切外影格（Task 2）。
- 極小／奇數畫布、很短片段：正整數尺寸、至少一張有效畫面、不默默放大（Task 2）。
- 取消恰逢驗證／發布、輸出路徑被替換：不假成功，不刪舊檔，不提前解鎖（Task 3）。
- 滑鼠移入父選單、移入子項目、鍵盤、不同 DPI：可選擇正確目標、不誤啟動對話框（Task 4、5）。

## Execution and evidence

Status: Tasks1–5 implementation and automated verification completed locally. Final Core255/Media975 passed,20 skipped,0 failed; see docs/verification/2026-10-21-gif-and-clip-export.md for failure history, RED/GREEN evidence, design rulings, and explicit manual acceptance limits. The original task checklists below are retained as the approved plan, not a claim that unexecuted native/manual checks passed.

沿用 .worktrees/focused-editor-revision；執行前核對工作樹及使用者變更。不使用額外代理，依既有簡化審查要求自行逐項檢視。以下相對路徑皆以此工作樹為根。

測試命令的 dotnet 使用可用 .NET 8 SDK；既有本機路徑 /tmp/opencam-sdk8-20261020/dotnet 須先確認仍存在。FFmpeg 使用測試套件既有尋找方式，記錄實際版本。每個 task 依 RED → 最小實作 → GREEN；僅 stage 該 task 明列檔案並本機 commit，不用 git add .。

本次僅查詢 /tmp/opencam-task2-ffmpeg.GOzyFj/ffmpeg 能力：GIF encoder 支援 pal8，muxer 支援 loop=0、final_delay，palettegen 可用。這不是輸出驗證，也不是 Windows 打包版本驗證。Task 2 必須先對實際工具做合成證據；若能力不符，停止並回報，不改用未授權下載或降低檢查。

### Task 1: 匯出契約、選定範圍與指紋

**Files**

- Create: src/ScreenRecorder.Core/Projects/ProjectExportOptions.cs
- Create: src/ScreenRecorder.Core/Projects/ProjectExportSelection.cs
- Modify: src/ScreenRecorder.Core/Projects/ProjectExportFingerprint.cs
- Modify: src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs
- Create: tests/ScreenRecorder.Core.Tests/ProjectExportSelectionTests.cs
- Modify: tests/ScreenRecorder.Core.Tests/ProjectExportFingerprintTests.cs
- Modify: tests/ScreenRecorder.Core.Tests/IpcTests.cs

**Interfaces**

- Produce enums ProjectExportFormat { Mp4 = 0, Gif = 1 }, ProjectExportScope { WholeProject = 0, Clip = 1 }.
- Produce record GifExportOptions(int? MaximumWidth = 640, int FramesPerSecond = 15); null width means original. Loop is fixed, not request-controlled.
- Produce record ProjectExportOptions(ProjectExportFormat Format = Mp4, ProjectExportScope Scope = WholeProject, Guid? ClipId = null, GifExportOptions? Gif = null).
- Produce ProjectExportSelection.Create(RecordingProject snapshot, ProjectExportOptions options) -> RecordingProject. Preserve project ID/revision/canvas and source ownership; only select immutable clip array, without modifying original.
- Add nullable ProjectRequest.ExportOptions. Absent means legacy request behavior; reject new options combined with ExportWebPlayer. Add fingerprint overload Compute(RecordingProject snapshot, ProjectExportOptions options); retain old overload and exact old whole-MP4 behavior.

- [ ] Write selection, validation, serialization and fingerprint RED tests. Required assertions:
  `Assert.Single(selected.Clips);` `Assert.Equal(original.Revision, selected.Revision);` `Assert.Equal(beforeJson, Serialize(original));`
  Verify compound parts retained, adjacent grouped sibling absent, invalid enums/width/FPS/empty clip ID rejected; MP4 cannot carry GIF settings; whole-project cannot carry ClipId. JSON without options remains legacy.
- [ ] Run `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter "FullyQualifiedName~ProjectExportSelectionTests|FullyQualifiedName~ProjectExportFingerprintTests|FullyQualifiedName~IpcTests"`; expect new tests fail for missing contract.
- [ ] Implement validation and immutable selection using ProjectClipProjection. Fingerprint new scope/format/effective clips/GIF profile, width/FPS/loop; old MP4 receipts stay byte-for-byte compatible. Do not reuse whole-project receipts for single-clip exports, even if visually identical.
- [ ] Run same tests; require all pass, including stale/nonexistent ID and original-object invariance.
- [ ] Stage only listed files; commit `feat: define scoped media export contracts`.

### Task 2: 有界 GIF 合成、編碼及格式驗證

**Files**

- Modify: src/ScreenRecorder.Media/Projects/ProjectMediaExportJobs.cs
- Create: src/ScreenRecorder.Media/Projects/ProjectGifMediaJobs.cs
- Create: src/ScreenRecorder.Media/Projects/GifTiming.cs
- Create: src/ScreenRecorder.Media/Projects/GifStructureReader.cs
- Create: tests/ScreenRecorder.Media.Tests/ProjectGifMediaTests.cs

**Interfaces**

- Consume Task 1 options and existing ProjectRenderPlan.Create(selected).
- Produce ProjectMediaJob.RenderGifClip(ProjectRenderPlan plan, int index, GifExportOptions options) -> ProjectMediaJob: composed, scaled RGB frame stream, no audio; share crop/position/background construction, do not change MP4 codec constraints.
- Produce ProjectMediaJob.AnalyzeGifPalette(int width, int height, int fps) and EncodeGif(int width, int height, int fps, int finalDelayCentiseconds) -> ProjectMediaJob, extending existing bound input mapping for the palette.
- Produce GifTiming.BoundaryCentiseconds(long frameIndex, int fps) -> long and FinalDelayCentiseconds(long durationTicks, long frameCount, int fps) -> int.
- Produce GifStructureReader.Read(Stream input) -> GifStructureInfo(int Width, int Height, long FrameCount, long DurationCentiseconds, bool InfiniteLoop), with bounded extension/sub-block parsing, no raster accumulation.

- [ ] Write RED tests for 10/15/20 FPS, dimensions 480/640/960/original, small/odd canvas, fractional trims and compound boundaries. Assert full decoded output frame colors stay in selected source intervals; MP4 composition reference agrees within GIF palette quantization.
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectGifMediaTests`; expect fail for missing implementation.
- [ ] Implement shared composition and disk-stream RGB pipeline: render selected clips into owned frame spool at global target sampling times; no audio job. Analyze spool to one palette, then reopen bound spool plus palette to encode pal8 GIF with loop=0. Do not split full movie into a palette-producing branch that queues all video frames. All reads/writes/procedures use existing cancellable process boundary.
- [ ] Implement cumulative timestamp rule: B(k)=floor((100*k + fps/2)/fps); difference between successive boundaries gives per-frame centiseconds. Last delay ends at nearest centisecond of actual duration, at least 1 cs for sub-centisecond input. Validate actual FFmpeg timestamps and final_delay against this rule, not just argument strings.
- [ ] Verify output with both bounded GIF structure inspection and complete FFmpeg decoding; exact dimensions, loop extension, nonempty frame count, no audio. Expected total duration within 1 cs of quantized target (shorter-than-1cs minimum documented); no linearly growing tolerance. Use ignore-loop during decoding so verifier terminates after one cycle.
- [ ] Run same tests using actual FFmpeg, including malformed/truncated sub-blocks and 300-second small-canvas 15 FPS case. Prove memory use does not require retaining all frames; record spool bytes and process completion. If backend cannot meet timing/odd-size contract, fix before proceeding, not widen assertions.
- [ ] Stage listed files; commit `feat: add bounded silent GIF media pipeline`.

### Task 3: Recorder routing、取消與安全發布

**Files**

- Create: src/ScreenRecorder.Recorder/Services/ProjectGifExporter.cs
- Modify: src/ScreenRecorder.Recorder/Services/ProjectRecordingCoordinator.cs
- Modify: src/ScreenRecorder.Recorder/Services/ProjectIpcDispatcher.cs
- Modify: src/ScreenRecorder.Recorder/Services/RecordingContentExportJob.cs
- Modify: tests/ScreenRecorder.Media.Tests/ProjectManualExportTests.cs
- Modify: tests/ScreenRecorder.Media.Tests/RecordingContentExportLifecycleTests.cs
- Create: tests/ScreenRecorder.Media.Tests/ProjectScopedExportTests.cs

**Interfaces**

- ProjectGifExporter implements existing IRecordingContentExporter.ExportAsync(owner, snapshot, outputDirectory, exportId, progress, ct); constructor consumes validated GifExportOptions and existing media/storage dependencies.
- Extend coordinator ExportAsync with trailing optional ProjectExportOptions? exportOptions = null; pass through dispatcher. BeginExport selects immutable projection and exporter while retaining original owner/revision checks.
- RecordingContentExportJob accepts trailing options; existing constructor calls retain legacy MP4/web behavior. Operation identity includes normalized options so duplicate IDs with different formats/scopes are rejected.

- [ ] Write RED tests for whole GIF, trimmed/compound/group-member MP4/GIF; old missing-options MP4/web/autostop unchanged. Assert original project and sources hash unchanged and no audio job invoked for GIF.
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectScopedExportTests|FullyQualifiedName~ProjectManualExportTests|FullyQualifiedName~RecordingContentExportLifecycleTests"`; require relevant new failures.
- [ ] Route MP4 selected snapshot through existing fast/fallback exporter without loosening eligibility. Route GIF through Task 2 stages with size estimates using checked arithmetic and existing disk safeguards. Validate extension against format; refuse existing file/directory. Keep source handles bound and all temporary files owned.
- [ ] Add fault tests at render, palette, encode, verify, cleanup and publish. Assert cancellation/error leaves no final success, old sentinel output unchanged, owner protected until all children/streams close; replacing destination with symlink cannot redirect writes. Test corrupt/truncated GIF, disk-full, wrong revision and repeat operation ID.
- [ ] Implement aggregate progress and cleanup-before-terminal/publication using current lifecycle. No fixed short whole-export timeout, no new retry or unconfirmed-unlock behavior; cancellation/stall protections retained. GIF receipt cannot collide with MP4; failed output cannot become reusable.
- [ ] Run above tests plus `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectFastExportTests|FullyQualifiedName~ProjectExistingExportTests|FullyQualifiedName~WebPlayerExportTests|FullyQualifiedName~ProjectStopExportPolicyTests"`; require pass.
- [ ] Stage listed files; commit `feat: route scoped exports with safe GIF publication`.

### Task 4: 原生階層選單、GIF 設定與雙語警告

**Files**

- Create: src/ScreenRecorder.UI/Projects/ProjectWorkspaceExport.cs
- Create: src/ScreenRecorder.UI/Projects/ProjectGifExportDialog.cs
- Modify: src/ScreenRecorder.UI/Projects/ProjectExportFileDialog.cs
- Modify: src/ScreenRecorder.UI/Projects/ProjectWorkspaceViewModel.cs
- Modify: src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml
- Modify: src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml.cs
- Modify: src/ScreenRecorder.UI/Projects/ProjectWorkspaceListDrag.cs
- Modify: src/ScreenRecorder.UI/Projects/ProjectWorkspaceTimeline.cs
- Modify: src/ScreenRecorder.UI/ViewModels/MainViewModel.RecordingContent.cs
- Modify: src/ScreenRecorder.UI/Views/MainWindow.axaml and its code-behind
- Modify: src/ScreenRecorder.Core/Localization/LocalizationService.cs
- Create: tests/ScreenRecorder.Media.Tests/ProjectExportMenuTests.cs
- Create: tests/ScreenRecorder.Media.Tests/ProjectGifExportDialogTests.cs

**Interfaces**

- Produce UI record ProjectExportTarget(Guid ProjectId, long Revision, Guid? ClipId, string Name, long DurationTicks).
- ProjectWorkspaceViewModel.PrepareExportAsync(Guid? clipId) -> Task<ProjectExportTarget?> resolves unsaved changes first, refreshes target from saved state, returns null on cancel/failure.
- ProjectWorkspaceViewModel.ExportFileAsync(string path, ProjectExportTarget target, ProjectExportOptions options) -> Task validates captured project/revision/clip again before IPC; preserve legacy one-argument overload.
- ProjectGifExportDialog.ShowAsync(Window owner, ProjectExportTarget target) -> Task<GifExportOptions?>; warning is inside this preparation flow. File dialog adds format and target overload; shared RequestExportAsync(ProjectExportFormat format, Guid? clipId) in workspace view orchestrates.

- [ ] Write RED menu tests: whole MP4 then GIF then existing web; parent header 「匯出片段…」 has exactly MP4/GIF children, no parent export command; hover opens native submenu without opening dialog. Selecting child sends captured clip ID; keyboard and blank-area cases follow spec.
- [ ] Write RED dialog tests: default640/15, all choices, fixed no-audio zh/en; duration299.999s no warning,300s/300.001s warning, 10-minute project/short selected clip no warning. Cancel emits zero IPC calls; continue permits >300 seconds. MP4 never opens GIF options.
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectExportMenuTests|FullyQualifiedName~ProjectGifExportDialogTests"`; expect relevant failures.
- [ ] Implement native nested Avalonia MenuItem.ItemsSource, not click-to-open format dialog. Use same command from timeline/list; native hover delay and keyboard navigation, no bespoke hover timer. Keep parent menu open when pointer moves into child.
- [ ] Implement target preparation and dialogs, safe suggested filename including clip identifier, extension filters, target name/duration and secondary quality caveat. Add exact warning copy from spec; no new persistence. Resolve edits before capturing saved revision; if state changes during subsequent dialogs, abort with localized retry message.
- [ ] Add and pass tests for switch-project, delete-target, revision change, busy/recording/unknown state, duplicate child selection, unusual Unicode/path-like names and zh/en. No source paths in user-visible diagnostic leaks.
- [ ] Run new tests plus `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectWorkspace`; require pass.
- [ ] Stage listed files; commit `feat: expose GIF and cascading clip export menus`.

### Task 5: 整合回歸、文件與人工驗收

**Files**

- Create: tests/ScreenRecorder.Media.Tests/ProjectGifExportIntegrationTests.cs
- Modify: README.md, TESTING.md, MANUAL_TEST_CHECKLIST.md
- Create: docs/verification/2026-10-21-gif-and-clip-export.md

**Interfaces:** Consume Tasks 1–4 through actual IPC/UI request and exporter; no new product interface.

- [ ] Add end-to-end tests using synthetic distinct-color/video+tone sources, trimmed/compound/group clips, selected MP4 audio effects and silent GIF. Assert decoded first/last content, no out-of-range samples, loop/dimensions/timing, safe cancel and unchanged source hashes.
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectGifExportIntegrationTests`; investigate any failure before accepting.
- [ ] Run `dotnet restore ScreenRecorder.sln`, `dotnet build ScreenRecorder.sln -c Release --no-restore`, `dotnet test ScreenRecorder.sln -c Release --no-build`, then `git diff --check`; record exact results. Existing CI failure is not a GIF regression without evidence; do not hide or weaken it.
- [ ] Document actual results, architecture and user operation. Manual checklist includes prerequisites, steps, expected/failure/evidence for Win/mac menu hover and keyboard at high DPI, native GIF viewers, >5-minute warning/continue/cancel, longer memory/disk measurements and source safety. Mark unexecuted platform tests REQUIRES MANUAL VALIDATION, not PASS.
- [ ] Self-review spec coverage and full diff, stage only task files, commit `test: verify GIF and clip export workflows`. Report completed/tested/not tested/known issues. Stop for integration direction; no push, version increment, release or schedule restart.

## Plan self-review

Specification coverage: scope/effects/fingerprints Task 1–3; bounded encoding/timing/verifier Task 2–3; exact cascading-menu behavior and all bilingual prompts Task 4; cancellation/source safety Task 3/5; regression and honest platform evidence Task 5. User approved inline execution. Evidence and remaining native/manual acceptance limits are recorded in docs/verification/2026-10-21-gif-and-clip-export.md; approval does not imply those manual checks passed.
