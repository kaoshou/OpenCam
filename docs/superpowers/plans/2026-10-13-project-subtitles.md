# Project Subtitles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正名稱對話框與輸出箭頭，加入可隨專案保存、預覽及離線網頁切換的多語字幕。

**Architecture:** Core負責字幕軌、cue、語言辨識與時間查詢；Infrastructure保存唯一字幕資產與完整draft；Recorder提供已認證、受限的字幕操作與輸出。UI顯示匯入清單及與PlayheadTicks同步的純文字字幕overlay；網頁使用同一cue資料而不fetch外部字幕。

**Tech Stack:** C#/.NET8、Avalonia11、現有JSON/IPC/BoundDirectory、原生HTML/CSS/JS；不新增應用依賴。

**Spec:** `docs/superpowers/specs/2026-10-13-project-subtitles-design.md`

## Global Constraints

- 僅編輯器預覽與網頁顯示字幕；不燒錄MP4、不修改MKV、不影響錄影。
- 原字幕保存為專案內`subtitles/<GUID>.srt`或`.vtt`，不可覆寫同名資產。
- 每檔10MiB、最多32軌、每軌100000cues、每cue10000字元；UTF8含BOM/UTF16 BOM；無BOM非UTF8拒絕。
- 時間cue採[start,end)，字幕時間以最終影片為準；剪輯改時序後提示確認同步，不自動改寫。
- 字幕初始預設zh-TW，有字幕預設開啟；未包含zh-TW明確提示fallback，軌道以唯一ID識別。
- 首次保存字幕設定升project schema4；舊版拒絕，無字幕舊專案維持原schema。
- 網頁仍只輸出index.html/media.mp4/opencam-player.js；安全JSON/純文字DOM，不eval/fetch/innerHTML字幕。
- 不升版、不推送、不合併、不發布；保留main原dirty文件與已發布0.3.3。

## Review Focus

1. 同語言同檔名替換後，復原／救援不能失去旧字幕資產（Task3）。
2. 匯入超限、連結或來源途中更改，不能產生已引用的部分檔案（Task3）。
3. 重疊cue、seek回退與播放到cue末端，不能殘留字幕（Task2/4/5）。
4. 有字幕專案由舊版開啟／save不能靜默丟掉資料，draft回復也不能降schema（Task3）。
5. 瀏覽器storage拒絕、全螢幕控制列展开，字幕仍可操作與閱讀（Task5）。

## File Map

- UI修正：`ProjectNameDialog.axaml`、`ProjectWorkspaceList.cs`的片段改名視窗、`Editor/EditorToolContent.cs`、`Views/MainWindow.axaml`、`ProjectWorkspaceView.axaml`。
- 新Core：`Projects/ProjectSubtitles.cs`、`ProjectSubtitleParser.cs`、`ProjectSubtitleLanguage.cs`；擴充`RecordingProject.cs`、`ProjectEditDraft.cs`、`ProjectEditHistory.cs`、`ProjectValidation.cs`。
- 保存：`Infrastructure/Projects/ProjectSubtitleStore.cs`（新）、`JsonProjectStore.cs`、`ProjectDraftStore.cs`、`Core/Projects/IProjectStore.cs`。
- 操作／訊息：`Infrastructure/IPC/ProjectMessages.cs`、`Recorder/Services/ProjectSubtitlesCoordinator.cs`（新partial）、`ProjectIpcDispatcher.cs`、`ProjectEditPersistence.cs`。
- UI字幕：`UI/Projects/ProjectSubtitlesDialog.axaml/.cs`、`ProjectWorkspaceSubtitles.cs`（新）、`ProjectWorkspaceViewModel.cs`、`ProjectWorkspacePlayback.cs`、`ProjectWorkspaceView.axaml/.cs`及既有Localization文字檔。
- 網頁：`ProjectWebPlayerDocument.cs`、`ProjectWebPlayerExporter.cs`、`WebPlayer/player.html`。

### Task 1: 名稱按鈕與輸出箭頭

**Interfaces:** 新圖示名稱`chevron-down`供首頁／編輯器共用；名稱視窗既有事件／Command不變。

**Tests:** `tests/ScreenRecorder.Media.Tests/ProjectNameDialogTests.cs`、`ProjectWorkspaceViewTests.cs`及現有首頁layout測試。

