# 網頁播放器輸出驗證 — 2026-10-13

## 完成內容

- 主畫面及編輯器「輸出…」選單：MP4 影片／網頁播放器；MP4 沿用檔名選擇，網頁選擇存放位置並建立獨立資料夾。
- 固定輸出 index.html、media.mp4、opencam-player.js。HTML／JS 註解標示 OpenCam 及專案網址。
- 嚴格 JSON 指派 window.OpenCamPlayerData，format=opencam.web-player、schemaVersion=1；章節來自最終 logical timeline，支援裁減、重排及合併。
- 已驗證 MP4 封裝為網頁，入口頁最後發布；取消清理本次已知檔案，既有檔案與素材保留，可能留空資料夾。網頁輸出不寫入 MP4 重用 receipt。
- 播放器以影片底部的自製控制列提供播放、進度、靜音／音量、倍速、寬螢幕及全螢幕；保留無 JS 時的原生控制列。章節顯示起始／長度並限制清單捲動高度，標題區改為 OpenCam 藍灰色系輕巧卡片。無新增第三方套件或外部網路資源。

## 已實際執行

環境：macOS arm64，.NET SDK 8.0.425，FFmpeg 7.1.2。

- 完整 Release 測試：Core 207、Media 703 通過，19 略過、0 失敗。日誌 /tmp/opencam-web-full-final.log。
- 15 項聚焦回歸：資料識別、安全字元、剪輯後章節時間、合併為單章節、輸出取消及影片完成後取消、IPC、暫停狀態、編輯器選單、首頁布局、藍底白字、原 MP4 檔名選擇；日誌 /tmp/opencam-web-regressions.log。
- 真正 ProjectFfmpegExporter 網頁封裝：驗證影片九影格、有聲音訊樣本、JS 章節及原 MKV SHA256 不變。
- 初版 Node 播放器互動測試 4 通過；後續控制列修訂的最新重點驗證另見下方，不以初版結果代表目前瀏覽器驗收。
- 網站 30 項通過：首輪因沙箱無法寫 Swift cache 失敗，使用獨立暫存編譯快取後通過；日誌 /tmp/opencam-web-website-final.log。
- git diff --check 通過。聚焦審查無阻擋；審查提出的包裝途中取消覆蓋缺口已補測。

首輪完整測試五個失敗來自仍定位 ProjectExportMp4 舊標籤的 UI 測試；依新需求改為 ProjectExportMenu，保留位置、白字及圖示檢查，最後完整回歸通過。

## 尚待人工確認

### 最新控制列修訂驗證

- Node 12 項通過、0 失敗：章節跳播／時間／150 章節、metadata 延遲、進度／播放／音量／倍速狀態同步、wide 還原、stage 全螢幕與 API 拒絕回退、閒置／焦點顯示、鍵盤邊界、播放拒絕提示及全螢幕快速連按防護。日誌 `/tmp/opencam-web-overlay-node.log`。
- 更新嵌入 HTML 後 Release 編譯及 5 項 WebPlayerExportTests 通過，0 失敗。日誌 `/tmp/opencam-web-overlay-package.log`。
- 此次僅做相關重點測試，未重跑完整 .NET suite；上方完整測試數字屬初版。瀏覽器實際繪製、原生播放／全螢幕及手機觸控仍需人工確認。

瀏覽器工具禁止 file:// URL，未繞過；實際離線播放與視覺驗收沒有標記 PASS。Windows 原生資料夾／檔案對話框及影音後端亦待實機驗收。

人工檢查：

1. 使用可拋棄的兩段以上有聲素材，裁減、排序、合併及更名後儲存。
2. 分別從首頁和編輯器選「輸出… → 網頁播放器」，選存放位置。
3. 確認新資料夾有三個固定檔案，雙擊 index.html；在桌機與窄螢幕檢查文字、版面及控制列。
4. 播放並點選每個章節，確認畫面／聲音及播放位置符合剪輯，拖動進度後章節標示同步；改倍速、音量、靜音及寬螢幕，再全螢幕拖曳進度。確認暫停／觸碰／鍵盤焦點時控制列顯示、播放閒置淡出；Esc 可退出頁面內滿版。使用大量章節檢查桌面／手機捲軸，頁面不得被章節數量撐高。
5. 選擇 MP4 影片輸出，確認仍可指定檔名；取消對話框及輸出時不得改寫既有成品或原 MKV。

