# 錄影專案預覽引擎：選型尚未完成

更新：2026-10-09（Asia/Taipei）

## 狀態

未選定產品播放器，不新增播放器產品依賴。現有 `tests/ScreenRecorder.ProjectPreviewProbe` 是獨立診斷工具，不是原生編輯器預覽實作。

## 已量測部分

FFmpeg pipe 候選只讀取呼叫端提供的唯讀、可定位 stream，不重新開啟使用者專案中的任意檔案路徑。測試生成自己的短 MKV（320×180、30 FPS、非零 PTS），核對 requested PTS 與獨立以畫格序號解碼的 SHA-256 framehash。

已取得：PTS 2200 定位／畫格內容一致、連續 100 個定位請求取消舊請求、Stop 取消並等待子程序退出。十次短片定位最大 35.1ms。此數據不是兩小時專案的 p95，也不是產品效能承諾。

探針整體輸出 **NOT_READY / exit 2**：未實作 PCM 播放、A/V clock，未驗證預覽音訊停止；缺乏 VFR、60 FPS、長 GOP、長專案、來源替換和雙平台完整矩陣。不能據此允許錄影與預覽音訊交疊。

## 候選比較與未解門檻

| 候選 | 已有證據 | 仍缺少／限制 |
| --- | --- | --- |
| FFmpeg 子程序＋pipe stream | 短 MKV 定位與取消探針 | pipe 不可 seek，當前實作每次從來源頭解碼；長片效率未驗證。PCM sink、同步時鐘與連續跨段尚未實作 |
| LibVLCSharp＋StreamMediaInput | 官方有串流回呼及播放器 API | 尚未實測；此機沒有 /Applications/VLC.app。Apple Silicon native binaries、套件散布與停止回呼需逐項核對，不沿用已放棄的試作 |
| 平台播放器（AVPlayer／Media Foundation） | 本輪未實測 | 需確認原始 MKV、跨平台一致性、精準 PTS、取消與分段音畫；不能預先假定可直接使用 |

FFmpeg 官方說明 [pipe 與 fd 的 seek 差異](https://ffmpeg.org/ffmpeg-protocols.html#fd)：fd 對 regular file 可定位，pipe 不具同樣能力。但跨平台安全傳遞已綁定檔案描述元尚未在本專案實作，不能只改 URL 就宣稱已解決。

LibVLC [MediaPlayer API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html) 的 Stop 等待媒體工作結束；[MediaInput API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.html) 提供受控輸入方向。是否能達到精準邊界、回呼可取消及可靠停止，仍需測試。

## 下一個必要交付

以相同 fixture／量測規則補齊候選實驗，至少一個後端通过精準畫格、聲音停止、跨接點同步及原始來源保護門檻，才制定其產品接線計畫。若沒有後端通過，保留未完成狀態，不用假預覽或預先轉整片掩蓋問題。
