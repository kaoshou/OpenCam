# Editor Media Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Preserve the user's chosen inline/native execution method. Steps use checkbox (`- [ ]`) syntax.

**Goal:** 在同一 OpenCam 原生程式完成影音預覽、真實縮圖／波形、指定位置補錄、精剪 MP4 與直接錄影入口，不以模型、假播放器或只保存資料代替完整流程。

**Architecture:** Recorder 保持專案及錄製的唯一擁有者。共用來源 PTS／剪輯映射供預覽與輸出使用；媒體作業讀取綁定的唯讀來源，播放器、快取及輸出均可取消。媒體後端仍須通過 Task 1 的整合證據，不能因本計畫存在就把候選標為已選定。

**Tech Stack:** .NET 8、Avalonia 11.2.5、既有 FFmpeg／ffprobe、認證 IPC、平台音訊輸出；不把失敗的 LibVLC 候選加入產品。

**Spec:** `docs/superpowers/specs/2026-10-08-recording-project-editor-design.md`，入口／停止自動 MP4 依修訂 `docs/superpowers/specs/2026-10-09-record-first-edit-after-design.md`。

**Status:** 使用者已於 2026-10-09 確認繼續，正在本機實作。不是已完成的功能或後端選型結果；進度與驗證記錄於同名 SDD ledger。

## Global Constraints

- 沿用 `codex/recording-project-persistence`；本機工作，不推送、不合併、不改 VERSION 0.2.5、不發布。不動既有 Phase B 驗收文件未提交修改。
- 保留核准的三欄原生版面：左片段、中央影音、右屬性、下方時間軸。不建立另一個 Demo App，不加入「開發預覽」首頁標題。
- 從錄影主入口直接開始；不用先建立專案。停止自動輸出 MP4；停止後的編輯只保存，手動輸出才另產生成品。
- 原始素材永久保留，刪除時間軸內容不刪來源；不偷偷修改使用者的舊清理偏好。
- 來源與輸出均使用已綁定的檔案／目錄，不能從 UI 或專案字串拼出任意媒體路徑交給 FFmpeg。所有子程序有資源上限、取消、退出確認及有限日誌。
- source PTS 使用有理數運算，不以 FPS × 秒猜測原始畫格；整個成品只量化一次到專案輸出時間格，不能逐段累積捨入誤差。
- 暫停編輯、繼續、補錄前均先完成預覽停止確認及保存；停止未確認不得擷取。
- 使用真實解碼的影像／PCM，不用假波形；不把原生 buffer 計數當聲學回錄，不把短測資當兩小時效能驗收。
- 本機 Mac 可進行完整本機驗證；Windows 硬體無法取得時記錄未驗證，不新增遠端 Actions、不宣稱雙平台驗收完成。

## Review Focus

1. 有聲／無聲／晚起始音訊交替：畫面與聲音使用共同時間原點，不能各自減去不同 STARTPTS 後產生位移。Task 1/3/6 使用帶已知音畫標記的測資。
2. 來源或輸出父目錄在媒體工作中改名／被替換：仍只能讀原綁定來源並發布至原綁定目錄。Task 2/6 注入替換競態，核對內容與來源 hash。
3. 補錄完成、來源提交、時間軸插入、journal 完成之間 crash：重開只插入一次，不能先切開舊片段卻遺失新素材。Task 5 逐個 crash point 重播。
4. 預覽 Stop、最新 seek、續錄及 UI 關閉交錯：舊畫面不可覆蓋新 revision，聲音未停不可開始錄影。Task 3/7 用延遲／失敗音訊 sink 加真實裝置測試。
5. AAC 尾端填補及跨片段 FPS 捨入：MP4 時長／接點順序正確，輸出不包含被刪區間。Task 6 以獨立解碼內容及樣本位置核對，不只看 FFmpeg exit 0。

## Task 1: 完成 FFmpeg＋平台 PCM 的整合候選驗證

**Files:** Extend `tests/ScreenRecorder.ProjectPreviewProbe/Program.cs`, `FfmpegPipeCandidate.cs`, `NativeAudioProbe.swift`; create `IntegratedPlaybackProbe.cs`; update `docs/adr/recording-project-preview-engine.md`。

**Interfaces:** 診斷候選提供 `OpenAsync(Stream, CancellationToken)`, `SeekAsync(long sourcePts, ProjectRational, CancellationToken)`, `PlayAsync(CancellationToken)`, `StopAsync(CancellationToken)`；不得以仍丟 NotSupported 的 Play 通過門檻。

