# Ubuntu 平台支援設計：隔離實作與跨平台安全門檻

日期：2026-10-30。狀態：設計草案，待審查與使用者核准；不是 Linux 已完成宣告。

## 目的與已確認範圍

使用者希望 Ubuntu 可以實際錄影，而不只是能啟動介面，且不得破壞目前正常的 Windows、macOS 功能。使用者已同意獨立平台元件、NativeOnly/libobs、獨立分支、三平台回歸，以及 Ubuntu 24.04 LTS x64、Wayland 與 X11 的目標方向。本文件細化實作與驗收，尚未取得文件層級的實作核准。

正式版基準為 v1.2.0；應用程式基準 commit 為 `e0c369e6495532cc0393c349dc428ffb8a00327e`。既有 Ubuntu 試封裝分支僅新增診斷工具與文件，其 `src/`、`native/` 未修改。試驗確實在 Ubuntu/Xvfb 啟動主畫面，但不能錄影，也未驗證剪輯、實體裝置、Wayland 或長時間同步。不得將這份證據改寫為錄影通過。

首要目標是畫面、所選音源與時間軸正確，失敗不破壞已寫入內容。保持 20 FPS 首次預設、既有使用者設定、MKV 工作檔、暫停續錄、救援及驗證後輸出。版本暫維持 1.2.0；只提供明確標記的 Linux 開發測試包，不合併、不改標籤、不發布正式 Release。

## 方案比較與選擇

1. **新增 Linux 平台元件並擴充少量明確接入點（選定）**：重用核心模型、專案與編輯服務，Linux 的螢幕、音訊、原生處理與程序啟動獨立實作。Windows/macOS 原有工廠結果與封裝不變。成本是仍需審查少量共用分支，但差異可列舉並回歸。
2. 全面改寫三平台平台層：長期可能統一，但將可用的 Windows/macOS 同時置於重構範圍，違背本次安全優先，排除。
3. Linux 呼叫另一套 FFmpeg 錄影／外部 OBS GUI 自動化：可能較快看到影像，但引入第二種生命週期與錯誤模型，不符合保留 NativeOnly/libobs 的方向，排除。

不得為 Linux 升級現有 Avalonia、Windows/macOS OBS 或 FFmpeg 版本。Linux 若需要不能相容既有協定的升級，視為阻擋條件，先提出差異，不靜默擴大範圍。

## 原始碼現況與必要接入點

- `MainViewModel.cs` 建構時用 Windows/else 選取 FFmpeg、螢幕與音源服務，Linux 誤走 macOS；專案流程目前只在 Windows/macOS 啟用。需三路明確選擇，而非將所有非 Windows 視為 Linux。
- `ObsRuntime.cs` 僅允許 `win-x64`、`osx-arm64`，且包含 runtime manifest、雜湊、路徑驗證。Linux 增加獨立 runtime，不放寬既有平台驗證。
- `ObsRecordingEngineRouter.cs` 的後端預設為 Windows WGC、else SCK。需明確 Linux 後端，不能變更舊平台預設或失敗時改用舊錄影引擎。
- `native/obs-capture/CMakeLists.txt` 為 APPLE/else Windows；Linux 必須獨立 `elseif(UNIX AND NOT APPLE)`，Windows 仍明確 WIN32，保留各自連結內容。
- `ProjectMediaBackend.cs` 僅 Windows/macOS，Linux 預檢、匯入、預覽、音訊播放、輸出皆需補接，不得只打開 UI 控制就當成支援。
- `MacProjectMediaProcess.cs` 使用 Darwin 專用 spawn/FD ABI，不能直接在 Linux 呼叫或只移除作業系統 guard。
- `BoundDirectory.cs` 已有部分 Linux/POSIX 分支，需在 Linux 驗證實際開檔旗標、FD、權限及符號連結防護，不能假定所有 POSIX 行為都相同。
- 教鞭、全域快捷鍵、標註透明視窗與輸入穿透需逐項平台能力測試，不能由 Avalonia 能顯示視窗推論支援。

