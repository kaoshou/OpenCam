# OpenCam 0.2.x 專案整頓設計

日期：2026-09-26
狀態：待使用者審閱

## 目標

本次工作修正目前已確認的可靠性、測試、相依安全、封裝相容性與文件一致性問題，但不加入 Apple Developer ID、notarization、Windows Authenticode 或其他付費簽章流程。

完成後，OpenCam 的自動化測試必須成為桌面封裝與 Release 的必要門檻；Recorder 回報的健康狀態必須源自可觀測資料；macOS 26 與 GitHub `macos-15` 都必須能建立應用程式圖示；正式專案不得再解析到目前已知的 High 級 `Tmds.DBus.Protocol` 弱點；文件只能宣稱有執行證據的結果。

## 範圍

### 納入

- 恢復目前被註解停用的六個 xUnit 測試並修正其暴露的回歸。
- 在 GitHub Actions 中加入 .NET、網站及適用的原生 helper 測試門檻。
- 讓 Release 封裝工作依賴測試成功。
- 以真實 Recorder 與 FFmpeg 狀態取代固定健康值。
- 將看門狗偵測結果帶入 telemetry，供 UI 呈現。
- 修補 `Tmds.DBus.Protocol` 傳遞相依弱點，並更新測試工具鏈。
- 修正 macOS 26 的 ICNS 產生相容性。
- 固定 Windows FFmpeg 來源版本並驗證 SHA-256。
- 集中產品版本來源，降低跨檔案版本不一致風險。
- 更新架構、Roadmap、驗收報告、測試策略、使用說明與人工驗收紀錄格式。

### 排除

- Apple Developer ID、notarization、Mac App Store 封裝。
- Windows Authenticode 或商業憑證。
- Linux 正式支援。
- UI 重啟後接管既有 Recorder 的新通訊協定。
- 新功能、影音編輯、串流或其他非整頓需求。

## 錄影行程生命週期

一般視窗關閉仍沿用現況：從使用者按下開始錄影起，直到錄影安全停止與完成收尾前，關閉視窗會被阻擋並顯示提醒，不能直接中斷錄影。

若 UI 行程非正常消失，Recorder 不留下無法控制的孤兒錄影。Recorder 偵測父行程結束後執行既有的安全停止流程：停止 FFmpeg、保留 MKV、嘗試驗證並 Remux、保存 Session 狀態，然後退出。架構文件與驗收項目必須描述此實際行為，不再宣稱 UI 消失後無限繼續錄影。

UI 重啟接管背景 Recorder 需要穩定的發現協定、所有權與停止權限，以及同版本相容性處理；本次不以半套方式加入。

## Recorder 健康模型

`RecorderTelemetry` 不得以固定常數表示健康狀態。健康資訊由 Recorder 內部維護，至少包含：

- FFmpeg 行程是否仍存活。
- 最近觀察到的影格數與錄影時間是否前進。
- 目前工作分段的檔案大小是否持續增長。
- 已選音訊來源是否仍可取得資料或已回報裝置錯誤。
- 看門狗最後一筆警告或引擎錯誤。

錄影開始時健康狀態初始化為尚未確認；收到第一個有效影格後才標記視訊與編碼器健康。檔案連續三個兩秒週期沒有增長時，標記視訊／編碼器健康異常並保存警告，但不僅因檔案增長遲滯便殺死 FFmpeg。若 FFmpeg 已退出、磁碟到達危急門檻或既有明確失敗事件發生，才執行安全停止。

`DroppedFrames` 只有在能從 FFmpeg 進度或明確計數器取得時才提供真實數值；尚無資料時使用可辨識的「未知」語意，不得用零冒充已確認無丟幀。若現有資料模型無法表達未知，新增 nullable 欄位或伴隨的可用性欄位，並維持 IPC JSON 向後相容。

音訊健康由已選來源分別判定。未選的來源不應被標示為故障；選取但未收到新鮮 level、helper 退出或觸發裝置遺失時，標記為異常或不可用。波形顯示錯誤不得影響實際錄音管線。

UI 在狀態監控區顯示 Recorder 回傳的健康警告，並保留磁碟剩餘空間與音訊波形。短暫 IPC 失敗仍使用既有連續失敗門檻，避免瞬間延遲造成誤判。

## 測試與 CI 門檻

### 單元與整合測試

- 恢復六個被停用的 `[Fact]`，逐一執行紅綠循環。
- 新增 telemetry 測試，證明未啟動、正常錄影、FFmpeg 結束、檔案遲滯及音訊遺失不會回傳固定健康值。
- 新增看門狗警告可由 telemetry 觀察的測試。
- 保留 macOS 原生系統音訊 helper 測試。
- 真實麥克風、螢幕擷取、多螢幕、拔插、休眠與長時間錄影仍屬人工或明確 opt-in 測試，不在無硬體的 Runner 上假造成功。

### GitHub Actions

