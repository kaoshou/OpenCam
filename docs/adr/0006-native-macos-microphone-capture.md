# ADR 0006：macOS 使用原生麥克風擷取 helper

## Context

macOS 版本原先由 FFmpeg 的 `avfoundation` input 直接讀取內建麥克風。實測成品會反覆出現約 106.7 ms（5,120 個 48 kHz 樣本）的固定靜音塊；MP4 音訊封包時間戳連續，證明問題發生在擷取階段而非 Remux。FFmpeg 的 AVFoundation 音訊輸入亦有已知的遺失樣本問題。

## Decision

新增獨立的 `OpenCam.Microphone` arm64 helper：

1. 使用 Apple `AVAudioEngine` 擷取目前的 macOS 麥克風輸入。
2. 使用 `AVAudioConverter` 正規化為 48 kHz、16-bit、單聲道 PCM。
3. 經權限為使用者讀寫的 Unix FIFO 傳給 FFmpeg。
4. 透過 Core 的 `IMicrophoneCapture` 管理 helper 啟停、錯誤事件及資源釋放。
5. helper 不可用時回退靜音軌，保留畫面錄製及 MKV 資料安全。

Windows 的 DirectShow/WASAPI 路徑不變。

## Alternatives

- **保留 FFmpeg AVFoundation，調整 queue 或 bitrate**：無法修正 AVFoundation demuxer 自身遺失樣本，且測得 queue 已為 1,024。
- **只移除 `aresample=async=1`**：只會把靜音補償改成時間戳跳躍或 A/V 失步，沒有修復遺失的來源資料。
- **引入 SoX 或其他第三方 capture binary**：增加發行依賴、權限與簽署面積，不如使用 macOS 原生 API。

## Consequences

- macOS 麥克風資料不再經過 FFmpeg AVFoundation input。
- App bundle 新增 `OpenCam.Microphone`，打包驗證會檢查其 Mach-O arm64 格式與執行權限。
- 麥克風與系統聲音可各自使用獨立 PCM FIFO，再由 FFmpeg 進行同步與混音。
- 新 helper 的生命週期及錯誤必須由錄影引擎管理。

## Risks

- 麥克風權限仍需使用者授權；拒絕時只能安全回退靜音。
- 實體裝置切換與拔除必須以人工硬體測試驗證。
- 長時間 A/V drift、睡眠喚醒與高負載仍需依 `MANUAL_TEST_CHECKLIST.md` 執行實機驗收。