- [ ] RED：生成含畫格序號、闪光與短音訊標記的兩段 MKV，測 `ContinuousPlaybackCrossesClipBoundary`, `LateAudioRetainsOffset`, `StopJoinsAudioAndVideo`, `LatestSeekSuppressesOldPublication`。目前整合 Play 缺失應明確 FAIL。
- [ ] 在隔離探針中以 FFmpeg 連續解碼影像和 PCM，以音訊裝置 sample clock 排程畫面；無音訊時用 monotonic clock。視訊排程必須使用解碼時間戳，不用 UI timer 當音訊時鐘。
- [ ] 測 30/60 FPS、VFR、非零／負 PTS、無聲、晚起始音訊、跨片段音畫標記；Stop 後觀察至少 250ms 無新音訊 render／視訊發布，播放重啟後再次測停止。注入 hung helper，外層程序 watchdog 必須產生失敗結果而非永久卡住。
- [ ] macOS 真實輸出裝置通過後，記錄解碼 PTS、裝置 clock、音畫標記差值、停止延遲及來源 hash；音畫標記差異門檻 40ms。這是新增測試門檻，不宣稱既有測量已達到。
- [ ] Run: `dotnet build tests/ScreenRecorder.ProjectPreviewProbe -c Release`；執行新整合候選及既有 `ffmpeg-fd-macos --extended`。Expected：每項輸出 PASS/FAIL/NOT_TESTED，完整候選未通過不得進入 Task 3 產品播放器接線。
- [ ] 更新 ADR 為可核對的決策；Windows 未測保持未驗證。若共同時鐘／停止門檻不過，報告具體失敗，不回退為失敗的 LibVLC 精準定位。Commit：`test: verify integrated preview clock and stop lifecycle`。

## Task 2: 共用媒體計畫與綁定來源執行器

**Files:** Create `src/ScreenRecorder.Media/Projects/ProjectRenderPlan.cs`, `ProjectMediaProcess.cs`; platform-specific process code under `src/ScreenRecorder.Platform.macOS/` and `src/ScreenRecorder.Platform.Windows/`; tests `tests/ScreenRecorder.Media.Tests/ProjectRenderPlanTests.cs`, `ProjectMediaProcessTests.cs`。

**Interfaces:** `ProjectRenderPlan.Create(RecordingProject snapshot) -> ProjectRenderPlan`；plan 包含不可變 clip/source identity、來源 PTS 入出點、精確累積時間、輸出畫布與音畫屬性。`IProjectMediaProcess.RunAsync(ProjectMediaJob job, IReadOnlyList<FileStream> boundInputs, FileStream? boundOutput, CancellationToken ct) -> Task<ProjectMediaResult>`；job 是內部受驗證操作，不接受 UI 任意參數。

- [ ] RED：`NonzeroPtsAndVfrKeepExactIntervals`、`FractionalBoundariesDoNotAccumulate`、`SilentClipCreatesTimedSilence`、`LateAudioIsNotShiftedEarly`；assert 手算時間區間與獨立解碼內容。新介面缺失應 FAIL。
- [ ] 建立單一來源到專案時間的計畫，預覽與輸出都消費它；音訊依影片來源原點換算，不對兩種 stream 各自任意歸零。不同解析度等比例 fit／補底，不拉伸。
- [ ] 以已開啟 handle 傳遞輸入及暫存輸出；macOS 使用明確 FD 繼承，Windows 使用受控 handle 繼承並驗證當版 FFmpeg 支援。協議只開所需本機 FD／pipe，不接受網路協議或任意來源 URL。
- [ ] RED→GREEN：`RenamedSourceStillUsesOriginalHandle`, `ReplacedAncestorCannotRedirectOutput`, `CanceledChildIsReaped`, `MalformedOutputHitsQuota`。圖像單幀上限 64MiB、stderr 保留上限 1MiB；seek 15 秒超時，長輸出不套用 seek 超時，而以進度停滯 30 秒及使用者取消控制。
- [ ] Run: `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~ProjectRenderPlan|FullyQualifiedName~ProjectMediaProcess'`；Expected 全 PASS；完整 solution 後 commit `feat: add bound project media execution and render plans`。

## Task 3: 產品影音預覽與停止隔離

**Precondition:** Task 1 macOS 整合候選門檻通過及 ADR 記錄，Task 2 綁定來源檢查通過。不得直接把 probe 程式搬入產品而略過測試。

