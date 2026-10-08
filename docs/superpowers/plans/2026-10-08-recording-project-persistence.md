# Recording Project Persistence — Phase B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在同一 OpenCam 程式中建立、保存、關閉、重開錄影專案並繼續真實錄製，不必輸出 MP4、不刪除來源。

**Architecture:** 沿用 Avalonia 與既有 Recorder process；Recorder 內的 ProjectRecordingCoordinator 是專案唯一寫入者，UI 只透過既有認證 IPC 發送命令。專案 manifest、session 安全工作檔與 MP4 成品各自管理；新增明確的 project completion policy，不以修改使用者全域設定來跳過 Remux。

**Tech Stack:** 既有 .NET 8、C#、Avalonia、System.Text.Json、xUnit、FFmpeg/ffprobe；不加入播放器或新外部相依。

**Spec:** docs/superpowers/specs/2026-10-08-recording-project-editor-design.md，主要實作 §2、3、7、8、11B；使用者追加「剪完先存檔，不輸出 MP4」。保留手動儲存、自動保存、Ctrl/⌘+S，輸出是獨立動作。

**Status:** 使用者已核准，並要求操作錯誤可還原。採主代理逐項實作，最後一次獨立審查；B–D 尚未完成。B 的名稱變更具復原／重做，C 的裁短、刪除、排序、群組及屬性共用相同歷史機制；儲存不清除當次歷史，重開僅恢復已存結果。有效備份用於檔案損壞復原，不能冒充操作 Undo。

## Global Constraints

- 新的隔離原生功能分支以目前穩定 master 建立；A 模型與這份計畫保留，不能恢復／合併已放棄的 editing-preview。執行前檢查基底是否仍為 0cdb34d，若不同先比對變更。
- 不推送、合併、增版本、建 tag、Release；不更改快速錄影的預設擷取、音訊、Remux、快捷鍵及救援行為。
- 專案採可搬移資料夾，含 project.opencam、sources、sessions、cache、exports。為保留現有 SessionPathPolicy，B 階段錄製檔留在 sessions/Sessions/<sessionId>/segment_NNN.mkv；manifest 以專案內相對路徑引用，不為整理目錄重複複製長片。sources 留給後續明確匯入；cache 可重建，不能刪來源。
- 專案模式永不因 DeleteWorkingFileAfterSuccessfulRemux 刪來源，也不藉修改全域偏好達成。快速模式維持現有行為。
- 「儲存專案」寫 manifest，不重編碼、不 Remux；自動保存於完成操作後一秒內啟動；只有落盤成功顯示「已儲存」。Ctrl/⌘+S 與按鈕呼叫同一命令。
- 「結束本次錄製」只完成安全 MKV 與專案保存，留在專案；「關閉專案」須先完成保存。錄製中關閉仍提示，不得停止錄影。暫停時可明確結束本次錄製並關閉，不強迫輸出。
- B 建立真正可用的專案生命週期及最小原生專案工作區，不移植整個瀏覽器模型、不新增假預覽。精準裁短／時間軸預覽／補錄插入／MP4 編輯輸出屬 C；畫面及音效處理、效能屬 D。尚未接通的功能不能顯示為可用。
- 來源時間資訊來自 ffprobe 實際 stream time_base/start_pts/duration_ts，不用 FPS×秒估算；B 保存時間基準及完整來源範圍，逐格 PTS 索引與預覽引擎留 C。
- 中英文完整；沿用 A 視覺方向與正式 App icon。來源、專案檔視為不可信，不接受 manifest 內命令、任意外部路徑或符號連結。
- 不以合成測試取代真實錄製。Windows 無實機則 BLOCKED；本機缺 .NET 8 runtime，先查可用隔離 runtime，缺少時經環境許可安裝至隔離工具目錄，不改 TargetFramework、不用 major roll-forward 冒充 .NET 8 驗證。

## Review Focus

1. 關閉與自動保存／暫停完成競態：舊 revision 成功不得把新變更標為已存；錄影保持不中斷（Task 2、4）。
2. MKV 完成但 manifest 尚未提交即 crash：重開能找回素材且不重複插入；失敗不把原工作檔當垃圾（Task 3）。
3. 惡意專案、搬移資料夾、symlink／hardlink／路徑替換：讀寫只能綁在使用者開啟的專案內（Task 2）。
4. IPC 超時重試與重複請求：一個 operationId 不得啟動第二段或重複追加；超時維持狀態未確認，需查詢（Task 3、4）。
5. 專案模式與快速模式交替、舊 session 缺新欄位：不得錯用 completion policy、刪除素材或改變既有快速 Remux（Task 3、5）。

