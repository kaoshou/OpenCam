# 測試策略與驗證規範 (Testing Strategy)

本專案將「錄影可靠性」視為最高指導原則，測試工作不能僅止於「按下開始錄影可以錄製」，必須建立**故障注入 (Fault Injection)**、**邊界壓力測試 (Stress Testing)** 與**長時間自動化巡檢**。

本計畫於 2026-10-17 修訂，適用於 0.3.8 之後的測試維護。目標是讓功能正確性不依賴 hosted runner 恰好在某個時間點完成工作，並保留真正的錄影、存盤、取消與安全驗收。

**實施狀態：三平台 .NET TRX、版本清單與失敗時證據上傳已推送，並已在 hosted runner 取得證據。** 心跳／影格的事件式改寫、完整測試分層、工具釘選及 PR 分工仍待落實。目前三個平台仍執行完整 solution 測試，平台驗證成功才可打包；在替代測試覆蓋確認前，不刪除、停用或降級既有阻擋條件。

本機通過與人工功能正常是判讀證據，不會自動把 CI 失敗改成 PASS；反之，CI 失敗也不能只憑一個時間斷言就認定產品功能失效。先判斷失敗層級、收集階段證據，再決定修正測試或產品。0.3.8 的實例見 [發布驗證紀錄](docs/verification/2026-10-17-v038-release-preparation.md)。

錄影專案可定向執行 Core 的 `RecordingProjectTests`、`ProjectStoreTests`、`ProjectPathSecurityTests`，以及 Media 的 `--filter FullyQualifiedName~Project`。跨程序測試使用 `ScreenRecorder.ProjectRecordingProbe` 與真正 FFmpeg 合成素材；不是實體螢幕／麥克風驗收。[Phase B 驗收](docs/acceptance/2026-10-08-project-phase-b.md) 是該階段的歷史證據，不能直接當成本版結果。

---

## 1. 測試金字塔與分類

### GIF／片段匯出（開發分支）

Core `ProjectExportSelectionTests` 驗證保留範圍、合併／群組語意、選項與指紋。Media 的 `ProjectGifMediaTests`、`ProjectScopedExportTests`、`ProjectGifExportIntegrationTests` 使用真實 FFmpeg／綁定檔案，驗證累積 GIF 時間、無音訊、合併片段顏色順序、MP4 聲音／靜音、取消、故障清理與目錄替換。`ProjectExportMenuTests`、`ProjectGifExportDialogTests` 驗證兩個整片入口、原生階層選單結構、五分鐘邊界、選項、過期版本及預覽停止後版本競態。

定向命令：`dotnet test tests/ScreenRecorder.Media.Tests -c Release --filter "FullyQualifiedName~ProjectGif|FullyQualifiedName~ProjectScopedExport|FullyQualifiedName~ProjectExportMenuTests"`。FFmpeg 必須可在 PATH 找到；目前真實媒體新測試使用 macOS process adapter，不能把其他平台略過當成 Windows 通過。仍須跑完整 solution 回歸；GUI hover／鍵盤、高 DPI、原生 GIF 播放器與長片資源量測依人工清單驗收。見 [本輪證據與限制](docs/verification/2026-10-21-gif-and-clip-export.md)。

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

核心邏輯應使用可控制輸入、時間與完成順序的測試；真實檔案、FFmpeg、管道與裝置的行為另由整合／實機測試驗證。依實際依賴分類，不以專案名稱分類：例如 Core 測試專案中的真實磁碟 `SessionStoreTests` 仍屬存盤整合測試。

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

- CI 會在可攜、Windows 與 macOS 工作上執行建置／測試；macOS 工作另驗證網站、App icon 與原生音訊 helper；可攜工作在封裝前阻擋 High/Critical NuGet 弱點。
- Windows FFmpeg 下載使用固定版本 URL 與 SHA-256，避免浮動最新版改變封裝內容。
- 自動化測試只能證明受測程式路徑；實體多螢幕、DPI、音訊設備拔插、鎖定、休眠與長時間錄影仍須依 `MANUAL_TEST_CHECKLIST.md` 留存人工證據，未執行不得標示 `PASS`。

