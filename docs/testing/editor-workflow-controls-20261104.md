# 剪輯器新增操作／Editor controls (development branch)

本文件為本次開發版本操作說明，尚未納入公開 v1.2.1 安裝檔或官網。完成實機驗收、準備發布時再併入正式使用說明。

- 前／後一格依專案輸出 FPS 定位，混合不同來源 FPS 也使用同一時間軸。
- 選取片段後拖曳左右手把向內修剪；放開才套用，Esc 取消，原始媒體不改寫。
- 邊界吸附預設啟用，目標為片段邊界和書籤；可取消，或按 Alt 暫時略過。
- Space 播放／暫停；←／→ 前後一格；Shift+←／→ 前後一秒；I／O 標記範圍。輸入欄位、滑桿、選單及聚焦按鈕保留原操作。鍵盤操作按鈕提供說明。
- 有效範圍可播放選取範圍、循環試聽；一般播放鍵仍播放整個時間軸。拖曳、修改範圍、停止或離開會取消原本循環。
- 預覽混音音量表使用效果後、預覽靜音前的真實 PCM，不代表作業系統喇叭音量。無聲和等待取樣分開顯示；已混合的系統／麥克風音訊不可分離調整。
- 專案已儲存不等於影片已輸出。底部列出最近輸出的修訂；片段輸出不代表全專案已輸出。

## English

This is development-branch guidance, not a description of published v1.2.1 installers. Merge into public guides only after physical acceptance and release preparation.

- Frame steps follow the project output FPS, including mixed-FPS sources.
- Drag selected clip edge handles inward to trim. Release applies; Escape cancels. Original media is unchanged.
- Snap to clip edges/bookmarks is initially enabled; toggle it off or hold Alt to bypass.
- Space plays/pauses; Left/Right steps frames; Shift+Left/Right seeks seconds; I/O marks the selection. Focused inputs and controls retain their own keys. Keyboard controls lists the bindings.
- Play selection and Repeat selection operate only within a valid marked range. Ordinary Play uses the whole timeline. Scrubbing, changing the range, stopping or leaving cancels the old loop.
- Preview mix levels use actual post-effects, pre-preview-mute PCM, not speaker hardware volume. Silence differs from awaiting samples. Already mixed microphone/system audio cannot be separated.
- Project saved is not video exported. The footer identifies the latest export revision, without claiming a clip-only export contains the whole project.