## 共用契約與檔案分工

- Core/Projects：資料、驗證、無平台依賴的專案狀態。
- Infrastructure/Projects：manifest 儲存、寫入 lease、路徑驗證、提交 journal；複用安全的 BoundDirectory，不放媒體引擎或 UI。
- Recorder/Services：ProjectRecordingCoordinator 負責序列化專案命令，串接既有 RecordingOrchestrator；不能新增第二條 capture 實作。
- UI/Projects：ProjectWorkspaceViewModel、ProjectWorkspaceView、ProjectClient；MainViewModel 只做模式路由，避免繼續堆疊全部專案邏輯。
- tests/ScreenRecorder.Core.Tests：schema、保存、鎖、路徑、故障注入；tests/ScreenRecorder.Media.Tests：Recorder、IPC client/viewmodel、真實媒體閉環。

### Task 1: 定义可持久化專案與保存狀態

**Files:** Create src/ScreenRecorder.Core/Projects/RecordingProject.cs、ProjectValidation.cs、ProjectSaveState.cs、IProjectStore.cs；tests/ScreenRecorder.Core.Tests/RecordingProjectTests.cs。

**Interfaces:** RecordingProject 包含 SchemaVersion=1、Guid ProjectId、long Revision、Name、Canvas(width,height,fps rational)、Sources、Clips、Sessions、ViewState。Source 保存 Guid Id、SessionId、RelativePath、FileSize、Sha256、StreamTiming(timeBase numerator/denominator,startPts,durationTs)、媒體基本資訊；Clip 包含獨立 Id/SourceId、來源 InPts/OutPts、Name、GroupId、Volume/Muted/Fade、Crop/Scale/Position。B 只產生完整來源 clip 與名稱更新，數值預設保留後續相容性。

IProjectStore.CreateAsync(parent,name,ct) / OpenAsync(manifestPath,ct) 回傳 IProjectHandle : IAsyncDisposable；handle 提供 Current、SaveAsync(next,expectedRevision,ct):Task<ProjectSaveReceipt>，receipt 為 ProjectId/Revision/CommittedAt。ProjectValidation.Validate(project) 無效時拋 InvalidDataException。ProjectSaveState 記 DirtyRevision、SavedRevision、Saving/Failed，完成舊 revision 不清新 dirty。

- [ ] RED：schema roundtrip、非法 SourceId／零長區間／NaN／重複 ID／新版 schema 拒絕；設定值 roundtrip 不丟失；保存 revision3 完成而 current4，仍 dirty。上限 manifest4MiB、10,000 sources、10,000 clips、1,000 sessions、名稱200字、相對路徑1024字、JSON depth32；超限拒絕且不寫檔。ProjectEditHistory.RenameClip／Undo／Redo 返回新 revision，還原不刪sources；保存不清歷史、新編輯清redo，重開建立空歷史。
- [ ] Run `dotnet test tests/ScreenRecorder.Core.Tests -c Release --filter FullyQualifiedName~RecordingProjectTests`；Expected：RED 缺類別，不得把 runtime 缺失當測試 RED。
- [ ] 實作上述型別與驗證；時間 rational 分母必須正，區間 within source，B 不接受任意濾鏡字串。檔案大小上限在讀取階段先限制，不在 deserialize 後才檢查。
- [ ] 同命令 Expected：PASS；Core 不引用平台組件。
- [ ] 本機 commit `feat(project): define durable recording project contracts`。

### Task 2: 安全、原子、可恢復的真實存檔

**Files:** Create src/ScreenRecorder.Infrastructure/Projects/JsonProjectStore.cs、ProjectPathPolicy.cs、ProjectWriteJournal.cs；tests/ScreenRecorder.Core.Tests/ProjectStoreTests.cs、ProjectPathSecurityTests.cs。僅必要時擴充 src/ScreenRecorder.Infrastructure/Session/BoundDirectory.cs；不降低既有防護。

