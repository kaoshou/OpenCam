# OpenCam 驗證報告 (Acceptance Evidence)

本報告只記錄實際執行過的自動化驗證。未連接或未操作的實體硬體情境列為待人工驗證，不以程式存在或測試被略過推定為通過。

## 驗證基準

- 日期：2026-09-26（Asia/Taipei）
- OpenCam：0.2.1
- Commit under test：`a3804e326100fb82b3633c1faa1b91dead6e7fae`
- 主機：Apple Silicon (`arm64`)
- 作業系統：macOS 26.5.2，Build 25F84
- .NET SDK：10.0.302（專案目標 `net8.0`）
- 驗證範圍：本機自動化、macOS 原生 helper、網站與封裝腳本
- 非本次證據：Windows 實機錄影、Windows 安裝程式操作、實體多螢幕/DPI/熱插拔、鎖定與睡眠、2/4/8 小時長錄影

## 實際命令與結果

| 命令 | 結果 | 證據摘要 |
| :--- | :--- | :--- |
| `DOTNET_ROLL_FORWARD=Major dotnet restore ScreenRecorder.sln --force-evaluate` | PASS | 依專案宣告重新評估並還原相依套件；本專案尚未追蹤 `packages.lock.json`，因此不宣稱 locked restore |
| `dotnet build ScreenRecorder.sln -c Release --no-restore` | PASS | 所有方案專案建置成功，未產生編譯診斷 |
| `dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release --no-build --no-restore` | PASS | 53 passed、0 failed、0 skipped |
| `dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --no-build --no-restore` | PASS with skips | 215 passed、0 failed、12 skipped；略過項目為目前主機不具備的條件，不視為通過 |
| `node scripts/check-nuget-vulnerabilities.mjs ScreenRecorder.sln` | PASS | 解析完整 transitive graph；未發現已知弱點套件，High/Critical 會阻擋封裝 |
| `node scripts/check-version-consistency.mjs` | PASS | `VERSION`、組件資訊、安裝腳本、App bundle 與網站皆由同一版本來源衍生 |
| `npm test --prefix website` | PASS | 22 passed、0 failed |
| `npm run build --prefix website` | PASS | 雙語首頁與使用說明成功產生 |
| `bash tests/native/OpenCamSystemAudioTests.sh` | PASS | macOS 原生系統音訊 helper 編譯與行為測試成功 |
| `bash scripts/build-macos-icon.sh src/ScreenRecorder.UI/Assets/app_icon.png /tmp/OpenCam-hardening-final.icns` | PASS | 產生 117,887-byte `.icns`；網站 icon 測試另以 `iconutil` 反解並驗證 10 個必要尺寸 |

## 已由自動化覆蓋的關鍵行為

- 錄影狀態機、Session 儲存、Recovery/Remux、磁碟門檻、設定持久化與雙語資源。
- Recorder 健康遙測以可空布林表示未知/正常/異常；影格無進度、編碼器錯誤與音訊來源遺失會產生警告。
- UI 異常離開時，Recorder 會依父行程生命週期執行安全停止，不再宣稱 UI 消失後仍持續背景錄影。
- 發布工作流在 Windows、macOS 和可攜測試通過後才建置；只有發布工作具 `contents: write`。
- Windows FFmpeg 使用固定版本與 SHA-256 驗證；macOS icon 與原生麥克風 helper 由測試保護。
- MKV 在 MP4 驗證成功前保留；未完成 Session 可由「修復救援」重新封裝。

## 尚待人工驗證

以下項目需依 `MANUAL_TEST_CHECKLIST.md` 填寫版本、Commit、日期、OS/build、硬體、執行者、結果及產出物/log 路徑後，才能宣告 `PASS`：

- Windows 與 macOS 真實錄影的所有音訊組合與連續性。
- 三個以上實體螢幕、負座標、不同 DPI 與顯示器 hot-plug。
- 外接麥克風拔除、預設音訊裝置更換。
- Windows Lock/Unlock、macOS/Windows Sleep/Resume。
- 2 小時音畫同步，以及 4/8 小時資源與工作檔成長趨勢。
- 強制終止 UI/Recorder/FFmpeg 後的實體檔案救援。

## 已知限制

- 未提供 Apple Developer ID 或 Windows Authenticode 簽章，因此下載版仍可能出現作業系統信任警告；此項依本次工作範圍刻意不處理。
- macOS 官方套件目前僅提供 Apple Silicon；Intel Mac 尚未驗證或發布。
- 自動化在 macOS 執行時無法替代 Windows 實機與特定硬體驗收。略過測試必須保留為未驗證，不得改寫為成功。