新增獨立驗證工作，至少在 Ubuntu 執行跨平台 .NET 測試與網站測試；在 Windows 執行 Windows 可用測試；在 macOS 執行 macOS 適用測試、原生 helper 測試與 ICNS 建立測試。Windows 與 macOS 封裝工作必須 `needs` 驗證工作，Release 工作再依賴兩個封裝工作。

NuGet 弱點檢查要成為 CI 步驟，但只阻擋正式產品相依中的 High 或 Critical 弱點。測試工具鏈本身也要升級，使完整方案稽核不再回報目前已知的舊 System 套件弱點。

## 相依安全策略

目前弱點路徑為：

```text
Avalonia.Desktop 11.2.5
→ Avalonia.X11 11.2.5
→ Avalonia.FreeDesktop 11.2.5
→ Tmds.DBus.Protocol 0.20.0
```

優先採用最小相容修補：解析到官方已修補且與現有 Avalonia 11 相容的 `Tmds.DBus.Protocol` 版本，並以 `dotnet list package --vulnerable --include-transitive` 驗證。若直接版本約束造成還原、編譯或執行相容問題，再升級至最新相容的 Avalonia 11.x；不在本次直接跨到 Avalonia 12 大版本。

測試專案更新 `Microsoft.NET.Test.Sdk`、xUnit runner 與 coverage collector 到可相容版本，避免舊 TestHost 帶入有弱點的 .NET Standard 1.6 相依。升級後必須維持測試探索與執行數量，不得以略過測試換取乾淨稽核。

## macOS 圖示封裝

先以保留暫存 iconset 的診斷測試確認 macOS 26 `iconutil` 拒絕的實際檔案格式或 metadata。修正必須仍由單一 1024×1024 RGBA PNG 產生標準十尺寸 iconset，並在 macOS 26 本機與 `macos-15` Runner 驗證。

若 `sips` 在 macOS 26 產生不被新版 `iconutil` 接受的 PNG，改用可重現的原生影像轉換工具輸出標準 RGBA PNG；不提交手工二進位 ICNS 作為唯一來源。測試需驗證所有尺寸、PNG 格式及最終 `icns` magic bytes。

## Windows FFmpeg 可重現性

Windows 工作流程不得下載 `latest`。來源改為明確 release URL，並在解壓前驗證固定 SHA-256。版本與 checksum 放在工作流程容易更新的單一區塊；檔名與資料夾結構不得依賴遠端 `latest` 別名。

macOS 現有固定 FFmpeg／x264 來源與 checksum 保持不變。

## 版本單一來源

產品版本以 repository-level MSBuild property 作為主要來源，UI assembly、安裝程式、macOS bundle 與 Release notice 從同一值讀取或在 CI 中明確驗證一致。使用者可見語系文字改由 assembly/package version 組合，不在字典與 XAML 重複寫死。

GitHub Pages 可以顯示目前 Release 版本，但下載連結維持 `/releases` 或 `/releases/latest`，不綁死安裝檔名稱。

## 文件與證據

- `ARCHITECTURE.md`：記錄 UI 消失時安全停止，而非持續錄影。
- `ACCEPTANCE_REPORT.md`：移除過期的「0 警告、45 項全數通過」，改成有日期、commit、平台與命令的實際結果。
- `ROADMAP.md`：已完成項目以證據勾選；長時間、睡眠、拔插等未執行項目保持未完成。
- `MANUAL_TEST_CHECKLIST.md`：每個案例新增版本、日期、設備、執行者、結果與證據欄位。
- 中英文使用說明：更新適用版本與實際 UI/Recorder 關閉行為。

不得把合成來源、自動化 fixture 或未執行的人工測試寫成真實硬體 PASS。

## 錯誤處理與可觀測性

本次涉及的錄影、Session 保存、看門狗、IPC 與音訊健康路徑不得新增空白 `catch`。現有空白 `catch` 若會隱藏錄影資料、Session 狀態或使用者可操作錯誤，改為具名例外與結構化 Log；純清理且無法補救的例外可保留 best-effort 行為，但必須留下 Debug 或 Warning 記錄。

## 驗收標準

1. `dotnet build ScreenRecorder.sln -c Release` 零錯誤，且不再有六個 `xUnit1013` 警告。
2. Core、Media/UI、網站與 macOS 原生 helper 自動化測試全部通過；硬體限定測試以明確 skip 原因列出。
3. 正式專案 NuGet 稽核不再回報 `Tmds.DBus.Protocol 0.20.0` High 弱點；完整方案不再回報目前兩個測試工具鏈 High 弱點。
4. macOS 26 本機成功產生有效 ICNS；GitHub `macos-15` 封裝流程仍通過。
5. Recorder telemetry 測試證明健康值會隨真實狀態改變，檔案遲滯警告可被 UI 取得。
6. Windows FFmpeg URL 與 checksum 固定，checksum 不符時工作流程明確失敗。
7. Release 工作只有在驗證與兩平台封裝皆成功時才能執行。
8. 文件、版本與實際行為一致，未驗證項目不宣稱 PASS。
9. 簽章與 notarization 維持排除，最終回報明確列為已知限制。
