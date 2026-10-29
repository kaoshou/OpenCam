# Linux 核心接入實驗紀錄

2026-10-30，OpenCam v1.2.0 開發分支 `codex/linux-core-feasibility-20261030`。

這是已核准 Ubuntu 平台設計的第一階段，不是已可正常錄影的 Linux OpenCam，也不是 Release。正式 Windows/macOS 程式碼、核心、建置腳本、版本與下載均未修改。

## 實作範圍

- Linux-only 固定來源／乾淨 checkout／新輸出目錄驗證，含真實 git fixture 測試。
- pinned OBS 33.0.0 與 pinned FFmpeg n8.1.3 Linux 私有前綴建置；不替換作業系統函式庫，不修改既有平台依賴。
- 獨立 `OpenCam.ObsLinuxProbe`：明確載入專用插件、X11／PipeWire 來源、PulseAudio 相容端點、OBS CPU H.264/AAC 編碼與既有 MKV mux 的唯讀重用。
- 有界 runner：MKV/stdout 與機器事件/stderr 分離、媒體／日誌上限、取消與直接子程序清理、第一失敗原因保留、完整解碼／packet／動態畫面／時長覆蓋驗證。
- 專用 Ubuntu Actions 工作只上傳日誌與自產測試媒體，不散布原生執行檔；來源／授權及相依閉包尚未驗收前不產生使用者二進位包。

尚未接入 `src/` 的平台工廠或專案流程。剪輯、匯入、預檢、試聽、輸出、教鞭、標註與快捷鍵的 Linux 產品接線仍未完成。

## 已執行的建置與錯誤隔離