## 平台元件與共用程式碼契約

新增 `ScreenRecorder.Platform.Linux`，只放 Linux 的顯示、音源、媒體程序與桌面能力元件；在既有組合根明確選擇三平台。不要為了集中初始化而大幅搬移 MainViewModel。

每一筆共用修改記錄：改動前 Windows/macOS 行為、改動後行為、證明兩者相同的測試、實體驗證是否需要。共用核心模型以新增可選欄位或能力回覆為優先，既有欄位意義不變、舊平台預設不變；若無法保持相容，停下評估，不強行合併。

能力必須區分：平台可支援、目前可用、已授權、來源已就緒、正在輸出、正常靜音及真正失效。未知不是可用，也不是磁碟 0。UI 不提供假的可錄螢幕或成功狀態；未支援的 Linux 選項說明原因，不影響其他平台 UI。

## OBS runtime 與封裝

Linux 使用既有 pinned OBS 33.0.0 相同來源版本，建立 `linux-x64` 原生 helper、libobs、必要擷取/音訊/編碼插件、資料與相依庫清單。新增獨立建置腳本、runtime layout 與 manifest，不重用 macOS app bundle 路徑。記錄編譯器、Ubuntu 基準、來源 commit、編譯選項、依賴版本與授權。

優先在 Ubuntu 24.04 runner 從固定來源建立 runtime；不在錄影時任意搜尋使用者安裝的 OBS，也不讓未驗證的插件從工作目錄或任意環境路徑載入。圖形驅動、音訊 server、desktop portal 仍是作業系統能力，打包說明列出必要條件。不能聲稱所有 Linux 發行版通用。

首個測試包為 Ubuntu 24.04 x64 自包含 tar.gz，含啟動器、缺依賴檢查、診斷收集、版本/commit/雜湊與授權文件；不要求使用者安裝 .NET。依賴清單與來源對應通過前不提供公開下載宣稱可用。日後 deb/AppImage/Flatpak 是另一項交付，不在這次範圍。

## X11 與 Wayland 擷取

X11 使用 libobs 的 Linux X11 擷取路徑，檢查實際顯示器識別、幾何、縮放及來源尺寸，不把 UI 座標直接當實體像素。Wayland 使用 ScreenCast portal/PipeWire 路徑與使用者選擇授權，不以 XWayland 根視窗擷取假裝取得整個桌面。

portal 需在執行環境查詢可用版本與能力，不因最新文件列出較新方法就假定 Ubuntu 已提供。取消、拒絕、session closed、來源移除及程序結束都需清理 session/FD，回到可重試狀態。使用者選擇的實際 stream 是擷取來源，不把先前監視器索引套上去；尺寸/身份在首幀確認，未知時不先寫成功。權限互動等待與故障逾時分開；不無限等候、不只延長 timeout。

優先驗證 OBS 33.0.0 Linux PipeWire source 如何啟動 portal 及其 GUI/main-loop 需求。若原插件依賴 OBS Qt 前端，不將它當 headless helper 可直接載入的插件。先做最小整合試驗；必要時新增 Linux 專用 portal bridge/擷取 source，保持 helper 協定與其他平台不變，列明所需範圍後再實作。

自訂矩形以已授權來源內的裁切處理，不越權跨螢幕擷取。重啟恢復設定時重新驗證顯示器與裁切有效性；失效時請使用者選擇，不默默改錄另一螢幕。

## 音訊、預檢與錄影安全

Ubuntu 目標為 PipeWire/PulseAudio 相容環境的輸出 monitor 與麥克風來源。以穩定端點身份綁定，明確區分預設輸出與預設輸入；禁止把螢幕影像 portal stream 推論為已包含系統聲音。

預檢與正式錄影共用同一端點解析結果及使用者增益，但採獨立、可取消的生命週期。測試靜音、低音量、無 callback、裝置開啟失敗及中斷；已健康但無活動的音源不可單憑沒新封包判斷損壞。補靜音限於已確認健康的音源，使用 libobs 音訊時鐘，不偽造裝置成功。不得偷偷關掉所選音源以通過開始。

