# Editing Preview Qualification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立獨立、可執行的剪輯預覽驗證工具，證明選取範圍、片段重排、精確定位及音畫播放的可行性，並交付雙平台採用建議。

**Architecture:** 在隔離 worktree 的 `experiments/EditingPreview/` 建立實驗程式，不加入正式 solution 或安裝包。使用不可變片段列表與共同時間映射生成預覽／驗證成品，播放器候選為 LibVLCSharp；測試失敗先回報，不將實驗直接接入 Recorder。

**Tech Stack:** .NET 8、Avalonia 11.2.5（與目前 UI 一致）、xUnit、既有 FFmpeg／ffprobe；LibVLCSharp／LibVLC 版本與平台資產在 Task 1 依官方證據固定。

**Spec:** `docs/superpowers/specs/2026-10-02-project-editing-design.md`，本計畫只實作第 9 節的「可行性資格驗證」，不是完整編輯器交付。

## Global Constraints

- 僅處理自行產生的測試素材；不讀取、搬移或剪輯使用者既有錄影。
- 不修改正式 `src/`、`ScreenRecorder.sln`、版本號、Release workflow 或封裝依賴；不推送、不合併、不發布。
- 使用 sibling/worktree 隔離，保留目前未追蹤文件；實驗內容不得誤入正式包。
- 支援 Windows x64／macOS arm64 的資格檢查；缺硬體／GUI 一律 BLOCKED，CI 與模擬測試不能冒充實機通過。
- 半開來源區間 `[in, out)`；以整數畫格及明確有理數 FPS 轉換時間，不累加浮點秒。
- 移動一次一個片段，插入而非覆蓋；畫面與音訊一起移動，來源檔不變。
- 預覽與驗證成品共用片段排序與切點；移動後不能按檔名或錄製順序還原。
- 不使用待驗證的套件版本或全域 VLC 安裝來冒充可交付封裝；所有新增套件只在實驗目錄內固定版本。

## Review Focus

1. 被裁掉的影格／聲音在跨接點時短暫漏出：Task 2 合成標記及 Task 3 實際播放器接點測試必須檢查。
2. 快速拖曳定位時舊結果覆蓋新位置：Task 2 測試最後請求優先，Task 3 測試連續定位與關閉。
3. 原生播放器在開發機能用但封裝缺相依或架構不符：Task 1 固定依賴並於未安裝 VLC 的環境驗證。
4. 裁切後重排改變範圍、音訊或總長度：Task 2 驗證 clip 身分與來源區間，Task 3 比較播放／成品。
5. 停止預覽仍有音訊輸出／殘留程序：Task 3 驗證停止確認與失敗時禁止虛擬續錄，不呼叫真實錄影服務。

## 檔案責任

- `experiments/EditingPreview/README.md`：啟動方式、實驗性標示、資產取得與平台限制。
- `experiments/EditingPreview/dependencies.json`：精確套件版本、官方下載來源、架構、原生檔 SHA-256 與授權文件來源。
- `experiments/EditingPreview/EditingPreview.csproj`、`Program.cs`、`App.axaml`、`App.axaml.cs`：獨立 Avalonia 實驗程式。
- `experiments/EditingPreview/TimelinePlan.cs`：片段與成品時間映射及單片段重排。
- `experiments/EditingPreview/FixtureGenerator.cs`：生成 A/B/C/D 合成畫面及聲音標記，不使用真實麥克風。
- `experiments/EditingPreview/PreviewMediaBuilder.cs`：精確切點影格、短接點快取及驗證用成品生成，受控子程序生命週期。
- `experiments/EditingPreview/PreviewController.cs`：最後定位請求優先、停止／釋放及播放狀態。
- `experiments/EditingPreview/MainWindow.axaml`、`MainWindow.axaml.cs`：上方播放、下方時間軸與單片段重排示範；不承擔錄影邏輯。
- `experiments/EditingPreview.Tests/EditingPreview.Tests.csproj`、`TimelinePlanTests.cs`、`PreviewMediaTests.cs`、`PreviewControllerTests.cs`：隔離測試。
- `docs/verification/2026-10-02-editing-preview.md`：可重現證據、限制與採用判斷。
- `docs/adr/0008-nondestructive-project-editing.md`：專案與 session 分離、非破壞式剪輯及播放器採用裁決。

### Task 1: 固定候選依賴與可執行平台探針

**Files:** dependencies.json、README.md、EditingPreview.csproj、Program.cs、App.axaml／.cs、驗證報告及 ADR。

