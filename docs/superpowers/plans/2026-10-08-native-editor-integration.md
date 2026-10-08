# Native Recording Editor Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Preserve inline execution; do not restore the abandoned editing-preview branch.

**Goal:** 將使用者已確認的三欄編輯工作區接到真實錄影專案，而不是再交付一個測試清單或另一套介面模型。

**Architecture:** Recorder 保持專案的唯一寫入者；Core 負責不可變剪輯命令與時間映射；UI 只提交帶 revision 的操作並呈現已確認結果。預覽、快取與輸出共用來源時間映射，但不能取得任意檔案路徑或改寫 sources。先完成媒體選型的可執行證據，再制定所選後端的具體接線計畫，不能把未驗證播放器直接搬進產品。

**Tech Stack:** 現有 .NET 8、Avalonia 11.2.5、xUnit、FFmpeg/ffprobe、已認證 IPC、既有安全專案儲存層。此計畫不預先增加產品播放器依賴。

**Spec:** `docs/superpowers/specs/2026-10-08-recording-project-editor-design.md`；視覺依據為使用者確認的 `prototypes/recording-editor/` 模型（`codex/recording-editor-model`）及本次附圖。

## Global Constraints

- 原版快速錄影、MKV 救援、平台擷取與音訊行為不因專案編輯改變。
- 不恢復、不合併 `codex/editing-preview`；不推送、不合併、不改版本、不發布。
- 同一 OpenCam 主程式入口；首頁保留快速錄影，新增新建／開啟／最近專案。移除「開發預覽」產品標題，不移除真實失敗或未完成狀態提示。
- 1440×900 主版型：左側約 220、右側約 260、時間軸約 240 邏輯像素；1280×720 預設收右側；最小 1024×640，面板可切換。
- 主要文字至少 14、輔助至少 12；按鈕至少 32、主要操作至少 36、裁切邊緣命中至少 10 邏輯像素。中英文、深淺色與高 DPI 均驗收。
- 真實預覽、真實縮圖、真實附屬音訊波形；不得以模型投影片、隨機波形、計時器或假輸出取代功能。
- 所有剪輯非破壞性；一次拖曳一次復原；來源不因刪除、取消、輸出失敗而消失；只存專案不強制輸出 MP4。
- 半開來源 PTS 區間；不以 nominal FPS 推測 VFR 畫格。播放頭與片段選取獨立。
- 快取初始上限 2 GiB；兩小時／200 片段長專案，已索引本機素材 warm seek p95 ≤250ms 是量測目標，不是已完成承諾。
- Phase B 現況僅部分實機驗收；Mac 音訊、Windows、異常情境仍是後續整合／發布阻擋項，不把現有靜音錄影通過當作雙平台通過。

## Review Focus

1. 非零／負 PTS、VFR、接點恰好落於 out：Task 2 驗證映射不越界、不產生零長片段。
2. 快速 seek 後立即續錄／關閉：Task 3 驗證過期画面不發布、預覽停止未確認時不得啟動錄音。
3. 保存失敗或 IPC 回覆丟失後重試剪輯：Task 2 驗證 operationId 冪等、revision 衝突與復原歷史不重複。
4. 長中英文名稱、200 片段、1024×640 高 DPI：Task 4 驗證主要控制可見、面板可收合、虛擬化不遺失選取。
5. 惡意專案引用、移動中來源替換、快取磁碟不足：Task 3 驗證綁定來源的存取與有限資源；不退回未驗證路徑。

## 工作拆分與完成定義

本計畫是第一個可獨立驗證的「原生編輯整合基礎」批次，**不是完整編輯器完成宣告**。Task 1–4 完成後應有正式入口、可保存／復原的剪輯核心、經量測的播放器選型，以及與原稿一致的原生版面測試。只有真實播放器接線與完整故事驗收完成，才展示為可用編輯器。

後續依同一規格接續兩份技術計畫，不重新請使用者選版型：

- **真實預覽與成品閉環：** 選定後端實作、時間軸縮圖／波形與拖曳、補錄交易、跨片段播放、MP4 輸出及取消。必須通過 A / D / C / B（刪中段）/ E 的原始故事。
- **屬性與平台完成：** 音量／靜音／淡入淡出、真正矩形裁切／縮放／位置、來源重新連結、長影片與雙平台實機驗收。現有單一 `Crop` 數值不足，需具備備份與相容規則的 schema 升級，不假裝已有矩形裁切。

