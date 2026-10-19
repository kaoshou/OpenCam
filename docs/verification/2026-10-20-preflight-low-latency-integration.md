# 即時預檢低延遲整合

接續使用者「繼續調整」，將已量測的 CPU／封裝方案接入預檢。研究基準與限制見 [前一輪量測](2026-10-20-preflight-option-latency.md)。不升版、推送或發布；不動先前尚未提交的設定開關與 CI 工作，不派額外審查代理。

## 產品變更

- FFmpeg 引擎新增明確的 `livePreviewOutput` 建構選項，預設 `false`。預覽模式必須有 bound stream，否則在建立工作目錄前拒絕開始。
- `Program` 只在即時預檢 factory 呼叫中傳 `livePreview: true`。五秒試錄 factory 保持預設值，正式錄影與受保護的串流檔案輸出也保持原模式。不能用 `OutputStream != null` 推斷預覽，因為正式錄影同樣使用此機制。
- 平台介面新增預覽專用輸出方法，預設回到原輸出方法以相容既有 provider。Windows／macOS 各自產生參數：CPU x264 加 `-tune zerolatency -bf 0`，預覽的 MKV `cluster_time_limit` 從 1000 改 100ms。
- NVENC／QSV／AMF／VideoToolbox 保留選定的編碼器及全部專屬選項，只縮短預覽封裝；不把 x264 tune 套在硬體上，不強制改 CPU，也不改試錄／正式輸出的品質、音訊設定與 1000ms cluster。
- 未加入測量中無獨立收益的解碼 probe 參數；解碼仍發布最新的完整 960×540 RGBA，4 FPS／UI 250ms 輪詢與單請求流程不變。本輪聚焦上游緩衝，不宣稱高 FPS 或消除所有延遲。

## 測試證據

- 參數 RED：先保留預覽介面的原輸出 fallback，12 案例全部失敗，原因為缺少 CPU 低延遲參數及 100ms 封裝，`/tmp/opencam-preflight-encoding-red.log`；實作後 12 通過，`/tmp/opencam-preflight-encoding-green.log`。
- 模式 RED：引擎尚未使用 opt-in 時，預覽子程序拒絕收到的普通錄影參數，以及預覽誤用檔案輸出各失敗；普通 bound stream 案例通過。2 fail／1 pass，`/tmp/opencam-preflight-mode-red.log`。
- 整合 GREEN：23 通過，`/tmp/opencam-preflight-mode-green.log`。包含兩平台參數 × 四音源、四硬體編碼器保留、真正 FFmpeg 合成預覽 × 四音源、模式隔離與不建立預覽錄影目錄。影格核對尺寸／RGBA 完整性、非黑色 RGB、停止後不再可讀；不是原生桌面／麥克風驗收。
- 不新增嚴格毫秒效能 gate、不放寬既有期限／斷言、不改取消／停機／租約保護，也不自動重跑失敗取得綠燈。
- 完整 Release build／test：Core 249、Media 849 通過，20 項依原平台／實機條件略過、0 失敗、exit 0，`/tmp/opencam-preflight-low-latency-full.log`；Media 4 分 31 秒。沒有把缺少裝置的案例當作 PASS，SDK 8.0.425／FFmpeg 7.1.2，既有警告與原生限制保留。
- 最後只強化測試的 RGB 檢查、排除 alpha=255 讓黑畫面誤過的可能；23 項定向再通過，`/tmp/opencam-preflight-low-latency-final.log`。產品程式未再變更，未重複執行完整套件。版本一致性及 `git diff --check` 通過；尚未 Windows hosted CI／原生實機驗收。

## 實機確認（REQUIRES MANUAL VALIDATION）

Windows 與 macOS 用計時器／閃爍標記，分別確認螢幕及自訂範圍預覽較貼近現在；覆蓋 CPU、可用硬體編碼器與四種音源。接著五秒試錄／回放並開始正式錄製，核對實際影像、收音及選定編碼器；取消、重查、裝置變更及多螢幕 DPI 都仍需原生驗收。

前輪約 1.62s → 0.55s 是無音訊合成小圖的 encoder-to-decoder 候選量測，不包含 UI／IPC 或原生擷取，也不是本輪 Windows 硬體／端到端成績。此整合不以原型數字保證實機速度。