若影片無法播放、章節錯誤、控制項不可操作或素材被更動，記錄瀏覽器／OS 版本、操作步驟、三個輸出檔及 app log。

初版保留於獨立工作區，當時未提交或發布，已發布的 v0.3.2 安裝包不含本次功能。

## v0.3.3 發布候選收尾

使用者後續授權收尾、增加 0.0.1、推送與 Release；VERSION 更新為 0.3.3。README、中英文使用說明、網站介紹與發布說明同步更新，沒有新增第三方相依套件。

- 本機還原相依套件及 Release 建置成功；版本一致性及 git diff --check 通過。
- 最終完整 Release：Core 207、Media 709 通過，19 略過、0 失敗，共 916 通過。日誌 `/tmp/opencam-v033-full-final.log`。
- Node 播放器 12 項及網站 30 項通過。日誌 `/tmp/opencam-v033-node.log`、`/tmp/opencam-v033-website-final.log`。
- 首輪完整回歸 4 失敗均為 InspectorActionsRemainInsideCardAndReachable 仍以純文字定位舊按鈕；改為定位圖示按鈕的可見文字，保留原尺寸、位置、捲軸安全斷言，最終四種語言／尺寸案例通過。
- 網站新增說明連結改成可發布的完整 URL；Swift 圖示產生器使用獨立暫存快取後通過，未略過測試。
- 聚焦唯讀整合審查無阻擋。瀏覽器實際排版及實機驗收限制維持上方狀態；完整實體 Release Gate 未宣稱通過。

正式發布仍須核對成功 GitHub Actions 的提交來源、Windows Setup／Portable ZIP、macOS DMG 及網站部署，不能用本機安裝包替代。發布狀態以 GitHub Release 為準。

### Windows CI 排程與快速輸出診斷

首個 0.3.3 Actions 38334013947 的 Windows 驗證失敗，未發布。其他 portable／macOS 驗證、macOS 建置及同提交網站部署成功。完整 Windows 日誌 `/tmp/opencam-v033-windows-ci.log`。

- 百片段含音訊快速輸出雖最後成功，但 normalized audio 末端為 100.085333 秒、4693 個 AAC 封包，原本快速路徑驗證拒絕並退回渲染；嚴格 phase 斷言失敗。PCM 合併有樣本總長檢查，尚不能判定 AAC 額外封包的來源。新增預期樣本數、預期起訖、實際差值與 tolerance，沒有放寬比較。故意傳入不一致樣本數的真實 MP4 驗證案例先失敗缺少診斷，再通過。
- 心跳注入失敗測試 0／3000ms 兩例首個 watchdog save 都約在 Recording 後 22.2 秒才到；20 秒等待逾時時，失敗後尚不足下一輪正常 2 秒重試。這不證明重試 catch 失效。新增 Debug 排程 queued／entered／前兩次 delay start/end、單調 elapsed 及 ThreadPool 計數；仍使用 Task.Run、2 秒間隔與 20 秒測試期限，不增加等待碰運氣。
- 診斷 logger 與測試輸出仍有32項、每項12000字元上限。不記錄影片像素或認證金鑰；排程診斷預設 Debug 層級。
- 新增診斷的相關18案例本機通過。這只是釐清下次 Windows CI 的證據，不宣稱原問題根治；發布前仍須新 CI 全部通過。
- 診斷補丁完整 Release 回歸 Core207、Media709 通過，19略過、0失敗，日誌 `/tmp/opencam-v033-ci-diagnostics-full.log`；聚焦唯讀審查無阻擋。測試逾時訊息在清理之前保存排程診斷，避免之後停止的日誌擠掉首輪事件。

### 合成錄影速率與 Windows 共享位置查詢修補