這個拆分是為避免未選定媒體引擎就虛構其實作接口；不是縮減已同意功能。未接線控制不出現在供使用者驗收的工作區；不以只有版面的視窗代替完成品。

## Task 1: 回復產品入口與對齊現有驗收基線

**Files:** Modify `src/ScreenRecorder.Core/Localization/LocalizationService.cs`、`src/ScreenRecorder.UI/Views/MainWindow.axaml`、`tests/ScreenRecorder.Media.Tests/ProjectMainEntryTests.cs`、`docs/acceptance/2026-10-08-project-phase-b.md`。

**Interfaces:** Consumes existing `IProjectClient.SendAsync(string command, ProjectRequest request, CancellationToken ct)` and existing main-window project handlers. Produces unchanged command contract; localized `ProjectWorkspace` = `錄影專案` / `Recording project`.

- [ ] RED：在 `ProjectMainEntryTests` 加入 `ProductEntryUsesNormalProjectTitle`：兩語言 `ProjectWorkspace` 分別精確等於上述文字；原快速錄影入口與專案開啟 handler 仍存在。不得用單純「沒有 preview 字串」當唯一測試。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectMainEntryTests`；Expected：新增標題斷言 FAIL。
- [ ] 更新中英文產品名稱與首頁說明；不把首頁替換成獨立 Demo，不擴大調整錄影控制。保留已有未提交的 inspector overflow 修正並記錄其來源。
- [ ] 同一命令 PASS；另跑 `dotnet test ScreenRecorder.sln -c Release`，記錄通過／跳過數，不把跳過列為成功。
- [ ] Scoped commit：只加入本 task 的入口、在地化及測試／驗收檔；訊息 `fix: restore normal recording project entry copy`。

## Task 2: PTS 時間映射與可復原剪輯交易

**Files:** Create `src/ScreenRecorder.Core/Projects/ProjectTimeline.cs`, `ProjectClipEdit.cs`; modify `ProjectEditHistory.cs`, `ProjectValidation.cs`, `src/ScreenRecorder.Recorder/Services/ProjectRecordingCoordinator.cs`, `ProjectIpcDispatcher.cs`, `src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs`; create `tests/ScreenRecorder.Core.Tests/ProjectTimelineTests.cs`, `ProjectClipEditTests.cs`; extend `tests/ScreenRecorder.Media.Tests/ProjectIpcTests.cs`.

**Interfaces:**

- `ProjectTimeline.Build(RecordingProject project) -> ProjectTimeline`; `DurationTicks: long`; `Locate(long timelineTicks) -> ProjectPosition?`.
- `ProjectPosition(Guid ClipId, Guid SourceId, long SourcePts)`；時間軸 ticks 為 TimeSpan ticks，來源 PTS 使用來源 TimeBase；checked 整數有理數計算，邊界只在明確輸入轉換時取整。
- `ProjectClipEdit` 封閉命令階層：`Trim(Guid ClipId,long InPts,long OutPts)`、`Split(Guid ClipId,long AtPts,Guid RightClipId)`、`Remove(Guid ClipId)`、`Move(Guid ClipId,Guid? BeforeClipId)`、`Group(Guid FirstClipId,Guid LastClipId,Guid GroupId)`、`Ungroup(Guid GroupId)`、`RemoveRange(long StartTicks,long EndTicks)`。
- `ProjectEditHistory.Apply(ProjectClipEdit edit) -> void`；沿用 `Undo()` / `Redo()` 與錄影 append 保護。
- `ProjectRecordingCoordinator.ApplyEditAsync(ProjectClipEdit edit,long expectedRevision,Guid operationId,CancellationToken ct = default) -> Task<ProjectCommandResult>`；IPC 使用明確 discriminator 白名單，禁止任意 CLR 型別反序列化。

- [ ] RED：用來源 timebase 1/1000、起點 5000、A=[5000,7000)、B=[8000,11000)；assert 總長 5 秒、時間軸第 2 秒映射 B/8000、末端 Locate 回 null。另測負 PTS、overflow、VFR 索引格點與零長拒絕。
- [ ] RED：A/B/C 刪 B 中段後只改引用、來源 hash／數量不變；一次 Undo 還原原序列、Redo 回剪輯結果；移動群組不可拆散；no-op 不增 revision；全刪仍保留 Sources 且可 Undo。
- [ ] RED：同一 operationId 同 payload 重送只改一次；同 id 不同 payload 拒絕；stale revision 拒絕；保存失敗不得回覆已保存；續錄 append 後 Undo 舊剪輯仍保留新錄製片段。
- [ ] Run `dotnet test ScreenRecorder.sln -c Release --filter 'FullyQualifiedName~ProjectTimelineTests|FullyQualifiedName~ProjectClipEditTests|FullyQualifiedName~ProjectIpcTests'`；Expected：新契約未實作造成 FAIL。
- [ ] 實作上述契約；range deletion 在交易內產生片段 ID、一次提交；群組只代表相鄰片段共同移動。禁止剪輯正在寫入的素材；沿用單寫者、保存與 IPC payload 限制。
- [ ] 同一命令 PASS；補上丟回覆／保存失敗注入測試，不僅 happy path。
- [ ] Scoped commit：上述 Core、IPC、coordinator 及測試檔；`feat: add transactional nondestructive clip edits`。

## Task 3: 預覽後端實測選型與錄影隔離契約

**Files:** Create `tests/ScreenRecorder.ProjectPreviewProbe/ScreenRecorder.ProjectPreviewProbe.csproj`, `Program.cs`, `PreviewProbeResult.cs`; create `docs/adr/recording-project-preview-engine.md`, `docs/acceptance/2026-10-08-preview-engine.md`. Candidate code stays in this test project, not the released app.

**Interfaces:** Probe-only `IPreviewCandidate : IAsyncDisposable` with `OpenAsync(Stream source,CancellationToken ct)`, `SeekAsync(long sourcePts,ProjectRational timeBase,CancellationToken ct) -> Task<PreviewProbeFrame>`, `PlayAsync(CancellationToken ct)`, `StopAsync(CancellationToken ct)`; `PreviewProbeFrame(long RequestedPts,long ActualPts,string FrameHash)`; input stream is read-only, seekable and owned by probe.

- [ ] RED：建立帶 frame-number／flash-click 音畫標記、非零 PTS、30／60 FPS、VFR、無聲、長 GOP 的固定 fixture；probe 在尚無後端時輸出 NOT_IMPLEMENTED 且 exit nonzero，不能空報告通過。
- [ ] Run `dotnet run --project tests/ScreenRecorder.ProjectPreviewProbe -c Release -- --candidate none --self-test`；Expected：nonzero、NOT_IMPLEMENTED。
- [ ] 比較 LibVLCSharp 的受控 StreamMediaInput 與 FFmpeg 解碼＋明確 PCM 輸出／A-V clock 方案；評估 native 平台方案的 MKV 需求。逐一記錄精準 PTS、連續播放、停止釋放、取消、Windows/macOS native assets、散布依賴；不把舊 LibVLC 試作作為通過證据。新增候選依賴僅限 probe，版本及 native package digest 記錄 ADR。
- [ ] 實測 latest-seek-wins（連續 100 次）、播放中 Stop、取消讀取、壞檔／decoder crash；Stop 未確認不得給出可續錄結果。測來源 stream 存取被拒、替換路徑、限額／磁碟不足，不退回任意檔案路径；日誌與記憶體有上限。
- [ ] GREEN：每個 candidate 產生 JSON 報告與 exit code；已通過項為 PASS，缺音訊裝置／Windows runner 為 BLOCKED，不可判定全平台成功。量測實際 A/V 偏差與 seek 誤差，不以播放器的回報 position 當 frame 證據。
- [ ] 在實際硬體執行兩小時／200 片段 cold/warm benchmark，公開環境與 p95。無硬體時保留 BLOCKED；不得用 20 秒 fixture 宣稱達標。
- [ ] ADR 選型僅在正確性／資源停止門檻有證據後成立；失敗則列原因並停在此門檻。通過後寫具體播放器／快取／輸出接線計畫供審閱，才加產品依賴。
- [ ] Scoped commit：probe 與 ADR／量測文件；`test: establish project preview engine acceptance gates`。

官方參考：LibVLC `Stop` 會等待播放器线程，不能在 UI thread 假設立即返回（[MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)）；FFmpeg 精準 seek 與 stream copy 行為不同，精剪不能保證任意位置直接 copy（[FFmpeg documentation](https://ffmpeg.org/ffmpeg.html)）。這些是候選限制，不是選型結果。

## Task 4: 原生版面與互動契約，不另做 Demo App

**Files:** Modify `src/ScreenRecorder.UI/Projects/ProjectWorkspaceView.axaml`, `.axaml.cs`, `ProjectWorkspaceViewModel.cs`; create `src/ScreenRecorder.UI/Projects/Editor/EditorLayout.cs`, `EditorSelection.cs`, `TimelineInteraction.cs`; modify `tests/ScreenRecorder.Media.Tests/ProjectWorkspaceLayoutTests.cs`; create `tests/ScreenRecorder.Media.Tests/ProjectEditorInteractionTests.cs`.

**Interfaces:** `EditorLayout.ForSize(double width,double height) -> EditorLayout` with LeftWidth/RightWidth/TimelineHeight and LeftVisible/RightVisible; `EditorSelection(Guid? ClipId,long PlayheadTicks,long? RangeStartTicks,long? RangeEndTicks)`; `TimelineInteraction` maintains transient drag state and emits at most one `ProjectClipEdit` on completion, none on cancel. Committed state remains Recorder-owned.

- [ ] RED：1440×900 顯示左清單／中央預覽區／右屬性／下時間軸；1280×720 右側預設收合；1024×640 保留播放與主要動作且面板可切換。測真正 client bounds，不僅設定 Window.Width。
- [ ] RED：zh-TW/en-US × light/dark × 100%/200% DPI 的長專案名／片段名，不越界、不蓋住操作；200 片段虛擬化後選取保持。點片段不移播放頭、播放到下一片段不改屬性選取。
- [ ] RED：拖曳 100 個 move event 後 release 只送一次剪輯命令；Esc 不送命令；拖邊緣是 trim、拖本體是 move；文字輸入內 Space/Delete 不觸發全域剪輯。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~ProjectWorkspaceLayoutTests|FullyQualifiedName~ProjectEditorInteractionTests'`；Expected：新版 layout／interaction 斷言 FAIL。
- [ ] 在既有原生工作區重整 Grid 和可收合 pane，沿用核准模型的白／炭灰中性色、藍色選取、簡潔文字工具列，不照抄模型 banner 或展示素材。原始快速錄影畫面保留。無素材顯示空專案指引，載入中與錯誤明確區分。
- [ ] 同一命令 PASS；新增預覽實作尚未接線時，不啟用播放／輸出假按鈕，不把本 task 的 headless layout 截圖當成功產品交付。
- [ ] Scoped commit：UI editor 與相應測試；`feat: align native editor workspace with approved layout`。