**Interfaces:**
- Produces: `--probe-runtime` 模式；stdout 一行 JSON 包含 OS、ProcessArchitecture、ManagedPackageVersion、NativeVersion、LoadSucceeded、Error。成功退出 0，缺原生相依／錯架構退出 2，不啟動錄影。
- Produces: 精確鎖定且可重現取得的相依清單。若官方可取得資產不支援其中一個平台，明確 BLOCKED 並停止採用，禁止任意換來源。

- [ ] 從官方文件、套件 metadata 核對 LibVLCSharp 與 Avalonia 11.2.5 的整合方式；選可支援本實驗的穩定相容版本，禁止 preview／floating 版本；記錄來源、精確版本及授權責任。原生 Windows x64／macOS arm64 資產分別驗證，不假設 managed 包已包含原生檔。
- [ ] 建立最小原生載入失敗測試：隔離程序指定不存在的 library 目錄，斷言 JSON 的 LoadSucceeded=false、退出 2；先觀察未實作探針導致失敗。
- [ ] 實作 probe 與獨立專案。不要要求安裝 VLC 到系統 Applications／Program Files；原生資產置於實驗輸出目錄，產物與下載快取加入局部 `.gitignore`。
- [ ] 執行 `dotnet run --project experiments/EditingPreview -- --probe-runtime`；預期可用平台 LoadSucceeded=true，錯誤路徑測試 LoadSucceeded=false。macOS arm64 不能以 Rosetta/x64 成功充數。無另一平台環境時填 BLOCKED，而非停止其他可做的本機實驗。
- [ ] 以 `dotnet publish experiments/EditingPreview -c Release -r osx-arm64 --self-contained true` 或 `-r win-x64` 檢查原生檔進入 publish；實際在對應平台跑 probe。若當前平台也無可用資產，提交調查報告並停止後續播放器整合。
- [ ] Commit：`docs: record preview dependency qualification`（加入本 task 實驗檔與資格紀錄，不加入二進位）。

### Task 2: 可測試的時間映射、裁切與重排預覽素材

**Files:** TimelinePlan.cs、FixtureGenerator.cs、PreviewMediaBuilder.cs、兩個 tests 專案檔及 TimelinePlanTests.cs／PreviewMediaTests.cs。

**Interfaces:**
- `record Clip(string Id, string AssetId, long InFrame, long OutFrame)`：本資格實驗使用 CFR 30／60 FPS 夾具，來源 PTS／timebase 另外由 ffprobe 紀錄；不是正式專案格式。
- `record SourcePosition(string ClipId, string AssetId, long Frame)`。
- `TimelinePlan(IReadOnlyList<Clip> clips, int fps)`，`long FrameCount`，`SourcePosition Locate(long outputFrame)`，`TimelinePlan MoveBefore(string clipId, string? beforeId)`；null 表示末尾，不可變輸入。
- `FixtureGenerator.CreateAsync(string newEmptyDirectory, int fps, CancellationToken ct)` 回傳 assetId 到絕對路徑的映射；拒絕非空目錄，四個各 4 秒的 MKV，A/B/C/D 有不同可機器識別畫面與音訊頻率，片段內每格有編號。
- `PreviewMediaBuilder.BuildAsync(TimelinePlan plan, IReadOnlyDictionary<string,string> assets, string newOutputPath, CancellationToken ct)`；以同一 plan 產生精確重新編碼 MP4，不覆寫存在的路徑，輸入 hash 不變。

- [ ] 先加入紅測試：A/B/C/D 各 120 格，D 裁成 `[30,90)` 後移至 B 前；斷言順序 A/D/B/C、FrameCount=420，Locate(120) 為 D 第 30 格、Locate(179) 為 D 第 89 格、Locate(180) 為 B 第 0 格。原列表、來源範圍不變；原位不變，未知 ID／越界拒絕。
- [ ] Run：`dotnet test experiments/EditingPreview.Tests --filter FullyQualifiedName~TimelinePlanTests`。Expected：先 RED，完成純模型後 GREEN；包含前移／後移／首尾、零片段、单片段、重複 ID、反向範圍及 long 溢位拒絕。
- [ ] 實作 deterministic fixtures 與 FFmpeg renderer，從每個 source 起点正規化 PTS、處理裁切及串接。最多一個 FFmpeg 作業，stdout/stderr 持續排空且尾端 ≤ 64 KiB；測試素材作業總期限 60 秒，取消後清理期限 5 秒，只終止自己擁有的子程序。命令用 ArgumentList，不拼 shell。
- [ ] Run：`dotnet test experiments/EditingPreview.Tests --filter FullyQualifiedName~PreviewMediaTests`。Expected：先 RED（尚無輸出），實作後 30／60 FPS 都 GREEN；檢查畫格身份順序、剪接點前後不得出現排除區間、音訊頻率順序與成品一致、總長誤差 ≤ 1 格、A/V 標記偏差 ≤ 100ms。
- [ ] 加入非零 PTS／缺音軌／掉幀夾具：以 ffprobe 的可解碼 PTS 作判定，不以 nominal FPS 猜來源；本實驗若不能覆蓋，將該資格標 FAIL 並說明，不宣稱所有素材可精確剪輯。
- [ ] 故障測試：無效輸入、已存在輸出、取消與假子程序無限輸出；斷言不覆寫、不修改來源、退出後無擁有的殘留程序；快取產物只刪本次實驗建立者。
- [ ] Commit：`test: qualify editing timeline mapping and reordered media`。

