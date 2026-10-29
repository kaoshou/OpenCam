# 錄影前音訊平衡：本機驗證紀錄

日期：2026-10-29。版本維持 1.1.0，開發基準 `935ac515e880f4d15a19ff8c3ffd47d65aaa4f80`。此次未推送、合併或發布，尚未產生新測試包。

## 已實作

- 兩音源各自 0～100% 錄音音量，舊設定預設 100%。
- 來源即時音量、實際混音總量、過大與零音量提示。
- 五秒試錄後播放混音；改音量失效舊試錄；試錄中鎖定滑桿。
- 確認後保存，取消不保存；略過預檢與續錄帶入相同音量。
- libobs 真正套用各來源音量；0% 不取消來源或繞過健康檢查。
- 非預設音量透過新 helper 協定保護，舊 helper 不能忽略設定後假裝成功。
- 繁中、英文、日文及無障礙滑桿名稱。未加分軌、耳返、增益或錄影 fallback。

## 環境

macOS 26.5.2 Apple Silicon、.NET SDK 8.0.425、本機 FFmpeg 9.0.1。原生 helper 以既有固定 OBS 33.0.0/libobs runtime 與原始碼編譯。原生測試使用生成畫面和兩組生成音源，不是實體螢幕／麥克風。

## 實際結果

| 驗證 | 結果 | 證據 |
|---|---|---|
| 全 solution Core | PASS：378 通過、0 失敗 | `audio-balance-full-final.log`、Core TRX |
| 全 solution Media | PASS：1,484 通過、0 失敗；23 跳過 | `audio-balance-full-final.log`、Media TRX；跳過不計通過 |
| 預檢專項 | PASS：170 通過、0 失敗 | `audio-balance-preflight-final.log`，包含於全套範圍，不重複加總 |
| 原生 helper 編譯 | PASS：macOS arm64 | `audio-balance-cmake.log`；仍有既有警告 |
| 原生 libobs 合成回歸 | PASS：15 通過、0 失敗；1 平台限定跳過 | `audio-balance-native-full-final.log` |
| NativeOnly UI build | PASS：0 error、11 warnings | `audio-balance-nativeonly-build.log`；不等於零警告 |
| CI／打包腳本 | PASS：77 通過、0 失敗；2 跳過 | `audio-balance-packaging.log` |
| 三語 UI 操作及 Skia 版面 | PASS：5 通過、0 失敗 | `audio-balance-layout-final.log`、三語 PNG；呈現的是測試資料 |
| 舊 Mac 包內 FFmpeg 7.1.2 濾鏡相容性 | PASS：31 音訊專項通過、0 失敗 | `audio-balance-bundled-tool.log`；不是新完整包的整體驗收 |

證據複本與截圖目錄：`/Users/yhcheng/Documents/Codex/outputs/opencam-audio-balance-20261029.xHUaXi/`。

真實解碼測試證明，雙來源常數 .2 與 .1，在第二來源 25% 時得到 .225，而不是舊預檢自動平均的 .15。原生 AAC 解碼後的兩頻率投影驗證獨立比例及沒有平均化；單系統、單麥克風、雙來源 0% 皆仍就緒並輸出靜音。

## RED → GREEN 與審查

設定缺失、偏好保存、混音振幅、協定傳遞、UI 滑桿均先取得失敗再實作。獨立審查指出試錄回覆延遲時滑桿仍可能接受修改；已用延遲確認測試重現，再鎖定 UI、拒絕在途變更及等待操作 gate。0% 短暫保留舊來源表讀值亦經三語測試重現後修正。所有重要項目已修正，無刻意遞延的小問題。

第一輪全套曾出現 5 個失敗：兩個舊視窗高度固定值預期、三個舊裝置觀察器量測預期。新增音訊管線量測正確取代任意 stub 數值；測試改驗證已知測試音真實 RMS，並修正測試的輸出串流清理順序。最終全套重新執行為零失敗，並未將初輪失敗隱藏成通過。

## 執行裁定與代價

1. 沿用獨立暫存開發 clone，不在 master 修改。若選錯來源，會驗證錯版本；此次已核對基準 commit 與乾淨狀態。
2. 滑桿停止後 300ms，沿用確認停止／重啟的預檢流程，避免新增並行修改擷取的 IPC。代價是套用會短暫重啟音訊檢查，不是即時耳返。
3. 非原音量使用協定 2，原音量維持協定 1。代價是舊 helper 會明確拒絕非原音量，必須整包更新。
4. 視窗增加至 740×610，保留捲動。代價是較多垂直空間，小螢幕可能需要捲動。
5. 跨層以一個原子變更交付，避免留下只保存數值卻未在正式錄影套用的中間版本。

## 尚未驗證／不應宣稱

- Windows SDK 原生編譯、Windows WGC/WASAPI 實際錄影。
- macOS 真實螢幕、系統音訊及麥克風的最新功能整體錄影與主觀試聽。
- 雙系統五分鐘／長時間實機同步、實體音訊裝置中斷或預設端點切換。
- 新完整測試包內執行、最終 MKV／MP4 的實際使用者播放驗收。

以上全部為 **REQUIRES MANUAL VALIDATION**，未以合成測試或完整解碼代替實機驗收。步驟及需收集的資料見 `preflight-audio-balance.md`。本紀錄不構成發布核准。