## 接續閉環必測項（下一份媒體接線計畫不得省略）

- 首次有效素材初始化 canvas/FPS；不同尺寸續錄採等比例 fit，不拉伸。
- 時間軸縮圖／波形依來源 hash、PTS、算法版本建立 bounded cache；媒體工作者失敗不阻擋保存。
- 補錄插入在完成素材後才原子分割／插入，一次 Undo；失敗無新剪輯。一般續錄永遠追加尾端。
- 預覽停止及專案保存兩項皆確認後，才啟動擷取；閉鎖待保存／失敗狀態有可恢復操作。
- MP4 固定 revision 快照，精剪重編碼，驗證後才發布；取消只清自己的唯一 temp，保留來源／既有成品。
- 真實 A/B/C 錄製→剪 B→搬 C→插 D→保存但不輸出→重開→追加 E→預覽與 MP4 同序；來源 hash 不變。
- 可供使用者驗收前，逐區與核准附圖比對並拍攝真正原生 App；不是瀏覽器模型，沒有「開發預覽」主標題，也不宣稱未測的音畫同步已完成。

## Self-review / execution handoff

- 規格 coverage：本批處理入口、時間模型／復原、選型證據、原生構圖；播放器實作、插錄、輸出、屬性及平台驗收明確列為接續工作，整體功能尚未完成。
- Review Focus 五項均有對應 task 斷言或實測門檻；主程式與已有 B 未提交修正不任意覆蓋。
- 介面一致：Core 剪輯使用 source PTS，UI／timeline 使用 ticks，唯一轉換由 ProjectTimeline 負責；probe 的實際 frame PTS 不以名義 FPS 推算。
- 請先審閱此施工順序；延續既定 inline 實作方式。設計版型已核准，不重開版型選擇。依 writing-plans 技能，新的實作計畫需審閱後才開始改產品程式。
