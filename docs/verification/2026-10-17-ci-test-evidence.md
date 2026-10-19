# CI 測試計畫第一批：保留證據

依據：`TESTING.md` 第 9、10 節。使用者同意繼續，先落實證據保存；不推送、合併、升版或發布。原有文件修訂與 main 未提交文件均保留。

## 變更與界線

- 三個 verify job 的完整 solution 測試加 TRX，失敗時仍上傳 `test-results/`，證據保留 7 天，名稱與 Release 安裝包分開。
- 版本清單保存 Commit、runner 映像／架構、OS、CPU 數、Node、SDK／Runtime、FFmpeg／ffprobe 版本及測試命令。不蒐集完整環境、程序參數、私人畫面或音訊。
- 蒐集工具的子程序各限 10 秒及每路 16 KiB；工具失敗仍保存 JSON 並非零退出，不掩蓋失敗。
- Windows restore 非零時明確停止，不讓後續命令覆蓋失敗碼。原測試、filter、期限、三平台打包依賴、漏洞檢查與正式附件選擇皆保留，無重跑／continue-on-error。
- 未修改應用程式。未實作心跳時鐘注入、慢讀測試同步、效能分工或完整程序收尾量測；不宣稱偶發問題已根治。

## 本機驗證（macOS arm64）

| 項目 | 結果／證據 |
| --- | --- |
| 蒐集工具 RED | 空實作時 4 案例因缺少資料／失敗狀態而失敗，`/tmp/opencam-ci-evidence-red.log` |
| 蒐集工具與播放器 | 5 個蒐集案例＋23 個播放器案例通過，`/tmp/opencam-ci-evidence-node.log`；含真實子程序非零退出、缺工具、超時、過量輸出與 CLI 留報告 |
| 網站 | 首輪 38 通過／1 失敗：原 macOS 圖示測試遭 Swift cache sandbox 拒絕，`/tmp/opencam-ci-evidence-website.log`；獨立 Swift 快取及允許本機工具執行後原 39 案例通過，`/tmp/opencam-ci-evidence-website-permitted.log`，未改斷言 |
| Release build | 0 errors／3 既有 warnings，`/tmp/opencam-ci-evidence-build.log` |
| TRX 實測 | 原狀態機 7 案例通過，`/tmp/opencam-ci-evidence-trx.log`，XML 在 `/tmp/opencam-ci-evidence/trx/`；不是完整 .NET 測試結果 |
| 相對路徑 | 再以 `--no-build --results-directory test-results` 跑同 7 案例，`/tmp/opencam-ci-evidence-relative-trx.log`；確認 XML 位於 workflow 上傳根目錄，解析 counters 為 7 passed／0 failed，且目錄由 `.gitignore` 排除 |
| 實際版本清單 | 使用 SDK 8.0.425、FFmpeg 7.1.2 執行 CLI 成功，`/tmp/opencam-ci-evidence/environment.json` |

本輪只本機驗證，Windows／Linux hosted runner 與 Actions artifact 上傳尚未執行。自行檢查差異與 YAML 接線，不另派審查代理。下一批才改寫兩項時間敏感測試，仍須保留原行為契約及真實存盤／4K 內容驗證。
