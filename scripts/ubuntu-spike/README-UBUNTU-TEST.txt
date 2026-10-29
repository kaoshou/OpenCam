OpenCam Ubuntu 24.04 x64 — 封裝可行性測試 / PACKAGING SPIKE

重要：不是可用的 Linux 錄影版，不是正式 Release。
目前 Linux OBS runtime、畫面／音訊來源及專案平台接線尚未實作。
能啟動視窗不代表可以錄影、剪輯、試聽或輸出。請勿用來錄製重要內容。
本包保留 NativeOnly 安全限制；未放回舊 FFmpeg 錄影核心。

此 tarball 包含自帶 .NET runtime 的 linux-x64 程式；不適用 ARM64。
在 Ubuntu 24.04 的桌面環境完整解壓，再從終端機執行：
  ./Start-OpenCam.sh

若桌面相依套件不足，可先手動安裝（此啟動器不會自行安裝）：
  sudo apt install libice6 libsm6 libfontconfig1 libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 libxrender1 libgl1

測試設定與資料隔離在：
  ${XDG_STATE_HOME:-$HOME/.local/state}/OpenCam-Ubuntu-Spike
或以 OPENCAM_UBUNTU_TEST_DATA 指定另外的空目錄。

請回報：Ubuntu 版本、X11／Wayland、CPU/GPU、終端機錯誤、視窗截圖。
diagnostics/platform-probe.json 是實際 Ubuntu 執行的能力／安全探測結果。
若只有 screenshot-mode.png，只代表示範渲染，不是正常啟動或錄影成功。
normal-startup.json 與 normal-window-tree.txt 才是一般啟動測試的觀察。

本次不附 OBS／FFmpeg Linux 二進位，因為應用程式還不能使用 Linux 擷取平台。
未驗證 Wayland、實體音源、多螢幕、同步、硬體編碼、教鞭與螢幕註記。
LICENSE、NOTICE.md、THIRD-PARTY-NOTICES.md 與 SOURCE.txt 保留授權及對應來源。

This is a diagnostic packaging experiment, NOT a working Linux recorder.
Keep the whole extracted folder. The .NET runtime is bundled; OS GUI libraries
are required. Recording stays fail-closed and no legacy capture is enabled.
Read diagnostics before testing. Do not use for important recordings.