## 7. CI 測試分層與執行時機（待落實）

| 層級 | 要證明什麼 | 執行方式 | 執行時機與失敗處理 |
| --- | --- | --- | --- |
| 可控制的邏輯測試 | 狀態、重試順序、取消、音源選擇、時間計算、權限／路徑拒絕 | 可控時鐘、排程器與完成事件；不等待真實秒數或依賴裝置 | 開發分支／PR 的必要檢查；失敗需修正，不能用本機功能正常抵銷 |
| 真實短程整合 | FFmpeg 輸出內容、JSON 存盤、跨程序 IPC、4K 資料完整性、程序收尾 | 小型合成素材、真實檔案／程序／管道，事件式同步與明確整體期限 | 相關變更及發布候選的三平台檢查；保留失敗原因，未釐清者不能直接忽略 |
| 效能與壓力 | 初始化、存盤、控制回應及影格傳輸延遲分布；負載下資源使用 | 記錄環境與各階段耗時，區分正常與刻意施壓條件 | 相關變更、排程測試與發布前；產品規定的時間要求或安全停機要求仍是阻擋條件 |
| 原生實機驗收 | 真實範圍、兩路收音、DPI、裝置拔插、浮窗排除、長時間 A/V | 同提交安裝包與實體 Windows／macOS，保存影片與 Log | 依變更範圍及 Release Gate 執行；缺裝置／權限為 BLOCKED，不是 PASS |

同一行為可以有多層測試：可控測試證明「下一次 tick 會重試」，整合測試證明「真的能寫入並讀回」，效能測試量測「花了多久」。不可用假資料取代整合驗證，也不可把資料毀損或卡住重新分類為單純效能波動。

目前 Media 測試已停用組件內平行執行，因為語言設定、環境變數、日誌與 OS 資源共用。分層時維持此限制；必須先隔離共享狀態，才評估平行化。不要為了加速而引入新的競態。

Core 中會啟動子程序的 `ProjectStoreTests` 使用非平行 collection，不能與其他 collection 的原生鎖釋放／立即重開測試同時執行。2026-10-20 的有界 A/B 實測確認，同程序另一執行緒的程序建立會暫時干擾 Unix `flock` 重取；只在此程序建立邊界隔離，不替產品刪鎖、不加 sleep／重試、不取消任何跨程序或復原斷言。[證據與界線](docs/verification/2026-10-20-heartbeat-delay-observation.md)。

## 8. 兩項優先改寫案例

### 8.1 心跳與實際存盤

現有 `SessionHeartbeat_ContinuesWhileRecordingAndPaused` 每 2 秒更新，測試固定等 2500ms 再讀檔；這混合了心跳邏輯、排程及磁碟完成時間。0.3.8 曾發生第一個 2 秒等待正常，但持久化尚未更新；沒有證據可把原因直接歸給 runner。

改寫順序：

1. 將 tick／時間來源設為可注入，正式環境仍使用現有 2 秒間隔。邏輯測試以可控時間推進，確認第一次更新、暫停時仍更新、失敗後下一輪重試、取消後不再寫入。
2. 用可控制完成的存盤邊界驗證「寫入尚未完成」及「寫入失敗」不算成功；不要以 `CurrentSession.LastHeartbeatTime` 的記憶體變化代替實際完成。
3. 真實 JSON／錄影整合測試在正式錄影仍持續時，等待指定新心跳的存盤完成訊號，再重新開檔核對時間及狀態。完成訊號只能在真實存盤成功後發出；不得由 Stop／清理補寫，亦不能僅斷言測試替身被呼叫。
4. 對整個整合操作保留取消與絕對期限，另外記錄 tick、gate 等待、寫入、flush／替換、讀回各階段耗時。不能改成無限等待。
5. 「2 秒排程」與「2.5 秒內必須完成持久化」須明確區分。若後者是產品要求，保留獨立時效驗收並修正產品瓶頸；若只是舊測試猜測的完成時間，先以量測說明調整依據，再改為事件式判斷。不可只增加 sleep 或期限直到變綠。