沿用錄影核心的首幀/尺寸/輸出檢查、音訊健康、MKV 已綁定輸出、停止封存、已有片段保全；真失敗要提示具體階段。無片段的開始失敗回待命可重試，不觸發無內容自動輸出；有片段的續錄失敗仍可編輯、匯出與救援。編碼優先驗證 OBS 軟體編碼器，硬體編碼另行驗證；不以未驗證 NVENC/VAAPI 作為必需條件。

## 剪輯、預覽、匯入與輸出

以 Linux 專用程序元件實作 FFmpeg/ffprobe 工作，仍只用於預檢/媒體處理，不是錄影 fallback。對接現有 `IProjectMediaProcess`、streaming 及音訊輸出契約，包含來源唯讀、輸出已綁定 FD、取消/逾時、輸出上限、診斷上限、退出碼與 child reaping。

Linux spawn/FD 映射需獨立設計及測試，包括 close-on-exec、來源與映射 FD 碰撞、所有權、取消後不殘留程序、路徑替換/連結攻擊，以及不意外繼承其他開啟檔案。Linux 既有 `BoundDirectory` 實作通過測試前不能宣稱專案寫入安全。不要為重用 Darwin 程式碼而放寬既有安全限制。

保留影片匯入、片段命名即時編輯、修剪/分割/合併、字幕、GIF、MP4 與網頁播放器；既有專案格式不變。Linux 預覽聲音輸出與混音試聽需獨立驗證，不能只驗證 FFmpeg 回傳 0。成功後大小與磁碟資訊需實際更新。

## 教鞭、標註與快捷鍵

先完成錄影安全與完整媒體流程，再處理桌面互動，不讓選配標註故障中止正常錄影。X11 可試驗對應的游標與透明視窗操作；Wayland 不假定能任意取得全域座標、改其他應用程式游標或強制置頂穿透。

目標仍包含 Linux 教鞭與標註，不把它們默默刪除。Wayland 能力研究若證明無法用安全、一般使用者權限達成既有體驗，交付明確的限制與替代方案，由使用者決定是否接受部分功能的 Linux 測試版；不因此把 Ubuntu 目標縮成 X11-only，不要求 root、任意裝置讀取或輸入注入以繞過限制。

快捷鍵在 X11、Wayland 分別探測與註冊；Wayland 檢查 GlobalShortcuts portal 是否可用，無可用全域方案時明確標示，保留主視窗按鈕與取得焦點時的操作。仍遵守使用者自訂快捷鍵及 Esc 清除結束繪圖的既有語意。未成功註冊不得顯示成可用。

## 階段、門檻與回歸

各階段在隔離開發分支，未達正式門檻不合併；使用者已允許嘗試開發，不等於允許發布。預計階段是實作順序，不是已完成紀錄。

1. **平台與原生核心可行性**：實際 Linux provider、runtime/helper build、X11/Wayland 最小來源及音源初始化、Linux owned-output 檢查。先驗證 headless OBS portal/main-loop 風險。產出可重現 evidence，不預先宣稱 parity。
2. **安全錄影與媒體工作流**：雙音源/靜音、預檢、20/30 FPS、暫停續錄、專案/剪輯/匯入/輸出/救援，含軟體編碼。初步 Linux 測試包標記未驗證項目。
3. **桌面功能與完整驗收**：教鞭、標註、自訂區域、多螢幕/縮放、快捷鍵、三語說明、診斷/授權及完整跨平台回歸；任何功能限制需明確決策。

### 自動化證據