**Interfaces:** JsonProjectStore : IProjectStore。ProjectWriteJournal.WriteIntentAsync(handle,SegmentCommitIntent,ct)、ReadPendingAsync(handle,ct)、CompleteAsync(handle,operationId,ct)。Intent 包含 operationId/sourceId/sessionId/relativePath/expectedRevision/status；路徑均由已開啟 handle 與程式產生的 leaf 組合，不信任 manifest 的絕對路徑。

- [ ] RED：暫存目錄新建→保存→dispose→新 store 重開，內容及 revision 相等；搬移整個資料夾後來源仍能解析。第二 writer 被拒絕，第一 writer dispose 後可開；拒絕過期 expectedRevision。
- [ ] RED：before-write、after-temp-flush、before-replace 注入 IOException／取消，至少舊版仍可讀；主檔損壞可明確提示由有效備份恢復，不悄悄丟掉較新變更。新版 schema 不用舊備份覆寫；完整性失敗保持唯讀並留原檔。
- [ ] RED：../、絕對路徑、UNC、ADS、根目錄前綴混淆、symlink祖先／leaf、hardlink、目錄替換均拒絕或安全綁定；外部 sentinel 檔位元不變。以兩個 process 測 writer lease，不只兩個物件。
- [ ] Run Core.Tests project filters ProjectStoreTests/ProjectPathSecurityTests；Expected：FAIL 因 store 未實作。
- [ ] 實作同目錄隨機暫存、flush、原子替換、有效上一版備份及 OS writer lease；保留 lease 至 close。複用 BoundDirectory pinned handles；若加入 Claim(string leaf) 必須保留既有 Claim() 預設 .recovery.lock 行為，專案使用 .project.lock。備份只覆寫為驗證有效的上一版。
- [ ] 保存及來源開啟前驗證每層路徑，不能只做字串 StartsWith。先開啟／綁定再使用，不放寬 SessionPathPolicy。平台能做到的目錄同步與斷電限制寫 ADR，不把程序 crash 測試當斷電保證。
- [ ] 同 filters + 原有 BoundDirectoryTests/SessionStoreTests/SessionPathSecurityTests；Expected：全 PASS；本機 commit `feat(project): persist projects with atomic saves and writer leases`。

### Task 3: 真實錄製與專案提交閉環，不強迫輸出

**Files:** Create src/ScreenRecorder.Recorder/Services/ProjectRecordingCoordinator.cs、ProjectSegmentCommitter.cs；src/ScreenRecorder.Core/Projects/ProjectSessionOwnership.cs；src/ScreenRecorder.Media/Probe/ProjectSourceProbe.cs；tests/ScreenRecorder.Media.Tests/ProjectRecordingLifecycleTests.cs、ProjectCommitRecoveryTests.cs。Modify src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs、src/ScreenRecorder.Core/Models/RecordingSession.cs、src/ScreenRecorder.Infrastructure/Recovery/RecordingRecoveryService.cs。

**Interfaces:** 新增 RecordingSession.ProjectId:Guid? 與 CompletionPolicy(enum QuickMp4=0,KeepProjectSources=1)，舊 JSON 預設 QuickMp4。StartProjectRecordingAsync(config,ProjectSessionOwnership,ct) 為 Orchestrator 的新明確入口；舊 StartRecordingAsync 簽名不變。Ownership 只能由 coordinator 的有效 handle 建立，不能由普通 StartRecording payload 直接指定。

ProjectRecordingCoordinator 提供 CreateAsync(parent,name,ct)、OpenAsync(path,ct)、StartAsync(config,operationId,ct)、PauseAsync(operationId,ct)、FinishAsync(operationId,ct)、SaveAsync(expectedRevision,ct)、CloseAsync(ct)、GetStatus()。方法回傳 ProjectCommandResult(Success,ErrorCode,ProjectId,Revision,Mode,OperationId)；模式 Closed/Ready/Recording/SavingSegment/Paused/SaveFailed/Interrupted。Start 在 Ready 或 Paused 執行：同次 session 續錄，關閉重開後新 session 追加。ProjectSegmentCommitter.CommitAsync(handle,finalizedSession,operationId,ct) 回傳保存 receipt，重複 operationId 不重複追加。

