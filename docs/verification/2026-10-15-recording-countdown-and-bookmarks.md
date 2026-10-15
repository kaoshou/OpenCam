# 錄製倒數與重點標記驗證

## 本機交付範圍

在 `codex/focused-editor-revision` 實作，沿用錄前檢查，新增0／3／5秒倒數、取消與到期後來源確認。新錄製、附加及插入適用；暫停直接繼續不倒數。使用者可在設定修改重點組合鍵，預設 Ctrl+Shift+M；Windows/macOS 原生全域註冊有成功／失敗回報。macOS 不額外改變開始／停止與暫停／繼續的前景行為。

Recorder 取目前來源的有效錄製時間，不增加檔案／磁碟查詢以取得標記時鐘。以現有綁定目錄原子寫入 `opencam-bookmarks.json`，每筆含 session、來源相對路徑與來源內時間。命令沿用認證 IPC 與 operation ID；寫入確認後才顯示保存成功。標記等待不占用 UI 主要錄製命令的 busy 狀態，停止控制仍可用；Recorder 端命令仍序列化，不承諾故障磁碟的回覆延遲。

編輯器提供有界可捲動清單、定位、改名、刪除及紫色旗標。重排／分割／合併／裁剪後依來源投影，同一來源重複保留時可顯示多個位置；無法定位／裁掉者保留附屬資料但不繪製旗標。標記獨立自動保存，不納入片段 Undo／Redo，不產生網頁章節。影片、主專案 schema 與產品版本不變，版本仍0.3.6。沒有打包、合併、推送、發布或啟動舊 Release 追蹤。

## 環境與測試記錄

macOS arm64，.NET SDK8.0.425（`/tmp/opencam-sdk8-repaired`），FFmpeg7.1.2（`/tmp/opencam-task2-ffmpeg.GOzyFj`），Node22.16.0。測試使用合成媒體、實際專案儲存與受控擷取邊界，不啟用真實螢幕／麥克風。

- Core bookmark tests：保存重開、主 manifest 不變、拒絕過期修訂、損壞附屬檔不覆蓋、非法路徑／重複 ID、重排／裁剪復原、重複及 compound 片段。
- Media tests：實際 dispatcher 錄製狀態限制／重送不重複／改名刪除／pause-resume 時間、清單定位、快捷鍵替換失敗回復、設定磁碟寫入失敗回復、倒數0秒／取消／UI延遲的單調時間、實際視窗取消、錄前檢查先於倒數、取消不建立專案或 session、單純續錄不重複倒數。
- `/tmp/opencam-bookmarks-ipc-red2.log`：尚未接入命令時觀察到 Assert.True 失敗；接線後定向通過。初始測試把來源誤設為 sessions/id；按現有 session store 結構更正為 sessions/Sessions/id，產品路徑未改。
- `/tmp/opencam-hotkeys-save-red.log`：設定保存拋出 disk full；新增錯誤處理與回復後通過。
- `/tmp/opencam-bookmarks-stop-red.log`：等待標記寫入時 IsBusy 為 true；改為獨立請求後要求 IsBusy=false 且 CanFinish=true 的測試通過。
- 第一輪完整回歸 `/tmp/opencam-bookmarks-full.log`：Core241通過；Media779通過、20略過、3失敗。1項英文820px按鈕重疊改為操作列換行；2項既有真實視窗測試以假預檢略過確認，須另明確接受新增倒數。原錄影保護斷言未放寬。
- 中途完整 `/tmp/opencam-bookmarks-full-final.log`：Core242、Media785通過、20略過、0失敗。後續仍有啟動順序／標記時鐘及停止控制的小範圍整合修正，不把此輪當作最後交付證據。
- 最後完整交付結果另於下方更新；命令為 `dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false -p:UseSharedCompilation=false --blame-hang-timeout 60s --blame-hang-dump-type none`，PATH使用上述SDK及FFmpeg。
- 播放器23項通過，`/tmp/opencam-bookmarks-player.log`；網站30項通過，`/tmp/opencam-bookmarks-website.log`。網站 Swift／Clang 測試使用獨立暫存 cache。
- 版本一致性0.3.6與 `git diff --check` 通過。保留既有 NU1900／nullable／preflight AVLN3001 警告，不宣稱線上 NuGet audit 通過。

初次受限沙箱測試執行器無法綁定本機 loopback，回合中止；允許本機測試連線後正常執行。第一批 Core 功能測試雖先寫，但首次受限建置尚未跑完即寫入實作，因此不把該回合記成已觀察到缺功能的 RED。後續實際失敗與通過紀錄如上，不擴大 RED→GREEN 主張。

## 原生 ABI 與人工驗收

本機 SDK 編譯 `/tmp/opencam-bookmark-abi.c`，執行結果為 EventHotKeyID=8、EventTypeSpec=8、ByteCount=8、ItemCount=8；Control4096／Shift512／Option2048／Command256、M46／F9 101、pressed5／released6。Interop使用兩個 UInt32 的 ID／事件型別，ByteCount與ItemCount使用nuint；callback留存並於dispose解除。這僅驗證宣告與常數，不代表真實背景事件已驗收。

以下為 **REQUIRES MANUAL VALIDATION**：

1. Windows與macOS實機設定 Ctrl+Shift+M，切換到其他程式錄製，按一下只產生一筆、長按不連發；再更改組合鍵並確認舊鍵失效。收集版本、OS、快捷鍵設定與標記檔；重複或漏收算失敗。
2. 以另一程式占用組合鍵，確認 OpenCam 提示註冊失敗、未謊稱新鍵已套用，原設定可用；解除占用後重試。確認離開程式後不殘留註冊。macOS依系統功能鍵設定測試F鍵；未擷取一般按鍵。
3. 各0／3／5秒、取消、Escape、開始／停止鍵、關閉倒數視窗；確認取消無新增正式專案／session，開始影片不含倒數。單／多螢幕與不同DPI皆需檢查。
4. 倒數中拔除／替換螢幕或音訊裝置、改變權限，確認不使用失效來源直接開始，提示重新檢查。
5. 錄製／暫停／續錄／插入多個片段，播放核對標記對應內容；編輯重排、裁剪、復原、合併與重開後重新核對；不能僅看秒數宣稱原生媒體時間已驗收。
6. 磁碟慢／空間不足或附屬檔拒絕寫入時，標記失敗需提示而不損壞影片，停止控制仍可操作；重開已成功寫入的標記應保留。長A/V與完整實體ReleaseGate仍依既有清單另行驗收。

## 交付驗證結果

最後版本已經由 `/tmp/opencam-bookmarks-final-targeted.log` 完成 Release 建置並通過13項新功能定向測試；之後以 `dotnet test ScreenRecorder.sln -c Release --no-build --no-restore -m:1 /nodeReuse:false --blame-hang-timeout 60s --blame-hang-dump-type none` 對同一份最後程式碼執行完整回歸，exit0：Core242、Media786通過，20略過，0失敗，共1028通過。完整記錄 `/tmp/opencam-bookmarks-ready.log`。沒有把平台略過算成實機通過。

播放器23項、網站30項、版本一致性及diffcheck均已通過。主repo原dirty spec及5個未追蹤計畫保持原狀。遵從使用者精簡審查要求，只作作者整合檢查，沒有獨立代理審查。本機分支保留；未合併、推送、打包或發布。
