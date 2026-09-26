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
2. **中斷耐受性**: MKV 的分段與 Cluster 結構可提高中斷後保留已寫入內容的機會；最後尚未完成的區塊仍可能遺失或損壞，實際可救回範圍須由 ffprobe/Remux 結果判定，不承諾固定秒數或完整比例。
3. **保留原檔**: 在 MP4 建立並驗證成功前，程式不得刪除或覆寫 `segment_*.mkv`（舊版 Session 可能是 `recording.mkv`）。這是程式生命週期規則，不代表作業系統檔案屬性會被設成唯讀。

---

## 3. 當機修復機制 (Crash Recovery Engine)

### 工作目錄結構
```text
Recordings/
└── Sessions/
    └── 20260915_010000_A1B2C3/
        ├── session.json      (當前狀態: Interrupted / Recording / Completed)
        ├── segment_000.mkv   (目前格式的第一段工作影音檔)
        ├── segment_001.mkv   (暫停後繼續時可能新增)
        ├── recording.log     (該 Session 專屬結構化日誌)
        └── recovery.json     (若需要進一步修復之元資料)
```

### 救援流程
1. **啟動偵測**: 程式啟動時自動掃描 `Sessions/` 目錄，比對 `session.json` 中的 `RecordingState`。
2. **異常辨別**: 若狀態為 `Recording`、`Preparing`、`Interrupted` 或未標註 `Completed`，即認定為非正常結束之錄影工作階段。
3. **救援介面**: UI 啟動及儲存位置變更時掃描 Session，於狀態區提示可救援數量；使用者按「修復救援」後，程式以 Stream Copy Remux 嘗試把可讀分段封裝為 MP4。原始工作檔可直接由儲存位置下的 `Sessions` 目錄檢視，未按救援時維持原狀。
4. **保留原則**: 救援操作不刪除原始 MKV；可讀分段會重新封裝，損壞或空白尾段則保留並在結果中標示部分救回或失敗。

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

---

## 5. macOS 麥克風擷取隔離

- macOS 不直接使用 FFmpeg `avfoundation` 讀取麥克風，避免其已知的音訊樣本遺失與週期性斷音。
- `OpenCam.Microphone` 使用 Apple `AVAudioEngine` 取得輸入，轉為 48 kHz、16-bit、單聲道 PCM，再經使用者專用 FIFO 串流給 FFmpeg。
- helper 初始化或權限失敗時，錄影引擎保留畫面錄製並回退靜音軌；錯誤透過 structured log 與 `AudioDeviceLost` 通知上層。
- Stop 與 Dispose 均會終止 helper、關閉 FIFO 並刪除暫存目錄，避免背景行程與管道殘留。

## 6. UI 結束與錄影健康狀態

- 一般錄影中按下視窗關閉按鈕時，UI 會攔截關閉並提醒先停止錄影，不會中斷目前工作。
- 若 UI 被作業系統強制終止或異常消失，Recorder 監看父行程並執行安全停止：先結束目前 MKV、再依正常流程嘗試封裝，最後退出，避免無人知情的背景錄影。
- Recorder 健康狀態以實際檔案成長、影格進度、編碼器錯誤與所選音訊來源的即時樣本判定。剛開始錄影或證據不足時回報「未知」而非假設健康；只有連續無進度或明確錯誤才回報異常。
- 健康警告不取代工作檔救援。只要 MP4 尚未驗證成功，就應保留 Session 內的 MKV，並可從「修復救援」重新封裝。