- [ ] RED：三段暫停、Finish 後 source 數=3，remux call=0、delete call=0；正常關閉後新 coordinator 重開追加第四段，source數=4，前三段雜湊不變、舊 session 不重啟寫入。
- [ ] RED：pause finalize/probe/store 任何一步失敗都不解鎖續錄、不宣稱保存；正長度有效 video 才可提交。處理 invalid／zero-byte 片段但保留檔案。當前正在寫入的 segment 不得提早列可編輯。
- [ ] RED：MKV完成／journal完成／manifest完成各點 crash，重開 reconcile 恰好一次；恢復不了顯示候選及原因，不自動刪除、不自動略過。舊回應或同operationId重送不增加素材。
- [ ] RED：每個內部停止路徑（磁碟、watchdog、裝置失效、父行程關閉）遵守 project policy；快速 session 仍正常 Remux，設定 true 時原既有清理測試不變。完成的專案 session 不被列異常；Interrupted仍可救援且保持 project sources。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectRecordingLifecycleTests|FullyQualifiedName~ProjectCommitRecoveryTests"`；Expected：RED 未實作。
- [ ] 實作 coordinator序列化gate、journal、probe實際時間基準及source hash。session root 使用專案 sessions，沿用 StorageService 建 Sessions/id；傳入複本 config 並强制 project保留來源，不修改使用者設定。專案清理保護由 policy／ownership 守住，不單靠 config旗標。
- [ ] 將安全完成 MKV 與 Remux 分成 Orchestrator 私有步驟；project 分支在完成／驗證／保存後返回無MP4，quick維持完整既有路徑。暫停回應需等專案提交完成；UI只相信coordinator模式，不能把較早的Recorder.Paused當成功。
- [ ] 重開執行 journal及session reconcile；來源遺失先禁止續錄並顯示路徑，B可請使用者還原原路徑後重試；互動重新連結屬C。續錄前重新檢查裝置與geometry，不因舊螢幕index改變而錄錯。
- [ ] 同 filters + RecordingCaptureLifecycleTests/RecordingEncoderLifecycleTests/RecordingRecoveryTests/RecoveryRaceTests；Expected：全PASS。本機 commit `feat(project): retain and resume recording sessions without remux`。

### Task 4: 同一原生入口、儲存按鈕與關閉保護

**Files:** Create src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs；src/ScreenRecorder.UI/Projects/ProjectClient.cs、ProjectWorkspaceViewModel.cs、ProjectWorkspaceView.axaml(.cs)；tests/ScreenRecorder.Media.Tests/ProjectWorkspaceTests.cs、ProjectIpcTests.cs。Modify src/ScreenRecorder.Recorder/Program.cs、src/ScreenRecorder.UI/ViewModels/MainViewModel.cs、src/ScreenRecorder.UI/Views/MainWindow.axaml(.cs)、src/ScreenRecorder.Core/Localization/LocalizationService.cs、src/ScreenRecorder.Infrastructure/IPC/IpcMessages.cs、src/ScreenRecorder.Core/Models/UserSettings.cs。

**Interfaces:** IPC新增 CreateProject/OpenProject/GetProjectStatus/GetProjectClips/StartProjectRecording/PauseProjectRecording/FinishProjectRecording/SaveProject/RenameProjectClip/CloseProject；request帶ProjectId、ExpectedRevision、OperationId（需要時）及型別化payload；新回應用 IpcResponse.DataJson 可選欄，不再把新資料塞進ErrorMessage，舊clients行為不變。GetProjectClips 用 offset/limit，limit<=100，保持既有1MiB frame上限；不將完整大專案在每次telemetry傳輸。

ProjectClient封裝上述非同步命令與未知狀態重查。ProjectWorkspaceViewModel提供 Create/Open/Start/Pause/Continue/Finish/Save/Close/RenameClip commands、SaveStatus、CurrentRevision、SavedRevision、CanClose。UI的最近專案只存locator，不是manifest寫入者；由既有settings服務新增最多20筆RecentProjectPaths。