**Files:** Create `src/ScreenRecorder.Recorder/Services/ProjectPreviewCoordinator.cs`; media `Projects/ProjectPreviewDecoder.cs`; Core `Projects/ProjectPreviewState.cs`; platform `ProjectAudioOutput` implementations；modify `ProjectRecordingCoordinator.cs`, `ProjectIpcDispatcher.cs`, UI `ProjectWorkspaceViewModel.cs`, `.axaml`, `.axaml.cs`。

**Interfaces:** `IProjectAudioOutput.StartAsync(format, ct)`, `WriteAsync(pcm, ct)`, `PositionSamples`, `StopAsync(ct)`；`ProjectPreviewCoordinator.SeekAsync(projectId, revision, ticks, ct)`, `PlayAsync(...)`, `StopAsync(ct)`；所有發布資料附 projectId/revision/generation。Coordinator 在 Recorder 中持有解碼與音訊 sink，不能只信任 UI 說「聲音已停」。

- [ ] RED：`SeekPublishesOnlyNewestGeneration`, `PlaybackDoesNotChangeInspectorSelection`, `PreviewStopsBeforeCapture`, `StopFailureBlocksCapture`, `ClosingUiStopsAudio`。受控延遲測試只證明狀態；另有真實裝置測試，不能互相取代。
- [ ] 視訊限量排隊，過期畫格可丟；音訊維持有界 buffer，不丟樣本來追 UI。seek 取消並 join 舊世代，播放／暫停保留真實位置；逐格使用來源畫格索引。音訊 sink 停止並排空／丟棄舊 buffer 完成後才確認 Stop。
- [ ] 控制命令沿用認證 IPC；媒體幀使用獨立認證、有限 frame 大小的資料串流，不提高既有命令上限，也不在 dispatcher 長鎖內等待整段解碼。UI 斷線終止 preview lease。
- [ ] 中央接真正 Avalonia 影像、播放／暫停／逐格／時間碼；定位待完成時維持舊圖並標示準備中，不顯示已被刪除片段當成功新畫面。選取片段與播放頭維持獨立。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~ProjectPreview`，再執行 macOS 真實播放、暫停、seek、停止後續錄故事；Expected 狀態及實際輸出皆通過。Commit `feat: integrate project preview with recording stop guard`。

## Task 4: 真實縮圖／波形、有界快取與屬性一致性

**Files:** Create media `Projects/ProjectMediaCache.cs`, `ProjectWaveformBuilder.cs`; UI `Projects/Editor/ProjectMediaPresenter.cs`; modify `ProjectTimelineControl.cs`, `ProjectWorkspaceView.axaml`；tests `ProjectMediaCacheTests.cs`, `ProjectWaveformTests.cs`, `ProjectPropertiesTests.cs`。

**Interfaces:** `GetThumbnailAsync(sourceId, sourcePts, size, ct) -> ThumbnailResult`；`GetWaveformAsync(sourceId, startPts, endPts, bucketCount, ct) -> WaveformResult`；key 包含來源 SHA256／PTS 區間／尺寸或桶數／算法版本。回傳資料附同一 revision/generation。

- [ ] RED：已知音訊脈衝的波形峰值落在手算區間；無聲來源有明確無聲狀態。裁短後縮圖／波形只顯示保留區間，重排不重新掃描全部來源。
- [ ] 背景批次產生，當前定位優先；磁碟 cache 預設上限 2GiB、LRU 僅刪自己非使用中的 cache。取消／缺磁碟只影響可重建 cache，不刪 sources／專案／exports。測快取滿、損壞、替換路徑及 stale generation。
- [ ] 接左清單缩圖、長度，以及時間軸縮圖與附屬音訊波形；不把錄音時即時音量條當整段波形。拖邊界提供真實邊界預覽；命中區至少 10 邏輯像素。
- [ ] 加入音量／靜音／淡入淡出、畫面裁切／縮放／位置的非破壞交易與 Undo。現有 Crop scalar 若不足以表達核准的矩形，先做 schema 1→2 有備份遷移；舊資料轉為等效矩形，不悄悄換意義。預覽及輸出共用同一属性計畫。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter 'FullyQualifiedName~ProjectMediaCache|FullyQualifiedName~ProjectWaveform|FullyQualifiedName~ProjectProperties'`；Expected 全 PASS，實際解碼的波形／圖片人工核對後 commit `feat: show source-derived thumbnails waveform and clip properties`。