通過標準：可控時間下每個重試／取消／暫停分支都正確；真實存盤可在錄影中被讀回；存盤失敗、過期記憶體值、Stop 補寫或取消後寫入，都不能讓測試誤過。既有 20 秒重試上限在有證據決定調整前維持不變。

### 8.2 慢速讀取、控制指令與下一張影格

現有 `SlowMediaReaderDoesNotBlockStopAndTransfersAreSerialized` 同時測試 4K RGBA、第一個慢讀連線、控制停止、第二個排隊傳輸。0.3.8 曾只有第二張回覆失敗，不能據此認定停止指令失效或直接推論為 timeout。

改寫順序：

1. 用明確的「第一個傳輸已占用位置」及「允許釋放」訊號協調測試；確認控制指令在釋放前就能處理，第二個媒體 handler 在釋放前不能進入。
2. 真實連線關閉／取消仍要保留實測，驗證占用位置被釋放、下一張能成功接收，沒有程序、連線或工作遺留。
3. 將上述控制／序列化驗證與大型傳輸效能量測分開；保留獨立的真實 4K 完整傳輸與逐位元組內容核對，不能縮小畫面或少讀資料來取得 PASS。
4. 保存第二張的 `Error`、認證／排隊／handler／傳輸階段、位元組數、開始與完成／取消時間、handler 次數。傳輸錯誤不得只留下 `Assert.True(false)`。
5. 目前整體 8 秒、控制指令 500ms、4K 回覆 6 秒與媒體用戶端 12 秒來自不同層，不可直接相加或任意增大。先量測哪段耗時、哪個期限觸發；若 500ms 是產品回應要求，仍保留獨立驗收。

通過標準：控制通道不被媒體占用、媒體 handler 保持序列化、放棄連線後可恢復傳輸、4K 資料內容與認證均正確，且所有等待有界。任何驗證、大小上限、取消或資料完整性保護不得放寬。

## 9. 失敗分類、證據與重跑規則

| 判讀 | 必要證據 | 處理 |
| --- | --- | --- |
| 產品缺陷 | 錯誤內容／狀態、資料遺失、無法取消、未釋放資源、已定位的程式競態 | 修產品並加對應回歸；資料／安全問題持續阻擋發布 |
| 測試同步缺陷 | 證明測試觀察了錯誤時點、替身、共享狀態或平台實作；有保留原契約的替代測試 | 修正測試設計，保留原驗收責任，跑相關回歸後重新驗證 |
| 環境／工具問題 | 例如下載失敗、明確缺少工具／權限，或可確認的 runner 事故 | 保存環境及階段證據；修復環境後重跑同提交。缺裝置標 BLOCKED／SKIPPED |
| 未分類／偶發失敗 | 只知道偶爾失敗，尚無足夠診斷 | 保留 FAIL 並調查；本機通過、人工可用或下一輪綠燈，都不構成根因結論 |

三平台 .NET test job 已在工作分支加入 `test-evidence-portable-*`、`test-evidence-windows-*`、`test-evidence-macos-*`，以 `always()` 上傳 `test-results/`、保留 7 天。包含 TRX（失敗斷言內的既有有界診斷也在其中）及 `environment.json`；蒐集工具每個版本命令限 10 秒、每路輸出限 16 KiB，失敗仍寫報告並保留非零結果。這些是測試證據，不是安裝包，也不附加到正式 Release。

目標保存清單如下；TRX／工具版本已接線，其他階段與資源收尾量測仍待後續實作：

