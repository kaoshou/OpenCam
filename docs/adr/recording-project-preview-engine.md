# 錄影專案預覽引擎：選型尚未完成

更新：2026-10-09（Asia/Taipei）

## 狀態

未選定產品播放器，不新增播放器產品依賴。現有 `tests/ScreenRecorder.ProjectPreviewProbe` 是獨立診斷工具，不是原生編輯器預覽實作。

## 已量測部分

FFmpeg pipe 候選只讀取呼叫端提供的唯讀、可定位 stream，不重新開啟使用者專案中的任意檔案路徑。測試生成自己的短 MKV（320×180、30 FPS、非零 PTS），核對 requested PTS 與獨立以畫格序號解碼的 SHA-256 framehash。

已取得：PTS 2200 定位／畫格內容一致、連續 100 個定位請求取消舊請求、Stop 取消並等待子程序退出。十次短片定位最大 35.1ms。此數據不是兩小時專案的 p95，也不是產品效能承諾。

探針整體輸出 **NOT_READY / exit 2**：未實作 PCM 播放、A/V clock，未驗證預覽音訊停止；缺乏 VFR、60 FPS、長 GOP、長專案、來源替換和雙平台完整矩陣。不能據此允許錄影與預覽音訊交疊。

## 候選比較與未解門檻

### 2026-10-09：macOS 綁定 FD 與原生音訊裝置探針

新增 `--candidate ffmpeg-fd-macos --extended`，仍僅限獨立測試專案。以 `posix_spawn` 明確繼承已開啟唯讀檔案至 stdin，並採 CLOEXEC_DEFAULT；不經 shell、不重新開啟素材路徑。FFmpeg 使用 `fd:` 支援 regular-file seek，輸出限額 1 MiB、子程序限時 15 秒，取消後 kill/reap。來源移名後仍正確解碼，Stop 後來源 SHA-256 相同。

實測執行檔是本機 `artifacts/local-osx-arm64/ffmpeg`，回報版本 **7.1.2**，與 macOS 封裝腳本版本一致；不是只有 Homebrew 9.0.1 的結果，也不是新建的 Actions 產物認證。

- 非零 PTS、60 FPS、VFR、負 PTS：實際輸出 PTS 與獨立循序解碼的指定畫格 SHA-256 核對通過。
- 負 PTS 採 MPEG-TS 測資（timebase 1/90000、目標 -72000）；原先 Matroska 負 PTS 測資產生 N/A，已排除，不能當有效證據。
- 一般 seek：關閉 FFmpeg 自動精準丟格，保留來源時間戳，由明確 PTS filter 決定畫格；否則非零起點短片曾得到空輸出。負時間戳不使用不受支援的 demuxer 快速定位，改從前綴解碼。
- 兩小時測資：64×36、30 FPS、GOP 300，尾端 7199.2 秒 20 次定位 p95 **12.4ms**。這是低解析度索引／定位機制量測，**不是 1080p、200 片段、原生 UI 的效能驗收**。
- `NativeAudioProbe.swift` 在真實預設輸出裝置播放低音量合成 PCM，確認非零輸出樣本與 sample clock 前進；呼叫 player.stop + engine.stop 後觀察樣本不再增加，重新播放及再次停止通過。沒有啟用麥克風或錄製其他程式。