- [ ] RED：剪輯名稱修改→自動保存最遲1秒啟動；按儲存與快捷鍵都走相同Save命令，不呼叫Stop／Remux／Export。save failure保留dirty；延遲revision3回應不清revision4。文字欄Ctrl/⌘+S儲存，其他編輯快捷鍵仍不攔截。
- [ ] RED：錄製中點X只警告且Recorder保持Recording；保存中close等待、失敗留視窗可重試；專案Paused點「結束本次錄製」完成後可Close，不受quick模式Paused永久封鎖。UI/Recorder狀態未知時不可自行清busy或重送新的operationId。
- [ ] RED：無認證、錯projectId、超大payload、未知command拒絕；重複操作和遺失response後查status不重錄。Quick模式全套既有測試保持。
- [ ] Run `dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectWorkspaceTests|FullyQualifiedName~ProjectIpcTests"`；Expected：RED 未實作。
- [ ] 實作主程式Quick／Project入口、原生檔案選擇器新建／開啟、最近專案、片段清單與保存狀態。保留現有擷取設定元件；project畫面明確有「儲存專案」「關閉專案」，對後續輸出功能標示尚未開放，不提供假按鈕或成功提示。
- [ ] 雙語實作保存中／已儲存／失敗、dirty離開警告及error重試。首次建立就選位置並成功保存才允許錄影；取消選擇不產生半套專案。鎖釋放僅在成功close／程序結束，UI斷線不直接釋放仍錄製的專案。
- [ ] 同filters+IpcSecurityTests/FoolproofUiLogicTests/LocalizationTests；Expected：全PASS；本機commit `feat(ui): add recording project save and reopen workflow`。

### Task 5: 真實資料驗收、回歸及交付

**Files:** Create tests/ScreenRecorder.Media.Tests/ProjectRecordingIntegrationTests.cs、docs/acceptance/2026-10-08-project-phase-b.md、docs/adr/recording-project-ownership-and-persistence.md；更新 TESTING.md、MANUAL_TEST_CHECKLIST.md。此階段不更新網站宣稱正式可用、不改VERSION。

- [ ] RED：加入實際store+Recorder整合測試，明確platform及device條件；使用新臨時測試專案，不讀寫使用者既有錄影。驗收3段→保存→正常關閉程序→重啟→第4段→保存→搬移後重開；sources=4、每段ffprobe有效、前3段hash不變、無自動MP4、無假成功。
- [ ] 執行測試；Expected：尚未接全流程時FAIL；硬體未授權／平台不存在列BLOCKED，不視為RED或PASS。
- [ ] 完成測試接線及針對發現的根因修復；故障注入只限本次測試process及temp project，不殺現有使用者Recorder、不填滿真實磁碟。
- [ ] Run `dotnet restore ScreenRecorder.sln`、`dotnet build ScreenRecorder.sln -c Release --no-restore -m:1`、`dotnet test ScreenRecorder.sln -c Release --no-build -m:1`、`git diff --check`；Expected：exit0，所有可執行測試PASS，未執行的硬體項獨立列出。
- [ ] GUI從正式App入口操作完整故事，核對儲存檔實際落盤、重啟保留、暫停改音訊只影響下一段；錄影進行中儲存不可截斷当前MKV。另測quick四種音訊模式、停止MP4與救援無回歸。收集OS/runtime/commit/設定、操作截圖、log、來源hash及probeJSON。
- [ ] Windows若本機不可用，交付人工清單（先決條件、步驟、期望、失敗、證據）；沒有新授權不推送觸發Actions。本機macOS無權限則請使用者處理，不繞過OS安全權限。
- [ ] 本機commit `test(project): verify save reopen and continued recording`；最後獨立全分支review，修復高影響問題並重跑測試，開正式App讓使用者驗收。B未通過不進C，不能把整個剪輯功能標完成。

## 自檢與後續分工

- 實作B覆蓋：真實專案建立、追加分段、多次啟動續錄、手動／自動保存、安全結束、保留來源、失敗恢復、快速模式回歸。
- C另計畫：正式非破壞剪輯命令、精準PTS索引／預覽引擎選型、拖曳時間軸、指定位置補錄、素材重新連結、獨立MP4輸出。B schema先保留這些資料欄位，但不以roundtrip測試宣稱C完成。
- D另計畫：畫面／音效處理、長片效能、雙平台完整UI驗收。
- Review Focus五項均分配到Task測試；資料只有Recorder store負責写入，UI沒有第二份可獨立保存的專案真相。
- Scope與AGENTS初版「不做剪輯」衝突依使用者後續明確需求處理；可靠性、來源保護、跨平台界線維持不變。