- TRX 結果：通過、失敗、略過數量及完整案例名稱。
- 原始失敗的例外／階段／耗時、有限長度日誌，必要時只收 managed stacks。不能記錄金鑰、環境變數全集、私人畫面或原生音訊內容。
- Commit、runner OS／映像版本、架構／CPU 數、.NET SDK／Runtime、FFmpeg／ffprobe 實際版本、測試指令與參數。
- 執行前後的測試自有子程序／資源收尾結果；不終止其他任務的程序。

本機可執行 `node scripts/collect-test-environment.mjs /tmp/opencam-test-environment.json` 產生同格式清單，及 `node --test tests/ci/*.test.mjs` 驗證蒐集工具。只讀取指定 runner 欄位和已知版本命令，不傾印環境變數全集；版本命令不存在、卡住或失敗都不回報成功。

預設不自動重跑失敗測試、不設 `continue-on-error`、不改 assertion 讓 CI 變綠。確認原因後可做同提交的一次診斷性重跑，但第一次失敗仍須保留並說明；重跑成功不叫「已修復」。已知偶發案例建立追蹤紀錄與退出條件，不能無期限隔離、刪除或讓它消失於發布報告。

## 10. 執行順序與完成條件

1. **先補證據與環境清單。** 在三平台 test job 保存 TRX／有界日誌及實際工具版本；Windows 固定 FFmpeg URL／SHA256 已存在，繼續沿用。選定已驗證 SDK／套件版本與更新節奏，改善目前 `8.0.x`、Linux 套件來源及 macOS `brew install ffmpeg` 的可重現性。釘選不是永久停更，弱點檢查仍執行；runner 標籤也不能保證映像永遠不變。
2. **只先改寫第 8 節兩個案例。** 保留舊測試的行為責任並逐項對照，可控時間測試不依賴真實秒數；真實資料、存盤、4K、取消及控制隔離測試仍存在。使用正常與延遲完成、失敗、取消情境驗證新設計會抓到缺陷。
3. **驗證穩定性後才調整 CI 分工。** 計畫先要求同一提交在各適用平台連續 3 次通過，不能「失敗就重新計算三次」。任一次失敗均回到原因分類，保留全部結果。這是測試改寫的有限確認，不是每次發布都跑三遍，也不保證從此沒有偶發問題。
4. **再導入開發分支／PR 檢查與效能工作。** 日常可先跑相關檢查，發布候選仍保留完整 solution 與三平台短程整合。效能／壓力工作須有明確負載、期限與結果處理規則，不能以分類名義移走 Critical gate。現有 Actions 依賴與標籤發布條件在本計畫落實前維持原樣。

完成報告應列出：測試程式／workflow 變更、原驗收責任對照、同提交本機與三平台結果、首次失敗及重跑記錄、工具版本、仍未驗證的實機項目。只有計畫文件更新不能宣稱 CI 已改善。

## 11. 本機與 CI 的對照方式

在同一提交、相同 SDK／FFmpeg 版本與相同設定下比較，保存完整輸出。定向命令用於開發定位，不能代替正式發布的完整驗證。

```bash
dotnet --info
ffmpeg -version
ffprobe -version
dotnet restore ScreenRecorder.sln
dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false
dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false
```

心跳與媒體定位可使用目前已有案例：

```bash
dotnet test tests/ScreenRecorder.Media.Tests -c Release --no-restore -m:1 /nodeReuse:false --filter FullyQualifiedName~SessionHeartbeat
dotnet test tests/ScreenRecorder.Media.Tests -c Release --no-restore -m:1 /nodeReuse:false --filter FullyQualifiedName~SlowMediaReaderDoesNotBlockStopAndTransfersAreSerialized
```

目前尚未加入分層 Trait／新的 workflow filter，不能把本文件中的分類當成已可使用的命令。環境不支援的原生案例依既有條件略過並記錄原因；不變更條件以規避失敗。實機復測見 [手動驗收表](MANUAL_TEST_CHECKLIST.md)。