音訊探針依據 [Apple stop 文件](https://developer.apple.com/documentation/avfaudio/avaudioplayernode/stop())，停止 player 並不足以停止底層 engine；兩者皆停止。此結果**不是聲學回錄、跨片段 A/V 同步或續錄隔離驗收**。`IPreviewCandidate.PlayAsync` 仍未實作，探針總結果仍為 NOT_READY / exit 2。

未完成：Windows FD／PCM 後端與實機驗證、連續解碼與共用時鐘、來源替換／拒絕／壞檔完整矩陣、長專案 UI、接點閃光／點擊對齊。Native spawn 程式碼仍是測試候選，不可直接視為已審查的產品媒體啟動器。

| 候選 | 已有證據 | 仍缺少／限制 |
| --- | --- | --- |
| FFmpeg 子程序＋pipe stream | 短 MKV 定位與取消探針 | pipe 不可 seek，當前實作每次從來源頭解碼；長片效率未驗證。PCM sink、同步時鐘與連續跨段尚未實作 |
| FFmpeg 綁定 FD＋平台 PCM | macOS 7.1.2 精準畫格／取消／唯讀來源核對；獨立原生音訊停止測試 | 尚未組合為播放器；Windows FD／PCM、A/V 同步與長專案 UI 未驗證 |
| LibVLCSharp＋StreamMediaInput | macOS 真實連續解碼、綁定 stream、停止與來源雜湊核對 | 本輪 VFR／非零起點精準定位未通過，不採用為產品精剪播放器 |
| 平台播放器（AVPlayer／Media Foundation） | 本輪未實測 | 需確認原始 MKV、跨平台一致性、精準 PTS、取消與分段音畫；不能預先假定可直接使用 |

FFmpeg 官方說明 [pipe 與 fd 的 seek 差異](https://ffmpeg.org/ffmpeg-protocols.html#fd)：fd 對 regular file 可定位，pipe 不具同樣能力。但跨平台安全傳遞已綁定檔案描述元尚未在本專案實作，不能只改 URL 就宣稱已解決。

LibVLC [MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html) 的 Stop 等待媒體工作結束；[MediaInput API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.html) 提供受控輸入方向。是否能達到精準邊界、回呼可取消及可靠停止，仍需測試。

## 下一個必要交付

### 2026-10-09：LibVLC 隔離候選實測

僅診斷專案引用 LibVLCSharp 3.9.4；產品未新增此依賴。使用 VideoLAN 官方 VLC 3.0.23 arm64 DMG，以唯讀方式掛載，不安裝至 Applications、不修改系統信任設定。SHA-256 與官方檔案一致：`fc6fac08d87f538517d44aca0c5e7a244b67c8c4cb589bf478363a7315fd5e0d`。原生 plugins 位置只透過測試程序的 `VLC_PLUGIN_PATH` 指定。

測資將原始畫格序號編成影像內的八條二進位色帶；回呼讀實際像素，而非以播放器回報時間推定畫面正確。先開唯讀 stream，再移名並替換原始路徑，證明此播放輸入未重新解析替換檔。獨立 ffprobe 確認 VFR 1.500 秒與 offset 3.500 秒確有對應畫格。

- 一般 H.264 30／60 FPS：1.5 秒定位畫格 45／90、快速 100 次定位最後畫格 60／120 通過。
- VFR：要求 1.5 秒畫格 90，得到 83；要求 2 秒畫格 120，得到 113。等待 250ms 後仍相同。
- 起點為 2 秒：按來源時間要求 3.5 秒畫格 45，得到 38；4 秒要求 60，得到 53。不能加固定補償值掩蓋誤差。
- 一般、60 FPS、VFR 的原生音訊 buffer 計數有增加；offset 初始觀察期未增加。這不是聲學回錄或同步驗收。
- Stop 後 250ms 無新視訊回呼，來源 SHA-256 不變。跨片段聲畫同步、音訊時鐘、Windows、長專案介面尚未驗證。

結論：候選為 **FAIL／NOT_READY**，不接進正式剪輯預覽；保留探針供重現。時間軸／非破壞編輯互動可以獨立施工，但不因此宣稱預覽或完整編輯器完成。

以相同 fixture／量測規則補齊候選實驗，至少一個後端通过精準畫格、聲音停止、跨接點同步及原始來源保護門檻，才制定其產品接線計畫。若沒有後端通過，保留未完成狀態，不用假預覽或預先轉整片掩蓋問題。

### 2026-10-09：已解碼影音與真實裝置時鐘的整合子測試

新增隔離候選 `--candidate ffmpeg-integrated-macos --ffmpeg … --audio-helper …`。
只在診斷中預解碼兩段各 0.8 秒、64×36、30 FPS 的自產測資：
48 個 RGBA payload 均核對獨立 framehash，兩段 PCM 共 76,800 個樣本，
第一段晚起始的音訊保留 0.2 秒偏移，來源雜湊不變。

原生 AVAudioEngine 播放上述實際解碼 PCM，影格 packet 依
`playerTime(forNodeTime:)` 的 sample clock 發布；不是 UI timer 推估聲音時間。
本次量測影格發布最大落後 10.33ms、player＋engine 停止最大 14.43ms。
首次在段中停止、觀察 250ms 無新音訊 tap／影格發布，再重新播放跨越兩段並停止，通過。

**仍為 NOT_READY / exit 2。** 這只驗證有界短測資的 packet 排程與裝置停止；
沒有把 RGBA 顯示在產品視窗，不是聲學／螢幕回錄驗收，也尚未量測完整音畫標記差值。
連續串流解碼、seek 與 Play/Stop 共用生命週期、VFR／60 FPS／負 PTS 的整合矩陣、
Windows 及原生編輯器仍未完成。不得把預解碼短測資做法接成整片預轉的產品播放器。