- [ ] 寫失敗測試`NameDialogButtonsCenterTheirContent`：新增／改專案名稱／片段改名三流程，確認Cancel與Confirm的有效horizontal/vertical alignment均Center；箭頭測試檢查兩入口含`EditorToolContent.Icon == "chevron-down"`、不再含「⌄」。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~NameDialog|FullyQualifiedName~WorkspaceView|FullyQualifiedName~MainWindow'`；Expected：新對齊／箭頭斷言FAIL，不是編譯錯誤。
- [ ] 實作有效ContentPresenter對齊與16px path圖示；保留disabled/primary對比，窄視窗按鈕位置。
- [ ] 同命令Expected PASS；`git diff --check` Expected exit0。
- [ ] Commit `fix: center naming buttons and refine export chevron`。

### Task 2: 共用字幕資料與解析

**Files:** 上述新Core三檔；`tests/ScreenRecorder.Core.Tests/ProjectSubtitleTests.cs`（新）。

**Produces:** `ProjectSubtitleCue(long StartTicks,long EndTicks,string Text)`；`ProjectSubtitleTrack(Guid Id,string OriginalName,string Language,string RelativePath,long FileSize,string Sha256,string TimelineSignature)`；`ProjectSubtitleSettings(ImmutableArray<ProjectSubtitleTrack> Tracks,Guid? DefaultTrackId)`；`ProjectSubtitleParser.Parse(ReadOnlyMemory<byte> bytes,string extension) -> ImmutableArray<ProjectSubtitleCue>`；`ProjectSubtitleLanguage.Infer(string fileName) -> string?`；`ProjectSubtitleTimeline.ActiveText(IReadOnlyList<ProjectSubtitleCue> cues,long ticks) -> string`。

- [ ] 新測試固定SRT `00:00:01,000 --> 00:00:02,000`及VTT等價資料，Assert start10000000/end20000000、末端不顯示；重疊cue1–3與2–4秒在2.5秒顯示兩句，seek0清空。
- [ ] 測試UTF8/UTF16 BOM、無BOM非UTF8拒絕；壞時間／空資料／超限拒絕；`lesson.en.vtt`辨識en、`engineering.srt`不誤辨、`zh_tw`正規化zh-TW、未知由UI預設zh-TW；惡意標記只成純文字。
- [ ] Run `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter FullyQualifiedName~ProjectSubtitle`；Expected新行為FAIL。缺類型時先建立空介面讓測試能編譯，再跑RED，不把compile error當行為證據。
- [ ] 實作有界解析、BCP47驗證及穩定cue排序／活動查詢；VTT NOTE忽略，不執行樣式標記。
- [ ] 同命令Expected PASS；Core suite Expected0fail。
- [ ] Commit `feat: parse bounded multilingual project subtitles`。

### Task 3: 唯一資產、draft保存與管理視窗

**Consumes:** Task2型別與Parser；現有IProjectHandle獨占project、BoundDirectory及draft/save/recovery。

**Produces:** `IProjectHandle.ImportSubtitleAsync(FileStream source,string originalName,string language,CancellationToken ct) -> Task<ProjectSubtitleTrack>`；`OpenSubtitle(ProjectSubtitleTrack track) -> FileStream`受relative path/hash驗證。`RecordingProject.Subtitles`及`ProjectEditDraft.Subtitles`採ProjectSubtitleSettings，省略欄位的舊JSON預設empty。

**IPC:** 在既有認證與operation-id/revision條件下增加`ImportProjectSubtitle`及`EditProjectSubtitles`。Import控制訊息只傳使用者選取的本機路徑／檔名／語言，不把10MiB內容放入控制pipe；Recorder唯讀開啟regular file、拒絕link，綁定handle後解析與複製，回覆track/cues時沿用媒體資料通道的大小及認證限制，不能放寬控制pipe。大cue集合分批／限額傳送，任何不完整回覆不可套用。

**Tests:** `ProjectStoreTests.cs`、`tests/ScreenRecorder.Media.Tests/ProjectSubtitlesIntegrationTests.cs`、`ProjectSubtitlesDialogTests.cs`（新）。

- [ ] RED測試兩份不同內容同名檔建立不同relative paths、hash正確、源檔不變，Save/Reopen/刪除外部來源後仍可讀。RejectLink/Oversize/ChangedSource測試不產生manifest引用。
- [ ] RED測試字幕套用進draft不直接存project，Cancel不變、undo/redo及recovery完整，替換後undo可讀舊資產；首次save schema4／無字幕不升，unsupported schema拒絕。
- [ ] RED管理視窗測試select/drop共用同匯入路徑、語言fallback提示、同語言replace/keep/cancel、預設移除fallback、32軌限制及清單捲動。
- [ ] Run `dotnet test ScreenRecorder.sln -c Release --filter 'FullyQualifiedName~ProjectSubtitle|FullyQualifiedName~ProjectStore|FullyQualifiedName~ProjectDraft'`；Expected各新增行為FAIL。
- [ ] 實作unique CreateNew資產保存、全流程subtitle clone/validation/IPC/save，readonly源檔及owned-temp cleanup；不刪歷史引用。寫管理視窗與所有既有語言字串，「附加影片字幕」在Export左側。
- [ ] 同命令Expected PASS；追加`dotnet build ScreenRecorder.sln -c Release` Expected0errors。
- [ ] Commit `feat: preserve subtitle assets with project edit drafts`。

