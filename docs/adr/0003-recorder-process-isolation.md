# ADR 0003: UI 與 Recorder 行程分離 (Process Isolation) 與 IPC 機制

## 狀態 (Status)
已採納 (Accepted)

## 背景與問題脈絡 (Context)
在桌面應用程式中，UI 執行緒與底層高壓力的音視訊擷取/編碼執行緒若共存在同一個行程 (In-process)：
1. UI 渲染異常、XAML 引擎崩潰、未處理的 WPF/Avalonia 介面例外或使用者強制關閉視窗，會直接將正在進行的錄影行程一併終止。
2. 背景進行高解析度畫面擷取或硬體編碼時，若發生瞬時 GC 停頓 (GC Pause) 或記憶體壓力，可能連帶導致 UI 失去回應 (Not Responding)，讓使用者誤以為當機而強制 End Task。
3. AGENTS.md 規格書第 8 節與 Milestone 7 明確要求隔離驗證：「UI crash 不應必然導致 Recorder crash」。

## 決策 (Decision)
1. 採用行程隔離架構：
   - `ScreenRecorder.UI`: 負責顯示視窗、托盤圖示、自訂區域選取 Overlay、參數設定與狀態監控。
   - `ScreenRecorder.Recorder`: 獨立後台主機 (Headless Host Process)，負責實際管理 `IVideoCaptureService`、`IAudioCaptureService`、`IEncoder`、磁碟容量守護與 MKV 檔案寫入。
2. **處理程序間通訊 (IPC)**：
   - 採用 Windows/跨平台皆具備極高效率與穩定性的 **具名管道 (Named Pipe, `System.IO.Pipes`)**。
   - 通訊協議採用輕量、無狀態或狀態可重構之 JSON-RPC / NDJSON (Newline Delimited JSON) 雙向串流。
3. **容錯與心跳保護 (Heartbeat & Fault Tolerance)**：
   - UI 與 Recorder 之間每秒交換心跳 (Ping/Pong)。
   - 若 UI 行程崩潰或被使用者手動關閉：Recorder 偵測到 Pipe 斷開後，不立即退出，而是切換至「無介面背景錄影 (Detached Recording)」狀態，持續安全錄影，並將狀態更新至 `session.json`。
   - UI 重新啟動時：透過檢查本機活躍的 Recorder 行程與 Session 目錄，重新連回既有 Named Pipe 恢復操作介面。
   - 若 Recorder 發現系統資源極度匱乏或收到特定終止訊號，則執行內部 Safe Stop 並儲存資料。

## 替代方案評估 (Alternatives)
- **單一行程架構 (In-process)**：
  - 雖開發最簡單，但 UI 崩潰必然導致錄影毀滅，無法通過 Milestone 7 之核心驗收標準。
- **gRPC over HTTP/2**:
  - 依賴較重，桌面本機通訊引進額外通訊協定棧；Named Pipe 更為原生且極低延遲。

## 後續影響與風險 (Consequences & Risks)
- **優點**:
  - 達成極致的錄影可靠度；UI 當機或使用者誤按關閉視窗不會損及錄影資料。
- **複雜度**:
  - 需要維護 IPC 狀態同調與例外處理；需實作 Pipe 斷線重連機制。