## Task 5: 指定位置補錄的原子保存與一次復原

**Files:** Create Core `Projects/ProjectInsertion.cs`, Infrastructure `Projects/ProjectInsertionJournal.cs`; modify `ProjectEditHistory.cs`, `ProjectSegmentCommitter.cs`, `ProjectRecordingCoordinator.cs`, IPC project messages/dispatcher、UI workspace；tests `ProjectInsertionTests.cs`, `ProjectInsertionRecoveryTests.cs`。

**Interfaces:** `StartInsertionAsync(config, expectedRevision, timelineTicks, operationId, ct) -> ProjectCommandResult`。持久 intent 保存 projectId、operationId、baseline revision、錨點 clipId/sourcePts、sessionId；錄製完成前不變更原時間軸。

- [ ] RED：A/B 中間補 C 後為 A-left/C/A-right/B，來源完整；一次 Undo 回到 A/B，Redo 恢復。開始失敗／零有效片段不分割。一般 Resume 不受播放頭影響，始終追加。
- [ ] 新素材確認並記錄來源後，於單次專案保存中套用分割與插入；來源新增不等同先追加時間軸再偷偷移動。插入群組內時維持群組連續，不能產生被其他群組打斷的成員。
- [ ] durable receipt 與時間軸同次保存，journal 清理失敗／重開可辨識已套用，不能重複插入。恢復前核對 baseline／錨點；衝突時保留新素材並提示，不猜插入位置。
- [ ] RED→GREEN：對來源落盤前後、intent 前後、時間軸保存前後、journal 完成前後逐點模擬 crash，核對來源 hash、插入次数及不確定狀態。丟 IPC 回覆重試同 operationId 只啟動一次擷取。
- [ ] UI「在此補錄」先顯示插入接點／將分開的片段並確認；呼叫 Task 3 Stop gate 及保存，再開始。正在錄製鎖定剪輯；安全暫停／停止後才展示插入結果。
- [ ] Run `dotnet test ScreenRecorder.sln -c Release --filter FullyQualifiedName~ProjectInsertion`；Expected 全 PASS，實機插入與重開核對後 commit `feat: commit recording insertions atomically with undo`。

## Task 6: 真正精剪 MP4 與可取消輸出

**Files:** Create `src/ScreenRecorder.Recorder/Services/ProjectFfmpegExporter.cs`; media `Projects/ProjectOutputVerifier.cs`; modify Infrastructure `Session/BoundDirectory.cs`、Recorder `Program.cs` 與既有 export job、UI 輸出進度；tests `RecordingContentExportIntegrationTests.cs`。

**Interfaces:** 實作現有 `IRecordingContentExporter.ExportAsync(IProjectHandle owner, RecordingProject snapshot, string outputDirectory, Guid exportId, IProgress<double> progress, CancellationToken ct)`；消費 Task 2 render plan。Verifier 消費已綁定成品 stream 與預期時間／音畫資訊，不信任檔名或 exit code。

