# ADR 0005: FFmpeg 管線編碼與無損封裝 (Stream Copy Remux)

## 狀態 (Status)
已採納 (Accepted)

## 背景與問題脈絡 (Context)
螢幕錄影的影音串流需要高效編碼為 H.264 與 AAC/Opus，並連續寫入檔案。
常見方式包括：
1. **直接調用 FFmpeg C API (FFmpeg.AutoGen 等 P/Invoke)**：
   - 效能高，但在 C# 與 C 原生記憶體互操作時，若有未處理指標錯誤容易引發 Access Violation Exception，且難以隔離。
2. **啟動獨立 FFmpeg 行程並透過標準輸入輸出 (Standard Input / Pipe) 傳輸原始數據**：
   - 行程獨立於 .NET Runtime，若編碼引擎發生意外崩潰，可被 C# 主機偵測到並觸發應急救援機制。
   - 參數調整彈性高，未來更換硬體加速 (NVENC, QSV, AMF) 僅需調整命令參數，不需重新編譯原生動態庫。
   - 跨平台支援度極佳。

錄影完成後轉換為 MP4：
- 若在錄影後「重新編碼 (Re-encode)」，不僅需要花費數倍錄影時間的大量 CPU/GPU 算力，還會導致畫質損失與失真。

## 決策 (Decision)
1. **編碼管線 (Encoding Pipeline)**：
   - 採用抽象介面 `IEncoder`，第一階段透過 `FFmpeg` 程序以管線方式接收 Raw Video/Audio Frames 輸出至 `recording.mkv`。
   - 嚴密監控 FFmpeg 程序的標準錯誤輸出 (stderr) 與行程存活狀態，若意外終止，State Machine 立即切換為 `Interrupted` 並啟動修復。
2. **無損封裝轉換 (Stream Copy Remux)**：
   - 錄影正常停止並關閉 MKV 檔後，執行指令：
     ```bash
     ffmpeg -i "recording.mkv" -c copy -movflags +faststart "recording.mp4"
     ```
   - `-c copy` 執行串流複製，零畫質損失、不重新運算像素、速度極快（受限於磁碟 I/O，通常僅需數秒）。
   - 加入 `-movflags +faststart`，將 moov atom 移至 MP4 前端，方便網頁與本機快速開始播放。
3. **安全驗收與防刪除保護**：
   - Remux 完畢後使用 `ffprobe` 檢測 MP4 檔是否存在且 Duration/Stream 合法。
   - 唯有驗證通過後，才依使用者設定歸檔或刪除 MKV；若 Remux 過程有任何異常，絕對保留原始 MKV 檔。

## 後續影響與風險 (Consequences & Risks)
- 系統需具備 FFmpeg / ffprobe 執行檔。我們將設計自帶 (bundled) 或系統路徑偵測機制。