Actions 38336516066 的 Windows 再次失敗兩例，完整日誌 `/tmp/opencam-v033-windows-scheduling-ci.log`。watchdog queued 到 entered 僅約 0.3ms，但第一個兩秒 delay 到約 22.07 秒才完成；這次 transient save 兩例均通過，另一个持續心跳案例失敗。合成錄影約 23 秒卻產生 960.3 秒影片，顯示測試來源未按即時速率輸入。另一個十片段案例在封包、時序及音訊檢查之後的解碼檢查發生 Invalid NAL unit size，不能把它說成原始影片已確認損壞。

- 新增真實合成錄影速率回歸；舊碼約一秒產生 230.987 秒影片而失敗（`/tmp/opencam-v033-realtime-red.log`）。Windows／macOS 僅 synthetic lavfi inputs 各自加入 `-re`；真實擷取參數不變。修補後錄影速率、心跳及 provider 相關21項通過（`/tmp/opencam-v033-realtime-green.log`）。原 watchdog 間隔與測試期限沒有放寬；仍不能宣稱 Windows 計時續接延遲根因完全確定。
- Windows 媒體進度監測不再對與子程序共用的位置呼叫 SetFilePointerEx，改用 NtQueryInformationFile 的唯讀 FilePositionInformation 查詢。錯誤、未完成結果及無效資訊拒絕使用；原來源唯讀、活動期限、取消、驗證條件均保留。這移除 seek/set API 干擾並行讀取的風險，尚不能把歷史 NAL／AAC 偶發問題歸因為已證明的共享位置競態。
- 新增 Windows 真實繼承 handle 的慢速二進位讀取測試：2MiB 內容重複讀取四次，逐次比對輸出並核對原檔未變。此項在 macOS 明確略過，須新 Windows CI 執行後才可標示通過。
- 六檔修補聚焦唯讀審查無阻擋；版本仍為0.3.3，未提前建立 tag 或 Release。
- 修補完整本機 Release 回歸 Core207、Media710 通過，20略過、0失敗（共917通過），日誌 `/tmp/opencam-v033-paced-query-full.log`，SDK8.0.425／FFmpeg7.1.2。新增 Windows 原生查詢測試在略過項目內，不冒稱已於 Windows 通過；版本一致性及 diff check 通過。

### Windows 延遲堆疊觀察（尚非根因修復）

Actions38339297953 的 macOS／portable 驗證、macOS 建置與同SHA網站部署成功；Windows唯一失敗仍為 SessionHeartbeat_ContinuesWhileRecordingAndPaused 的固定2500ms後斷言。完整日誌 `/tmp/opencam-v033-paced-windows-ci.log`：watchdog入口0.0891ms，但首個2秒等待21105.4431ms才續接，當時workers8／available32759／pending1；23.2秒成品已與實際錄製相符，故未限速合成來源不是延遲的唯一原因。兩個 transient retry 案例未失敗，不能把此輪說成20秒持久化重試驗證失敗。Windows Media654通過、69略過、1失敗；新位置查詢測試及concat案例未失敗，但一輪通過不證明歷史問題根治。

只在Windows CI安裝固定版本Microsoft dotnet-stack8.0.547301（不加入應用程式依賴或安裝包）。該測試新增獨立專用執行緒觀察器：5秒仍未前進則讀取當前testhost的managed stacks；另一專用執行緒最多10秒等待診斷工具並終止超時工具，最後join。報告有界約60k字元、每行最多2000字元、分10k區塊；不收集dump、像素、參數或環境。原2500ms等待、心跳2秒及驗證斷言均不放寬。新CI若失敗須讀取 Independent heartbeat observer 與 managed stacks，先定位真正佔用worker的呼叫，再決定修補。

本機SDK8.0.425／FFmpeg7.1.2編譯及4個心跳案例通過，日誌 `/tmp/opencam-v033-stack-observer-targeted.log`；macOS不啟用Windows診斷工具，不能聲稱已驗證其Windows執行路徑。診斷補丁唯讀審查無阻擋，diff check通過；未重跑無關全套，也未宣稱全套本輪重新通過。尚未建立tag／Release。