### Task 3: 可操作預覽工具與資格報告

**Files:** PreviewController.cs、MainWindow.axaml／.cs、PreviewControllerTests.cs、README.md、驗證報告及 ADR。

**Interfaces:**
- `PreviewController.LoadAsync(TimelinePlan plan, CancellationToken ct)`；`SeekAsync(long frame, CancellationToken ct)`；`PlayAsync(CancellationToken ct)`；`StopAsync(CancellationToken ct)`；`IAsyncDisposable`。
- 定位帶遞增 request generation；最多一個作業，僅最新請求可更新畫面。`StopAsync` 成功代表音訊已停止且待更新事件失效；不能確認時回傳錯誤，測試用「模擬續錄」按鈕不可啟用。
- Controller 接收可替換的播放器 adapter；fake 僅測並發／錯誤處理，實際 LibVLC 播放與封裝另行驗證，不把 fake 結果標成媒體驗收。

- [ ] 紅測試：舊定位在新定位之後完成不得覆蓋畫面；Load 新排序後舊接點結果不得發布；Stop 失敗不能進入可續錄狀態；Dispose 之後拒絕新請求。
- [ ] 實作 controller 與最小工具：上方預覽，下方時間軸／播放游標／起訖控制及片段移動把手；載入僅限生成夾具。支援 A/B/C/D、裁切 D、拖至 B 前、復原／重做及接點前後各 2 秒預覽；醒目標示「實驗工具，未整合錄影」。
- [ ] 拖曳停止預覽，提供插入線及 Esc 取消；獨立「往前／往後」按鈕與時間輸入，避免拖不準時無替代。裁切與排序共用 Task 2 plan。先嘗試原素材播放；若無法可靠隔離切點，使用短接點快取，準備中禁止播放錯誤順序。
- [ ] Run：`dotnet test experiments/EditingPreview.Tests`。Expected：純邏輯／controller／真實合成媒體測試全通過；平台缺項獨立列 BLOCKED，不刪測試降低門檻。
- [ ] Run：`dotnet run --project experiments/EditingPreview`。Expected：本機看到能播放的預覽，拖曳定位／裁切／重排與驗證成品一致；保存 screenshot、機器驗證 JSON 與 FFmpeg／原生版本，但不宣稱完整 OpenCam 功能已完成。
- [ ] 真實 GUI：連續定位 100 次、快速 A/D/B/C 切換、重複播放／停止 50 次、關閉再開 10 次。記錄資源趨勢、停止聲音及殘留程序；2 小時／200 區間定位取至少 100 筆，熱快取 p95 ≤ 300ms；超過 500ms 顯示定位中，UI 不阻塞。缺硬體平台 BLOCKED。
- [ ] Run：`dotnet test ScreenRecorder.sln -c Release` 及 `npm test --prefix website`；Expected：既有回歸通過，實驗相依沒有混入正式產品。若 SDK 僅 .NET 10，記錄 DOTNET_ROLL_FORWARD=Major 限制；不改產品目標 net8.0。
- [ ] 完成報告：逐項 PASS／FAIL／BLOCKED、輸出／hash／畫格與音訊證據、依賴大小及授權標示、性能結果、是否建議採用。沒有 Windows GUI 時不得宣布双平台資格完成；本機結果仍可交付使用者審阅。
- [ ] Commit：`feat: add isolated editing preview qualification tool`。

## 完成條件與下一步

交付是「可操作的隔離實驗＋明確採用判斷」，不是正式版剪輯功能。專案持久保存、鎖、故障恢復、真正錄影控制、正式 UI／本地化與輸出整合，分別落在設計第 9 節後四階段，需依資格證據再寫計畫。

本計畫自檢：三個任務的模型介面一致；五個 Review Focus 均有測試／實機步驟；不將原生依賴未知狀態當既成事實；本機可前進、另一平台缺環境可如實阻塞；不讀使用者素材；無正式碼變更。待使用者審閱本計畫並選擇執行方式後開始。