| Ubuntu run | commit | 實際結果 |
| --- | --- | --- |
| [40918132163](https://github.com/kaoshou/OpenCam/actions/runs/40918132163) | bf1bceb | pinned FFmpeg 私有函式庫編譯成功；OBS configure 缺 ECM，已保留日誌，補 `extra-cmake-modules` |
| [40918698202](https://github.com/kaoshou/OpenCam/actions/runs/40918698202) | 3d1108f | FFmpeg 編譯成功；淺 checkout 缺 tag，生成 header 顯示 commit 縮寫，OBS 版本檢查拒絕；恢復且驗證 n8.1.3 tag 對應同一 pinned commit |
| [40919652066](https://github.com/kaoshou/OpenCam/actions/runs/40919652066) | 38fc138 | pinned OBS 原生核心編譯、安裝成功，`CORE_BUILT`、錄影 `NOT_TESTED`；新探測器 include 路徑錯誤，改用私有 stage 明確 headers/library |
| [40920437874](https://github.com/kaoshou/OpenCam/actions/runs/40920437874) | fdd1687 | OBS 核心再次成功；探測器編譯發現 JSON 函式 `quoted` 與 `std::quoted` ADL 名稱衝突，改為 `jsonQuote` |
| [40922306269](https://github.com/kaoshou/OpenCam/actions/runs/40922306269) | 48f96a5 | OBS 核心與探測器皆編譯成功；無桌面依賴的四項原生輸入／防誤選測試通過，另外兩項因無 DISPLAY 略過；進入 Xvfb 後兩項真實來源測試異常退出，未通過錄影，已新增 gdb 蒐證重現 |
| [40923056696](https://github.com/kaoshou/OpenCam/actions/runs/40923056696) | a408a50 | gdb 證實 SIGSEGV 位於 `xshm_properties → obs_get_source_properties → main`；pinned XSHM callback 會解參考 source instance。改用 screen=-1 的未擷取實例及 `obs_source_properties(instance)`，不 patch upstream，也不先擷取預設螢幕 |
| [40923773969](https://github.com/kaoshou/OpenCam/actions/runs/40923773969) | 1b0df39 | 六項原生測試通過，實際 XSHM 動態畫面、20 FPS、無音源錄製及完整媒體驗證通過。系統音訊測試因 fixture 傳入 sink 名稱而非 OBS 列舉的 monitor source ID，於媒體寫入前以 `audio_device_identity` 正確拒絕。pinned `pulse-input.c` 的 `pulse_output_info` 回傳 `monitor_source_name`；修正 fixture，不放寬精確端點驗證 |
| [40924771313](https://github.com/kaoshou/OpenCam/actions/runs/40924771313) | 2a4acd6224abbfa774e00fca0a97f314b7f4735b | 七項原生整合／輸入測試全數通過。Xvfb 640×360、20 FPS、約 3 秒的 none/system/microphone/both 四組實際 XSHM/PulseAudio 相容來源錄製，皆通過動態畫面、每音軌 packet DTS、正 duration、請求時間覆蓋、H.264/AAC 存在性及完整解碼；音源是專屬靜音虛擬裝置，不是實體收音驗收 |

這些失敗不代表 Windows/macOS 回歸，亦不因核心建置通過就視為錄影成功。後續整合結果另行追加，不覆寫前述失敗紀錄。

本機證據根目錄：`/Users/yhcheng/Documents/Codex/outputs/opencam-linux-core-20261030/`，各 `run<ID>` 內有原始建置／環境／套件版本／版本雜湊／輸入測試日誌。診斷 artifact 保存期 14 天；開發源碼與本報告保留實測解釋。

40924771313 下載後的 MKV SHA-256（`x11-20fps*/capture.mkv`）：

```text
none        e9d54c161441d843b5654d4e860ce075f7355bfbe0cb34479491bc6a14124ac0
system      6ddf6b6f2f01f7c9f6138e0bf753ac35de268a274065cf0f6155018443b7774a
microphone  7730a444ed6af1fdd272e85969e3ec23692c1d1d6a9c0e2c5df2f728b3f4b3c2
both        c7a540a7492aeeaec6ec5fe066da150c5736bc9925c78ac7b6cafb5c49632fcb
```

## 獨立審查與修正

審查範圍 `5ce4dc0..fdd1687`，指出以下 Important 問題。修正以失敗測試先行，狀態不等於已做 GNOME 或實體驗收：

1. Unix platform/display 必須先於 `obs_startup` 設定，避免純 Wayland 啟動被誤選為 X11。
2. PipeWire／Portal 結束不能被舊紋理與正常編碼掩蓋；觀察 pinned source 的固定 stream-state 訊息及本 helper 所有的 session-bus `Session::Closed`。
3. 選取音源不能永久鎖存第一次回呼；診斷檢查持續樣本新鮮度，未知時使用 `audio_health_unverifiable`，不是聲稱已證實裝置斷線。零音量、有 frames 的回呼仍健康。
4. 改用 OBS monitor-only PipeWire source，不接受 MONITOR|WINDOW 的統一選擇器冒充桌面驗收。
5. 驗證器拒絕 terminal session/source/audio 事件，不能因較早的 healthy/ready 或較晚的 stopped 而通過。
6. 比較 packet-derived span、完整解碼幀數與實際 ready→STOP 時間；靜態 metadata FPS 或少量有效影格不足以通過長時間請求。

兩項 minor 尚留待產品接入前處理：pinned source 將取消／拒絕合併為 log，尚不能可靠區分；列舉包含 default/disabled 項目，但擷取只接受精確有效端點。不得藉此宣稱 Linux UI 已可用。

## 測試矩陣與目前限制

| 項目 | 狀態／證據 |
| --- | --- |
| 真實 pinned checkout、路徑／覆寫／來源污染拒絕 | 本機 12/12 PASS（含官方 source checkout）；一般無 source 環境會略過下載 source 的正向測試 |
| Core 回歸 | 本機 .NET 8：378 PASS、0 FAIL |
| Media 回歸 | 本機 .NET 8：1484 PASS、23 SKIP、0 FAIL；Windows/實體套件等 skip 不代表通過 |
| Node scripts/CI 回歸及 Linux parser/runner | 審查修正與隔離檢查加入後 123 PASS、3 SKIP、0 FAIL |
| Linux source health 純 C++ | 本機 C++17 編譯及 assertions PASS，涵蓋 session/stream loss、選取音源過期、正常靜音 frames、主動停止 |
| Ubuntu 原生 libobs build | 真實 CI PASS，見上述 run；不等於 source capture PASS |
| Linux 原生探測器編譯 | 真實 Ubuntu CI PASS，見 40922306269；不等於錄影 PASS |
| 實際 X11 虛擬擷取與音源矩陣 | 40924771313：640×360、20 FPS、約 3 秒動態畫面，none/system/microphone/both 四組皆 PASS；七項原生測試 PASS。下載後於 Mac 另跑四個 MKV 完整解碼亦 exit 0。虛擬來源不等於實體桌面驗收 |
| GNOME Wayland permission／cancel／source closure／FD cleanup | REQUIRES MANUAL VALIDATION |
| 實體螢幕／麥克風／系統聲音／聲音內容／長時間同步 | REQUIRES MANUAL VALIDATION |
| 5/30 分鐘、20/30 FPS、端點移除、更換預設、多次暫停續錄 | 尚未驗收；探測器不提供產品的專案暫停續錄流程 |
| ELF 依賴閉包、可重定位與來源授權完整二進位交付 | 尚未驗收；因此只上傳日誌／自產媒體 |
| Windows/macOS 實體錄影回歸 | 尚未重新執行；受保護來源無變更與自動回歸不能取代實機 |
| 後續 Linux 產品平台／媒體／桌面功能 | 尚未接入，正式支援不可宣告 |

原暫存 .NET 8 SDK 已缺檔，第一次本機 test 退出 131；第二次 `--no-restore` 因 clone 無 assets 出現 NETSDK1004。已從官方版本資訊取得 SDK 8.0.425，核對 SHA-512 後解至獨立 Documents 目錄、正常 restore 重跑，才得到上表 Core/Media 結果；未更換系統 SDK，也未隱藏原失敗。

## 執行決策與代價

- 重用已隔離的開發 checkout，另開 Linux 專用開發分支；不建立多餘 worktree，正式 master/tag/Release 不動。
- 固定 pin 的 public validator 使用泛用真 git checkout verifier，以便測試不用偽造 SHA；若驗證錯誤會錯標來源，因此有 wrapper 錯 pin 拒絕測試。
- Ubuntu 需要 >=8.0 FFmpeg，由既有 pinned n8.1.3 另建私有 libraries-only 前綴；失敗只阻擋 Linux 實驗，不改系統或其他平台套件。
- 新 workflow 以明確開發分支 push 觸發，避免為註冊新 dispatch workflow 而修改 master；代價是額外 CI 用量，沒有 release 權限。
- 等建置時先做已定義協定的獨立驗證器；若接入事實不同，調整 Linux-only probe 邊界，不更動產品協定。
- 沒有回呼不冒稱裝置斷線；無法證明健康時診斷保守拒絕，代價是假陰性，須在接入產品前研究端點實際狀態，不掩蓋無聲。
- 審查列為未判定的 compositor、實體音源、程序後代／CLI signal／磁碟故障、ELF 與產品功能仍是門檻，不是已豁免；代價是尚不能提供可正式使用的 Linux 包。

## 下一步門檻

基本 X11 虛擬資料證據已取得。下一個門檻是取得 Ubuntu GNOME Wayland 整合環境，實測純 Wayland、授權與來源中斷，另擴充有聲／停止／恢復、30 FPS 與耐久測試。目前本機是 macOS，沒有可操作的實體 Linux 桌面；GitHub Xvfb 不能替代上述驗收。若 pinned 插件不足以提供必要 metadata/transport 狀態，需 Linux 專用 bridge 的明確方案，不放寬通過條件。

上述確認後才撰寫並執行產品平台／安全 FD-spawn／預檢／剪輯輸出接入計畫，最後處理教鞭、標註與快捷鍵。未通過三平台驗收前不合併、不發布。
