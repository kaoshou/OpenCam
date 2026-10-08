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
| LibVLCSharp＋StreamMediaInput | 官方有串流回呼及播放器 API | 尚未實測；此機沒有 /Applications/VLC.app。Apple Silicon native binaries、套件散布與停止回呼需逐項核對，不沿用已放棄的試作 |
| 平台播放器（AVPlayer／Media Foundation） | 本輪未實測 | 需確認原始 MKV、跨平台一致性、精準 PTS、取消與分段音畫；不能預先假定可直接使用 |

FFmpeg 官方說明 [pipe 與 fd 的 seek 差異](https://ffmpeg.org/ffmpeg-protocols.html#fd)：fd 對 regular file 可定位，pipe 不具同樣能力。但跨平台安全傳遞已綁定檔案描述元尚未在本專案實作，不能只改 URL 就宣稱已解決。

LibVLC [MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html) 的 Stop 等待媒體工作結束；[MediaInput API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.html) 提供受控輸入方向。是否能達到精準邊界、回呼可取消及可靠停止，仍需測試。

## 下一個必要交付

以相同 fixture／量測規則補齊候選實驗，至少一個後端通过精準畫格、聲音停止、跨接點同步及原始來源保護門檻，才制定其產品接線計畫。若沒有後端通過，保留未完成狀態，不用假預覽或預先轉整片掩蓋問題。