### Task 4: 編輯器字幕預覽與同步提示

**Consumes:** State/draft.Subtitles、Task2 cue資料、PlayheadTicks。**Produces:** `SelectedPreviewSubtitleTrackId`（view-only）、`PreviewSubtitleText`及view-only `SubtitleAppearance`，overlay共用一般／全螢幕。`ProjectSubtitleTimeline.Signature(RecordingProject project) -> string`只包括時序和內容來源，不包括改名或音量。

**Tests:** `ProjectSubtitlePreviewTests.cs`（新）與既有fullscreen/preview測試。

- [ ] RED測試播放／pause／seek／跨片段／停止／切專案顯示與清除活動cue；選語言不改專案default、不送停止／重播影音命令。
- [ ] RED測試字級75/100/125/150/200%、四種配色、背景透明度、上中下及reset，解析度換720p/原始不改閱讀尺度，全螢幕overlay仍顯示及控制可達。
- [ ] RED測試時序改變標示stale，改名／音量不stale；輸出網頁可確認同步或略過字幕／取消，單獨MP4不受影響。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~SubtitlePreview|FullyQualifiedName~Fullscreen|FullyQualifiedName~WorkspacePlayback'`；Expected新行為FAIL。
- [ ] 實作ProjectWorkspaceSubtitles及overlay，接PlayheadTicks property變更；依可見畫面bounds縮放與避讓，無新的時鐘或影片重編碼。
- [ ] 同命令Expected PASS；Commit `feat: preview selectable subtitles in the editor`。

### Task 5: 網頁字幕控制與整合收尾

**Consumes:** ProjectSubtitles、相同Parser產出的cues及defaultTrackId。**Produces:** `WebPlayerData.Subtitles`可選欄位包含tracks(id/language/label/cues)、defaultTrackId，既有format/schema1不變；網頁CC/語言/樣式控制。不含原檔讀取、eval或HTML字幕插入。

**Tests:** `WebPlayerExportTests.cs`、`tests/web-player/player.test.mjs`。

- [ ] RED測試輸出三檔包含正確同源cue/default、JSON安全escaping、缺失／改動subtitle資產fail且不發佈index；無字幕包與MP4不变。
- [ ] Node RED測試語言/關閉/default/fallback/同語言兩軌、seek末端／paused／ended清除、重疊cue、字幕外觀立即生效；storage拒絕與全螢幕／觸控選單仍可用，cue文字不能執行DOM腳本。
- [ ] Run `node --test tests/web-player/player.test.mjs`及`dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~WebPlayerExport|FullyQualifiedName~Subtitle'`；Expected新增行為FAIL。
- [ ] 實作安全可選資料及字幕overlay/選單，shared純文字時間資料；如瀏覽器支援，用TextTrack補助原生字幕，視覺overlay只顯示一次；隱藏控制列不隱藏字幕。
- [ ] 同命令Expected PASS；以既有無字幕fixture確認功能不退化。
- [ ] 更新WEB_PLAYER_EXPORT及中英文guide，寫verification與實機checklist；記錄schema4、UTF8要求、時間軸警告、原檔與MP4未改。
- [ ] 最終Run `dotnet test ScreenRecorder.sln -c Release`、`node --test tests/web-player/player.test.mjs`、`npm test --prefix website`、`node scripts/check-version-consistency.mjs`、`git diff --check`；Expected0fail。平台/GUI略過只如實報告，不當PASS。
- [ ] Commit `feat: export offline multilingual subtitle controls`；一次全分支審查後修重要問題，交付可本機測試版本；不自動發布。

## Self-review

五項review focus分別落在Task2/3/4/5測試，沒有省略資產及draft/recovery整合。Task3使用Task2同型別，Task4/5共用同cue語意；控制pipe維持限制，匯入／cue資料走受限媒體通道。字幕與影音fingerprint分離，避免不必要轉檔。只執行本計畫範圍，書面計畫核對後開始。