- [ ] RED：真實 A/B/C 媒體→刪 B 中段→C/A/B 重排，輸出獨立解碼核對畫格序號、音訊標記及時長；每個來源 SHA256 完全相同。不可用 controlled exporter 代替這些測試。
- [ ] 完整且相容未調整片段保留 stream-copy 路徑；有任意精剪、音畫屬性、不相容格式則重編碼 H.264/AAC，按專案畫布／FPS 合成。處理音訊空白、晚起始及尾端填補，累積時間以整段精確映射量化。
- [ ] 長專案不一次開啟全部來源／建立萬段 filtergraph。精剪採受限批次正規化中間片段，固定畫布／編碼參數、無損音訊中間格式，最後合成才編碼一次 AAC；片段視訊格數及音訊樣本數由全段累積邊界相減取得。最多 32 個媒體 handle 同時活動；中間工作檔單獨估計空間，不冒充可任意驅逐的 cache。
- [ ] 唯一暫存及最後檔名皆由工作擁有，使用受控目錄與 exclusive create。來源、專案檔與既有成品不可當輸出；驗證成功才原子發布完整 MP4，不留半成品正式檔名。
- [ ] 新增 `BoundDirectory.PublishVerified(string temporaryName, string finalName) -> string`：僅接受同一已綁定目錄內的單一 leaf，使用不覆蓋的原子 rename。不可沿用現有 `PublishAsync` 先建立正式名稱再複製的行為，因為複製途中會暴露不完整成品。跨磁碟目的地在目的目錄內建立唯一暫存，完成驗證後才 rename。
- [ ] 素材解碼、編碼進度可取消；終態單一，取消只清自己的暫存。測磁碟不足、encoder 失敗、取消與完成競態、同名檔、父目錄替換、舊成品仍可讀；狀態查詢／取消不可被長 IPC 鎖阻擋。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter FullyQualifiedName~RecordingContentExport`；Expected 生命週期與實際媒體都 PASS。再用真實螢幕錄影剪輯與播放器核對成品。Commit `feat: export verified frame-accurate edited MP4`。

## Task 7: 接回直接錄影入口與完整原生驗收

**Files:** 續接既有 `2026-10-09-record-first-edit-after.md` Task 4–6：`RecordingContentController.cs`, `MainViewModel.cs`, `MainWindow.axaml`, `ProjectWorkspaceView*`, settings、localization；新增 `docs/acceptance/2026-10-09-editor-media-closure.md`。

- [ ] 完成主視窗與編輯器唯一 controller：移除首頁額外專案列，「內容編輯」置於 Stop 後；直接開始自動建內容、有效來源提交後才加入最近內容。編輯器不要求建立專案。
- [ ] 真實驗收：錄 A/B/C→暫停編輯→刪 B 中段→搬 C→A 後補 D→保存不手動輸出→關閉重開→追加 E→停止新 MP4；核對順序、聲音、原始五段 hash、舊成品未覆蓋。
- [ ] Stop 自動輸出使用現有已保存時間軸；停止後剪輯只保存；再輸出產生新檔。預覽停止失敗、保存失敗或狀態不確定一律不啟動擷取。
- [ ] 真正原生 GUI 1440×900、1280×720、1024×640，zh-TW/en-US、深淺色及高 DPI；截圖比對核准構圖，全部按鈕可見／可操作。來源缺失、空時間軸與錯誤提示必測。
- [ ] 兩小時／200 片段／1080p 專案：記錄硬體、版本、cold/warm seek、UI 反應、記憶體及磁碟cache；warm seek p95 目標 250ms。未達標必須報告，不改用低解析度短測資冒充。
- [ ] Run `dotnet test ScreenRecorder.sln -c Release`，再跑真實媒體與 macOS 操作故事。Windows無實機保留未驗證並阻止宣称完整雙平台驗收；不擅自上 Actions。
- [ ] 最後一次全分支獨立唯讀審查；Critical/Important 寫重現測試修正並跑完整 suite；不推送、不發布。Commit `feat: complete record edit reopen and export workflow`。

## 自我檢查與交付邊界

- 使用者本次點名四項：影音 Task1/3，縮圖波形 Task4，補錄 Task5，精剪輸出 Task6；Task7 驗證它們在同一主入口工作，不能各自 PASS 就宣稱產品完成。
- Task2 的不可變 render plan 是預覽／快取／輸出共同依據；source PTS 與 timeline ticks 不混用。Task3 Stop gate 被 Task5/7 消費；Task6沿用既有 exporter 簽名，不另開第二個 owner。
- 所有 Review Focus 均指到具體測試；現有 568 PASS/13 SKIP 是先前時間軸基線，不是本計畫完成證據。
- 探針、產品接線、原生實機與 Windows 驗收分開報告；不藉缺少 Windows 停掉可進行的本機工作，也不以 Mac 通過推定 Windows 通過。
- 保留既定 inline/native 實作方式；待使用者審阅本計畫後执行。若 Task1 未通過，播放器接線保持封鎖，但可繼續安全且獨立的 Task2/5/6，不再次用搬按鈕取代媒體能力。

## 官方技術參考

- [FFmpeg trim／atrim、時間戳及濾鏡說明](https://ffmpeg.org/ffmpeg-filters.html#trim)：裁剪與時間戳重設是不同操作，實作須維持共同原點。
- [FFmpeg fd／pipe 協議](https://ffmpeg.org/ffmpeg-protocols.html#fd)：regular-file FD 與不可 seek 的 pipe 需分別驗證，不能只替換 URL 就宣稱定位可用。
- [Apple playerTime(forNodeTime:)](https://developer.apple.com/documentation/avfaudio/avaudioplayernode/playertime(fornodetime:))、[Windows IAudioClock::GetPosition](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition)：音訊時鐘的候選依據；API 存在不代表本專案已整合或通過音畫驗收。
