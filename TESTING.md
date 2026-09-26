# 測試策略與驗證規範 (Testing Strategy)

本專案將「錄影可靠性」視為最高指導原則，測試工作不能僅止於「按下開始錄影可以錄製」，必須建立**故障注入 (Fault Injection)**、**邊界壓力測試 (Stress Testing)** 與**長時間自動化巡檢**。

---

## 1. 測試金字塔與分類

```text
               ▲
              / \
             /   \  手動硬體驗收測試 (Manual Acceptance Test)
            /-----\ (麥克風拔除、螢幕熱拔插、休眠喚醒、Lock/Unlock)
           /       \
          /---------\ 故障注入與整合測試 (Integration & Fault Injection)
         /           \ (Kill UI、Kill FFmpeg、磁碟容量截斷、極限負載)
        /-------------\
       /               \ 單元測試 (Unit Tests)
      /_________________\ (State Machine、Session 持久化、Remux、磁碟閾值)
```

---

## 2. 自動化單元測試 (Unit Tests)

Core 核心邏輯必須具備 100% 可重複驗證的單元測試：
1. **RecordingStateMachineTests**:
   - 驗證合法的生命週期轉移 (`Idle` → `Preparing` → `Recording` → `Stopping` → `Finalizing` → `Completed`)。
   - 驗證非法狀態轉移被拒絕或安全轉換（例如 `Idle` 下呼叫 `Stop()` 應無效或拋出安全例外）。
   - 驗證異常觸發時由任意狀態切換至 `Interrupted` 或 `Failed`。
2. **SessionStoreTests**:
   - 驗證 `session.json` 的建立、序列化、更新與完整性檢查。
   - 驗證在 `session.json` 損壞時的備份讀取與還原。
3. **DiskThresholdTests**:
   - 驗證當可用空間低於 Warning 閾值 (預設 2GB) 與 Critical 閾值 (預設 500MB) 時之警報與強制停機觸發。
4. **StorageManagerTests**:
   - 驗證工作檔案命名規則、工作階段資料夾結構建立與無衝突路徑生成。

---

## 3. 故障注入測試規範 (Fault Injection Tests)

| 測試編號 | 注入場景 | 預期系統反應 | 驗證標準 |
| :--- | :--- | :--- | :--- |
| **FI-01** | 錄影 5 分鐘後，強制殺死 `ScreenRecorder.UI` 行程 | Recorder 偵測父行程結束，安全停止目前錄影並退出 | MKV 被收尾保留；可封裝時產生可讀 MP4，否則可由修復救援處理，且不殘留 Recorder 行程 |
| **FI-02** | 錄影 5 分鐘後，強制殺死 `ScreenRecorder.Recorder` 行程 | MKV 檔截至崩潰前之 Cluster 保持完整 | 重新啟動後偵測到 Interrupted Session，ffprobe 驗證可讀 |
| **FI-03** | 錄影中強制終止背景 FFmpeg 編碼程序 | Recorder 立即感知管線中斷並切入 Interrupted | 日誌明確記錄 Encoder Lost，MKV 工作檔保留 |
| **FI-04** | 模擬磁碟可用空間跌入 Critical 門檻 | Recorder 主動發起 Graceful Stop | 檔案完整儲存，無磁碟滿載導致的零位元組檔案 |
| **FI-05** | 模擬 Remux 目標資料夾唯讀或損壞 | Remux 失敗，狀態轉為 Failed | 原始工作 MKV 檔嚴禁被刪除，提供重試途徑 |

---

## 4. 長時間穩定性與資源監控策略 (Milestone 11)

長時間測試須持續監控以下作業系統指標：
- **記憶體 (Working Set / Private Bytes)**: 每 10 秒取樣一次，繪製記憶體趨勢圖，連續 4 小時不得有線性向上之記憶體洩漏。
- **控制代碼與執行緒 (Handle & Thread Count)**: 確認 COM 物件、Pipe Stream 與 OS Handle 正確 Dispose。
- **丟幀率 (Dropped Frames)**: 當系統瞬間過載時，記錄丟失幀數與時間戳補償，確保音畫同步。

---

## 5. ffprobe 自動化檢驗標準 (Milestone 20)

每次錄影產生之成品（MKV 與 MP4）必須通過以下自動化腳本驗收：
```powershell
ffprobe -v error -show_entries format=duration,size,bit_rate -show_entries stream=codec_name,width,height,r_frame_rate,sample_rate -of json "recording.mp4"
```
檢查條件：
1. `duration` 與錄影計時器偏差小於 1.5 秒。
2. `video stream` 存在且解析度與 FPS 符合設定。
3. 若開啟音訊，`audio stream` 必須存在且 sample_rate 為 44100 或 48000 Hz。

## 6. 自動化與實體硬體的證據界線

- CI 會在可攜、Windows 與 macOS 工作上執行建置/測試；macOS 工作另驗證 App icon 與原生麥克風 helper，並在封裝前阻擋 High/Critical NuGet 弱點。
- Windows FFmpeg 下載使用固定版本 URL 與 SHA-256，避免浮動最新版改變封裝內容。
- 自動化測試只能證明受測程式路徑；實體多螢幕、DPI、音訊設備拔插、鎖定、休眠與長時間錄影仍須依 `MANUAL_TEST_CHECKLIST.md` 留存人工證據，未執行不得標示 `PASS`。