- 工廠/platform/runtime schema 表格測試：win-x64 仍選原本 Windows 物件與 WGC/DXGI，osx-arm64 仍選原本 macOS 物件與 SCK；linux-x64 才使用新元件。其他未支援平台明確拒絕。
- 舊平台 native CMake/script/runtime/plugin/encoder 設定差異審查；包內容排除 Linux 私有依賴，Linux 包不帶 Windows/macOS native 檔案。
- Windows/macOS 既有 Core、Media、native、packaging、網站/播放器測試全跑；Linux 額外加平台、安全、程序與真實 OBS 虛擬 X11/虛擬音源整合測試。失敗查證，不直接跳過測試。
- Ubuntu GNOME Wayland 桌面整合測試與實體來源驗收獨立，不讓 Xvfb/合成音源替代 Wayland、麥克風或主觀音畫驗證。
- artifact 附基準/測試 commit、依賴、測試矩陣與 evidence；FAIL、BLOCKED、REQUIRES MANUAL VALIDATION 與 PASS 分開。

### 實機必驗矩陣

Windows x64、macOS Apple Silicon：各至少一次 5 分鐘 30 FPS 錄影及 20 FPS 對照，含畫面動態、雙音源、靜音開始、聲音停止與恢復、多次暫停續錄、教鞭/標註、停止後匯出，確認既有操作未退步。缺實機環境時不可宣稱通過，不自行操作不在授權範圍的電腦。

Ubuntu 24.04 x64：在 GNOME Wayland 及 X11 各做上述測試；另加單系統靜音開始、單麥克風、無音源、權限取消/拒絕後重試、預檢取消、麥克風拔除/換預設、螢幕移除、區域設定重啟、無內容失敗重試與已有片段續錄失敗保全。至少一次 30 分鐘音畫同步及檔案成長測試，正式宣稱長時間穩定前擴充耐久測試。

MKV 與 MP4 檢查完整解碼、packet PTS/DTS/duration（依容器/編碼重排語意判讀）、媒體時長、選取音軌與可聽內容、實際畫面變動及同步標記。不得將所有 packet PTS 強制嚴格遞增當通用驗收，也不以 live MKV format.duration 空白單獨判損壞。可重現同步標記目標：固定偏移與 5/30 分鐘漂移均不超過 100ms，超標保留量測並阻擋宣稱同步通過，不用單純相同 stream duration 代替實際對齊。

## 回復、合併與發佈政策

開發工作不修改正式 master/tag/Release，既有下載不變。共用改動分小型可撤回 commit，平台接線與 Linux-only 元件分開。若 Windows/macOS 回歸，先撤回引入回歸的共用修改或修正後重新驗證，不要求原使用者改設定繞過。

需要增加依賴、變更原生協定、修改舊平台生產行為、降低安全檢查、接受 Wayland 功能縮減或對外發布，必須記錄新決策，不用本文件預先授權所有擴張。

目前主機是 macOS，無已確認可用的實體 Ubuntu/Windows 桌面；GitHub Ubuntu runner/Xvfb 只提供自動整合環境。開發過程可交付測試包與人工步驟，但實機項目保持未驗證直到實際執行。

## Builder 可自行決定的細節

Linux 類別內部命名、D-Bus binding 選型、測試 fixture 結構、tar layout 細節、軟體編碼調校起點，可在維持上述契約與安全限制下決定並記錄。優先沿用既有介面，避免泛用平台框架重構。新第三方依賴先審查來源/授權及 Linux-only 範圍。

## 參考資料（已查閱，須對 Ubuntu 實際版本能力探測）

- [既有 Ubuntu 試封裝與實測結果](../../testing/ubuntu-packaging-spike.md)
- [XDG ScreenCast portal：授權、session、PipeWire 與游標模式](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html)
- [XDG GlobalShortcuts portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html)
- [OBS 33.0.0 PipeWire 實作](https://github.com/obsproject/obs-studio/blob/33.0.0/plugins/linux-pipewire/pipewire.c)
- [OBS 33.0.0 XSHM 實作](https://github.com/obsproject/obs-studio/blob/33.0.0/plugins/linux-capture/xshm-input.c)
- [Avalonia Linux 文件](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)：目前專案版本仍須各自核對，不藉此升級框架；UI 使用 XWayland 不代表可用 X11 來源擷取整個 Wayland 桌面。
