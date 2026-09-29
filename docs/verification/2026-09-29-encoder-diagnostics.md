# 編碼器診斷第一階段驗證（2026-09-29）

## 範圍

分支 `codex/encoder-diagnostics`，基準 `04ec571`（產品基準 `fd1f1f8`）。
Task 1 `8ee3b85`、Task 2 `1afcf98`、Task 3 `e2d1808`；顯示與文件見本檔所在提交。
版本仍為 **0.2.3**。未推送、未建立 tag／Release，未替換 FFmpeg、依賴或授權。

此階段改善探測、快取、診斷及分段啟動失敗保護；**不是 Windows 游標閃爍修復，也沒有 CPU 降幅結論**。

## 環境與命令

- macOS 26.5.2 (25F84)、arm64；.NET SDK 10.0.302 / runtime 10.0.10；Node 22.16.0。
- 產品仍 targeting .NET 8。本機沒有 .NET 8 runtime，依既有測試方式使用 `DOTNET_ROLL_FORWARD=Major`；精確 .NET 8 與 Windows/Linux CI 仍待授權推送後執行。
- 本機 FFmpeg 使用 `/opt/homebrew/bin/ffmpeg`，不代表發布包內附版本的驗收。

```sh
dotnet restore ScreenRecorder.sln
env DOTNET_ROLL_FORWARD=Major dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
env DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
npm ci --prefix website --cache <本次工作區獨立快取>
npm test --prefix website
npm run build --prefix website
bash tests/native/OpenCamSystemAudioTests.sh
git diff --check
```

使用者 npm 快取曾出現 EACCES；改用獨立快取後成功，未修改其權限或清除快取。測試因需本機 socket／子程序，以允許該操作的執行環境運行。

## 自動化結果

| 項目 | 實際結果 |
| --- | --- |
| Release solution build | PASS：0 warnings / 0 errors |
| Core tests | PASS：80 passed / 0 failed |
| Media tests | PASS：268 passed / 0 failed / 12 skipped |
| Website tests + build | PASS：27 passed / 0 failed；建置成功 |
| Native audio helper compile / argument checks | PASS：兩個 Swift helper 編譯與錯誤輸入檢查通過 |
| Diff whitespace | PASS |

12 個略過項目包括 Windows 顯示／裝置／擷取、需特定麥克風或既有 Recorder 的 opt-in 驗證；**SKIP 不算 PASS**。三個既有 CI verification jobs 都測試完整 solution，新的 test-child 透過 project reference 建置並複製到測試輸出，不進產品封裝。

TDD 證據：

- Task 1：8 個新程序行為測試 RED → GREEN（退出碼、無換行雙輸出、逾時／取消、只回收自身程序、重複逾時）。
- Task 2：12 個新政策測試 RED → GREEN（CPU 零探測、平台順序、取消不污染、快取期限、FFmpeg 身分改變、失敗冷卻／總預算）。
- Task 3：9 個新行為測試 RED → GREEN；清理失敗後錯誤啟動第二場的追加案例亦 RED → GREEN。現在保留引擎所有權直到清理成功。回歸 42 passed / 1 skipped；另測成功握手後 metadata 保存失敗仍保留有效片段，不重試 CPU。
- Task 4：5 個文案鍵及 12 個顯示測試 RED → GREEN；舊 session/telemetry 缺欄位可讀。確認 Auto 偏好不被覆寫、暫停保留、本場未知不挪用上一場 codec。
- 真實 FFmpeg 雙段合成、stream-copy 合併、ffprobe 及解碼通過；音訊匿名管線、IPC 認證、救援安全回歸保留。

## 本機實際媒體／UI 檢查

1. FFmpeg libx264：1920×1080、30 FPS、3 秒合成影像 + 48 kHz AAC，MKV → MP4 stream copy，ffprobe 尺寸／幀率／音軌符合，解碼 exit 0。
2. VideoToolbox：1920×1080、30 FPS、3 秒合成編碼成功。這不是 Windows 或桌面效能比較。
3. 新 UI 本機啟動，中英待命／錄影／暫停共六個既有展示模式渲染均成功，新文字位於波形下方，未遮住右側按鈕／卡片。展示模式波形是展示資料，沒有 Recorder 遙測，因此編碼器保持 pending；不能當成真實錄影截圖。
4. 另用本次 Recorder、Mac provider 與重新編譯的原生 helper 做 **640×480／30 FPS 真實桌面錄影**，Auto 實際選到 VideoToolbox，四種音訊組合皆成功啟動、停止並封裝有效 H.264 / AAC MP4。無音訊模式使用原有靜音音軌策略。
5. 雙音源模式暫停後改為只錄麥克風、隱藏游標，更新／續錄／停止成功；兩段均為 VideoToolbox，正常合併為 7.599 秒 MP4。隱藏游標僅用於設定切換測試，不是閃爍修正。
6. 低音量測試音的系統波形 RMS 約 0.316；麥克風取樣當時低於波形顯示門檻（Silent）。成品 volumedetect：系統-only max -38.0 dB、麥克風-only max -61.7 dB、雙音源/續錄 max -43.4 dB。可確認非零音訊，但**不證明正常說話音量或音質驗收通過**。

本機暫存產物（未提交、未上傳）：

- 六個 UI 渲染：`/private/tmp/opencam-encoder-{idle,recording,paused}-{zh,en}.png`。
- 合成檔：`/private/tmp/opencam-encoder-1080p-cpu.mp4`、`/private/tmp/opencam-encoder-1080p-videotoolbox.mkv`。
- 實錄四個 MP4 與 MKV：`/var/folders/95/1sbw_nz57fschyb8czpsmxp40000gn/T/OpenCam-live-encoder-CAq4CL`。

## 仍待驗收

- **BLOCKED（缺 Windows 機器）**：使用者的 1080p／30 FPS／Auto／原始游標組合；Intel QSV、NVENC、AMF、混合顯卡、多螢幕與 CPU 前後比較。不能把探測失敗稱為「內顯不支援」。
- **待實機人工驗收**：正常說話的麥克風音質、長時間 A/V 同步、GUI 完整按鈕流程、啟動／停止過渡畫面與最長回退文案、實機強制中斷後救援。分段、救援及 UI 狀態邏輯已自動化測試，但不能取代上述項目。
- Mac 含原生音訊的停止／暫停觀察到約 6 秒收尾，遙測累計時間與成品時長不同；本階段未改音訊停止協定，不宣稱此項已改善，需另行比較基準版本。
- 第二階段 IPC 分流、第三階段 GDI／游標擷取尚未實作。簽章、發布驗收與 Windows FFmpeg 對應原始碼核對限制維持不變。

## 實作裁決

1. 使用本機 .NET 10 roll-forward 測試，不改產品 target；代價是精確 .NET 8 回歸仍待 CI。
2. 自動 CPU 重試僅限明確 `EncoderStartupException`，不把設定／保存／清理例外視為硬體失敗；避免已錄資料或未清乾淨時再開程序。代價是非編碼器的暫時性錯誤需要使用者手動重試。

獨立審查結果與必要修正於後續提交補記。
