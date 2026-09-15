# 可靠性與資料安全手冊 (Reliability & Data Safety)

本專案將「資料安全與錄影可靠性」置於所有功能與視覺效果之上。本手冊定義缺陷嚴重等級、救援機制與磁碟保護架構。

---

## 1. 缺陷嚴重等級 (Bug Severity Levels)

| 等級 | 定義 | 範例 | 發布限制 |
| :--- | :--- | :--- | :--- |
| **S0 (致命/資料毀損)** | 導致已錄製內容全部或部分不可逆遺失 | 錄影崩潰後產生 0 byte 壞檔、Recovery 誤刪原始檔、直接寫 MP4 斷電全毀 | **絕對禁止任何發布，必須立即阻斷並修復** |
| **S1 (嚴重/錄影中斷)** | 造成錄影功能無法啟動或在錄影中無故崩潰 | 編碼器溢位導致行程死結、麥克風拔除引發 Unhandled Exception 墜機 | **禁止發布 Stable 正式版** |
| **S2 (主要缺陷)** | 核心錄影運作但特定情境異常 | 特定 DPI 下錄影範圍偏移、Remux 在特定磁碟格式失敗 | 記錄於 Known Issues，可發布 Beta |
| **S3 (次要缺陷)** | 不影響資料安全的介面或輔助問題 | 倒數動畫輕微掉幀、文字排版微調 | 排入後續迭代更新 |

---

## 2. MKV 容器防護原則 (Continuous Container Flushing)

1. **零延遲寫入**: 擷取之影音幀必須以流式 (Streaming) 寫入 MKV 封裝管道，禁止將大量數據長久滯留在記憶體中「等按下停止時再寫入」。
2. **EBML Cluster 獨立性**: 每個 Cluster 包含獨立時間基準。若系統突然斷電，最後一個未寫完的 Cluster 頂多損失 1~2 秒，在此之前的所有內容百分之百完整可播。
3. **唯讀保護**: 錄影停止或中斷時，`recording.mkv` 立即以唯讀模式保護，直到 Remux 轉出之 MP4 經檢驗無誤前，嚴禁任何程式碼刪除或覆寫該 MKV。

---

## 3. 當機修復機制 (Crash Recovery Engine)

### 工作目錄結構
```text
Recordings/
└── Sessions/
    └── 20260915_010000_A1B2C3/
        ├── session.json      (當前狀態: Interrupted / Recording / Completed)
        ├── recording.mkv     (安全工作影音檔)
        ├── recording.log     (該 Session 專屬結構化日誌)
        └── recovery.json     (若需要進一步修復之元資料)
```

### 救援流程
1. **啟動偵測**: 程式啟動時自動掃描 `Sessions/` 目錄，比對 `session.json` 中的 `RecordingState`。
2. **異常辨別**: 若狀態為 `Recording`、`Preparing`、`Interrupted` 或未標註 `Completed`，即認定為非正常結束之錄影工作階段。
3. **救援介面**: UI 主動跳出「偵測到未正常完成的錄影」，提供使用者以下選項：
   - **[安全轉換為 MP4]**: 調用 Stream Copy Remux 將現有 MKV 轉出為完整 MP4。
   - **[開啟原始工作檔目錄]**: 直接開啟資料夾檢視原始 MKV。
   - **[保留現狀]**: 不刪除任何檔案。
4. **絕對原則**: 任何救援操作均不得刪除原始 MKV 檔。

---

## 4. 磁碟容量耗盡防禦 (Disk Full Defense)

1. **錄影前檢測**: 目標路徑剩餘磁碟空間必須大於 1 GB 方可啟動錄影。
2. **錄影中週期監控 (每 2 秒)**:
   - **Warning 門檻 (剩餘 < 2 GB)**: UI 顯示橘色容量警示，記錄 Log，錄影照常進行。
   - **Critical 門檻 (剩餘 < 500 MB)**:
     - 觸發主動式安全停止 (Safe Shutdown)。
     - 立即向編碼管線發送停止訊號，寫入 MKV 結尾。
     - 更新 `session.json` 狀態為 `Interrupted (Disk Space Critical)`。
     - 避免將磁碟榨乾至 0 Byte 導致作業系統崩潰或檔案截斷損壞。
