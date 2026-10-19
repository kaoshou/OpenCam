# 錄前檢查偏好與預覽延遲研究

後續已將預覽專用模式接入產品，見 [低延遲整合與驗證](2026-10-20-preflight-low-latency-integration.md)。本文件以下保留該輪研究當時的範圍及結果。

## 範圍

使用者同意短方案：實作預檢開關，預設開啟；關閉時跳過視窗但保留必要安全檢查。第二項先量測並提出低延遲改善方案，不改正式錄影參數。本輪不升版、推送或發布，不改前一批尚未提交的 CI 調整，也不另派審查代理。

## 已實作

- `UserSettings.RecordingPreflightEnabled` 預設 `true`。舊 JSON 缺此欄位仍開啟；保存 `false` 後重新載入可保留，恢復預設值會重新開啟。
- 設定頁加入中英文開關及關閉後需自行確認範圍／音源的說明。
- 主畫面載入、套用及另存其他偏好時保留此值。新的錄製、續錄與插入錄製共用同一條預檢選擇；啟用但沒有預檢 handler 時仍拒絕開始。
- 關閉僅略過預檢回呼，不改錄製權限、專案命名／未儲存修改處理、錄影程序擁有權、原有輸出／磁碟保護、實際啟動確認或正式編碼參數。

## 開關驗證

- RED：新增案例在原碼下 Core 1 失敗；Media 4 失敗、1 通過。原因是舊設定未輸出新欄位、關閉仍進預檢、設定頁沒有控制項，`/tmp/opencam-preflight-toggle-red.log`。
- GREEN：Core 1／Media 5 通過，包含實際 JSON 儲存、兩種語言的 headless UI 讀寫／重設、新建及續錄、權限拒絕與重新開啟後取消續錄，`/tmp/opencam-preflight-toggle-green.log`。
- 完整 Release build／test：Core 249、Media 826 通過，Media 20 項依既有平台／實機條件略過，0 失敗，命令 exit 0；媒體耗時 4 分 22 秒，`/tmp/opencam-preflight-option-full.log`。既有 nullable／Avalonia／xUnit 警告未當作本輪修復。不是 Windows／macOS 原生完整驗收。
- 首次命令在測試前因舊暫存 SDK 缺少 `dotnet.runtimeconfig.json` 啟動失敗，不算測試結果。從 Microsoft 官方固定 URL 還原 SDK 8.0.425 到 `/tmp/opencam-sdk8-20261020` 後才執行上列 RED／GREEN。FFmpeg 7.1.2。

## 延遲量測方法與界線

- 原碼路徑：擷取引擎產生 H.264／MKV → pipe → 第二個 FFmpeg 解碼為 960×540 RGBA、4 FPS → 最新影格槽 → UI 序列讀取狀態與影格、每輪再等 250ms。
- 串流輸出沿用正式編碼器設定與 1000ms MKV cluster；這不等於固定增加整整一秒，需實測。解碼端沒有預檢專用的探測參數。
- 可拋棄實驗 `/tmp/opencam-preflight-latency-probe.mjs` 使用真正 FFmpeg、合成 64×36／30 FPS 無音訊素材，八秒中在第四秒由黑轉白。Windows CPU 分支的 `libx264 / veryfast / CRF 23 / yuv420p` 參數在 macOS 執行，串接與解碼 filter 仿照現有管線；不讀實際桌面或麥克風。
- 最終量測以 producer `showinfo` 的第 120 張（PTS=4s）出現時間，對比解碼後第一張白色完整 RGBA 的接收時間。不可只用啟動後 4 秒推算，因 `-re` 的初始讀取節奏及程序初始化也會影響牆鐘。
- 對照保持影格尺寸、輸出 FPS、來源與傳輸方式不變；記錄 first encoded bytes（可能只是 header）、首張完整影格、白色標記、32 張完整輸出、退出碼及有界錯誤文字。只量測到 decoder，不包含 UI／IPC、原生擷取、硬體編碼器或音訊交錯；不能當 Windows 實機端到端延遲或固定加速保證。
- 初探（未用 showinfo 校正的牆鐘）位於 `/tmp/opencam-preflight-latency-20261020.log`、`/tmp/opencam-preflight-latency-30fps-20261020.log`，不將其中 59ms 當成正式端到端數字。正式方案仍需真實來源及四種音訊模式驗收。

完整回歸結束後、無並行測試負載的最終對照：`/tmp/opencam-preflight-latency-final-20261020.log`，四組 producer／decoder 都 exit 0、無 stderr 錯誤，各 32 張完整影格。以下延遲是 producer 的白色影格 `showinfo` 事件到 decoder 收到第一張完整白色 RGBA，不含 UI；每種情境一次，不是延遲分布或 CI 通過門檻。

| 對照 | 首張完整影格（自程序啟動） | 白色影格延遲 |
| --- | --- | --- |
| 現有參數 | 860ms | 1615ms |
| 僅解碼 `probesize=32768 / analyzeduration=100000` | 867ms | 1619ms |
| 僅 encoder 加 `tune=zerolatency / bf=0` | 564ms | 1179ms |
| 上述低延遲編碼、解碼參數及 `cluster_time_limit=100` | 59ms | 547ms |

結論：此素材下只改探測時間無改善；編碼緩衝有可量測貢獻，結合較短封裝緩衝可再縮短。最後一組同時改封裝及解碼，不能將全部差額歸給單一參數。首張 59ms 受到合成來源初始讀取節奏影響，不是原生初始化速度，也不是 59ms 端到端延遲。UI 的 250ms 等待尚未計入，不能只加快 UI 就宣稱這段緩衝已解決。

## 後續方案

1. 只在即時預檢串流設定低延遲編碼及較短 MKV cluster，不修改正式錄製／五秒試錄的檔案編碼設定；不能以預覽的 CPU 編碼器取代使用者選定硬體編碼器的驗證。NVENC／QSV／AMF／VideoToolbox 需各自支援與回歸，不直接共用 x264 專屬參數。
2. 解碼探測調整須保留真實 MKV／H.264 解析及失敗可見性，不能藉跳過資料、吞錯或顯示舊圖假裝即時。此參數單獨是否有效由對照結果決定。
3. 最後才評估 UI 更新頻率；維持單一請求與最新完整影格，避免排隊。單提高 FPS 不能解決上游緩衝。

預覽管線本輪僅研究，候選參數未套入產品。

## 實機驗收（REQUIRES MANUAL VALIDATION）

設定預設開啟 → 關閉保存、重開應用仍關閉 → 開始／續錄不開預檢 → 重新開啟後可再次預檢及取消。確認原有專案名稱、未保存修改、權限與磁碟錯誤仍可阻擋不安全的開始。用無音訊／僅系統聲音／僅麥克風／兩者逐一核對正式影片，關閉預檢不可改變錄音選擇。

未來套入低延遲方案時，需以同畫面的計時器／可識別閃爍標記測量真正 Windows／macOS 來源到 UI 的延遲，覆蓋螢幕／自訂範圍、各編碼器、四種音源、取消／重查與多螢幕 DPI。保留原有原生／長時間錄製驗收限制。
