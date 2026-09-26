# OpenCam（螢幕錄影工具）🎥

[![Latest Release](https://img.shields.io/github/v/release/kaoshou/OpenCam?display_name=tag&sort=semver)](https://github.com/kaoshou/OpenCam/releases/latest)
[![Build and Release](https://img.shields.io/github/actions/workflow/status/kaoshou/OpenCam/build-and-release.yml?logo=github&label=Build%20and%20Release)](https://github.com/kaoshou/OpenCam/actions/workflows/build-and-release.yml)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/License-AGPL--3.0--or--later-blue.svg)](LICENSE)

*（English version below）*

OpenCam 是一款以可靠性為優先的跨平台桌面螢幕錄影工具。錄影期間以 MKV 作為安全工作檔，正常停止後再無損封裝為 MP4，以降低當機、停電或音訊裝置異常時的資料損失風險。

本軟體以 .NET 8 與 Avalonia UI 打造，並使用 FFmpeg 作為核心多媒體引擎，支援 Windows 與 macOS。

📖 [完整使用說明（繁體中文）](docs/USER_GUIDE.zh-TW.md) ｜ [English User Guide](docs/USER_GUIDE.en-US.md)

🌐 [OpenCam 官方網站](https://kaoshou.github.io/OpenCam/)（可切換繁體中文／English）

目前原始碼版本為 **0.2.3**，採用 **AGPL-3.0-or-later**。已發布的 Windows 與 Apple Silicon macOS 安裝包請至 [Releases](https://github.com/kaoshou/OpenCam/releases) 下載。

## v0.2.3 安全修補與驗證狀態

本次修改針對本機錄影控制通訊與修復救援的檔案處理加強防護；這不代表所有安全問題已解決。目前仍待完成發布驗收，原始碼版本不代表安裝包已發布。

**影響範圍**：本次修補涵蓋 v0.2.2 的本機程序控制、救援工作階段檔案處理，以及 macOS 音訊傳輸。風險涉及同一台電腦上的其他程序或遭竄改的工作階段資料，並非已確認的遠端攻擊事件。待 v0.2.3 安裝包完成驗收並發布後，建議舊版使用者更新；更新前請保留尚未完成救援的原始 MKV 與工作階段資料。

- **錄影程序控制**：原本僅靠 pipe 名稱區分工作階段，無法充分驗證指令來源。現在透過私有啟動通道傳遞每次啟動的金鑰，檢查通訊雙方程序身分，並對訊息加入 HMAC 驗證、重播防護、大小及認證時間限制。未取得合法 UI 啟動資訊的 daemon 不允許直接啟動。
- **修復救援路徑**：工作階段中繼資料不再能指定其他目錄的 MKV；載入的資料綁定實際工作階段目錄，拒絕不安全路徑與已偵測到的連結檔。救援先透過檔案控制代碼建立暫存快照，再交給媒體工具處理，降低檢查後來源檔案被替換的風險。
- **避免意外覆寫**：救援中繼資料採用原子替換，避免透過硬連結覆寫其他檔案；修復後 MP4 驗證通過才以不覆寫方式存入目的地，原始 MKV 保留。救援快照需要額外的系統暫存磁碟空間。
- **媒體工具參數**：FFmpeg／FFprobe 改用獨立參數傳遞，並拒絕 concat 清單路徑中的換行，避免特殊檔名改變參數或清單內容的解讀。
- **介面修正**：補齊「關於本程式」版本標籤的資料綁定，修正 OpenCam 名稱旁出現空白綠框的問題。
- **救援目錄競態防護**：中繼資料、來源快照、救援鎖與輸出寫入綁定已開啟的目錄；Unix 使用相對目錄控制代碼操作，Windows 保持上層目錄控制代碼並禁止刪除共享。macOS 的工作階段、Sessions 與錄影根目錄替換測試均有涵蓋，Windows 實機仍待驗證。
- **macOS 音訊傳輸**：原始 PCM 改由匿名管線傳給 FFmpeg，不再建立可由檔名開啟的音訊 FIFO。系統聲音與麥克風仍使用獨立輸入；混音及波形訊息保留。

**已知限制與使用建議**：請使用自己控制的本機錄影目錄。自訂目錄或其上層為符號連結時，安全檢查可能拒絕操作；請改選實體目錄。私有暫存目錄無法隔離具有相同使用者權限的惡意程序，本次修改亦不防護程序注入、記憶體讀取或遭替換的應用程式／媒體工具。不要直接救援不明來源或可被他人修改的工作階段；這些風險說明不代表曾確認遭到利用。

**驗證狀態**：已加入 IPC 認證、救援路徑／目錄替換與匿名音訊傳輸自動化測試；匿名管線亦以實際 FFmpeg 驗證雙輸入混音及非零音訊。這不等於真實裝置收音驗收。實際錄影、系統音訊、麥克風、暫停／繼續與強制中斷救援的發布驗收，以及 Windows／Linux 執行驗證仍待完成；略過不代表通過。詳見 [手動驗收表](MANUAL_TEST_CHECKLIST.md)。應用程式簽章與 macOS 公證狀態未因本次修改而改變。

## 歷史更新

v0.2.2 加強錄影狀態監控、錯誤提示、版本一致性、套件安全檢查與 GitHub Actions 發布防護，並提升 macOS 新版系統的圖示封裝相容性。

v0.2.1 更新了 Windows、macOS 與網站共用的扁平式圖示，並在網站導覽列加入 GitHub 專案連結。下方主畫面顯示錄影中的收音狀態與獨立波形；偏好設定仍保留 v0.2.0 的實際畫面。

主畫面由 **0.2.1** 正式 UI 離線渲染，波形使用收音狀態展示資料；點選圖片可檢視原尺寸，避免縮小後看不清選項文字。

[![OpenCam 0.2.1 macOS 錄影中與收音波形](docs/images/preview_main_zhtw.png)](docs/images/preview_main_zhtw.png)
[![OpenCam 0.2.0 macOS 偏好設定](docs/images/preview_settings_zhtw.png)](docs/images/preview_settings_zhtw.png)

## v0.2.0 重點更新

- 錄製指定螢幕時，點選螢幕清單旁的辨識圖示，可在所有已連接顯示器上短暫顯示編號，並標示目前選取的螢幕。
- 自訂矩形選取框保持透明，可拖曳移動及調整大小。
- 錄影狀態區分開顯示系統聲音與麥克風的即時音量波形，方便確認是否收到聲音；波形不是成品音軌的品質保證。
- 錄影啟動後會鎖定不會即時生效的選項；暫停後仍可調整系統聲音、麥克風與游標樣式。
- 錄影期間關閉視窗會先提醒並保護工作階段，避免無意中斷。

v0.2.0 的完整安裝檔請至 [OpenCam v0.2.0 Release](https://github.com/kaoshou/OpenCam/releases/tag/v0.2.0) 下載。

## v0.1.5 歷史更新

- 修正錄影畫質、磁碟警告門檻與成功封裝後清理 MKV 等偏好設定未完整套用到 Recorder 的問題。
- 修正錄影正常停止後，UI 可能因最後狀態訊息而誤判為中斷的問題。
- 暫停後變更系統聲音、麥克風與游標設定時，會正確傳遞到下一個錄影片段。
- 新增繁體中文與英文完整使用說明，涵蓋錄影模式、參數、檔案機制、設定、工作檔位置及修復救援流程。
- 重整 Crash Recovery：支援目前的 `segment_*.mkv` 分段與舊版 `recording.mkv` 工作檔。
- 工作階段中繼資料遺失或損壞時，可由現存 MKV 分段重建並嘗試救援。
- 損壞或空白尾段不再阻擋整次救援；OpenCam 會保留可讀的連續內容，並清楚標示部分救回。
- 錄影、暫停、停止及 MP4 封裝期間持續更新工作階段心跳，避免把仍在執行的錄影誤判為可救援內容。
- 改善 MKV 寫入與 MP4 無損封裝的取消、長影片、特殊路徑及多實例安全性；所有原始 MKV 均會保留。
- 救援期間鎖定開始錄影與相關設定，並顯示完整救回、部分救回與失敗的正確統計。
- 錄影、暫停或初始化期間關閉程式時會顯示提醒並取消關閉，避免意外中斷錄影。
- 官方封裝優先使用 App 內附的 FFmpeg 與 FFprobe，避免系統 PATH 中的其他版本造成相容性問題。
- 修正 macOS 無音訊錄影時 FFmpeg 參數順序錯誤，指定螢幕與自訂區域皆可正常開始錄影。

完整安裝檔請至 [OpenCam Releases](https://github.com/kaoshou/OpenCam/releases) 下載。

## 🌟 核心特色

- **指定螢幕與自訂區域**：可選擇要錄製的顯示器，或使用透明且可調整大小的選取框指定錄影範圍。
- **多螢幕辨識**：可在每台已連接的顯示器上短暫顯示對應編號，辨認選中的錄影來源。
- **系統聲音與麥克風**：可分別開啟或關閉系統聲音及麥克風，並支援四種音訊組合。
- **分開的即時音量波形**：錄影時分別查看系統聲音與麥克風的收音狀態。
- **實際錄影健康監控**：狀態區依工作檔、影格、編碼器與已選音訊來源的實際進度顯示警告；剛開始或資料不足時維持未知，不會把尚未觀察到的來源誤報為正常。
- **游標效果**：可使用原始游標、光暈、光暈加點擊漣漪，或在影片中隱藏游標。
- **防中斷安全機制（Crash Recovery）**：強制使用 MKV 作為工作檔；若錄影途中斷電或崩潰，下次啟動可嘗試恢復已寫入的內容。
- **防誤關保護**：錄影中按 X 不會中斷錄影；只有在啟動狀態無法確認且安全停止失敗後，才提供需二次確認的強制結束選項。
- **異常退出安全停止**：若 UI 被系統強制終止，Recorder 會停止並收尾目前工作檔後退出，不會在使用者不知情時持續背景錄影。
- **音訊熱拔插防護（Audio Hotplug Watchdog）**：Windows 錄影途中若麥克風中斷，系統會使用虛擬靜音音軌盡可能維持錄影流程。
- **暫停期間調整**：暫停錄影後可調整系統聲音、麥克風開關及游標樣式；錄影來源、解析度、FPS 等固定設定仍保持鎖定。
- **硬體加速支援（Hardware Encoding）**：支援 NVIDIA NVENC、Intel QSV、AMD AMF 以及 Apple VideoToolbox，並在不可用時回退至 CPU 編碼。
- **自動無損轉檔**：錄影正常結束後自動使用 Stream Copy，將 MKV 快速封裝（Remux）為 MP4，不重新編碼影片。
- **磁碟守護機制（Disk Monitor）**：磁碟空間不足前發出警告，並在臨界點嘗試安全停止，降低檔案損毀風險。
- **跨平台**：支援 Windows 10/11 x64，以及搭載 Apple Silicon 的 macOS 13 Ventura 或以上版本。

## 🚀 系統需求與安裝

### Windows 10/11（x64）

- 前往 [Releases](https://github.com/kaoshou/OpenCam/releases) 頁面。
- 下載 `OpenCam_*_Setup.exe`（安裝版）或 `OpenCam_Windows_Portable.zip`（免安裝版）。
- 執行應用程式。套件已包含所需的 .NET Runtime，無須另外安裝。

### macOS 13 Ventura 或以上（Apple Silicon／arm64）

> 目前尚未提供 Intel Mac（x64）版本。

- 前往 [Releases](https://github.com/kaoshou/OpenCam/releases) 頁面並下載 `OpenCam_macOS_AppleSilicon.dmg`。
- 打開 DMG，將 `OpenCam.app` 拖曳至「應用程式（Applications）」資料夾。
- 第一次啟動時，請依 macOS 提示在「系統設定 → 隱私權與安全性」中允許 OpenCam 使用「螢幕與系統音訊錄製」及「麥克風」。變更權限後若功能尚未生效，請完全結束並重新開啟 OpenCam。
- 若 Gatekeeper 阻擋啟動，請先在 Finder 對 `OpenCam.app` 按右鍵並選擇「打開」，或在「系統設定 → 隱私權與安全性」選擇「仍要打開」。
- 只有在確認 DMG 是從本專案的官方 GitHub Releases 下載，而且系統仍顯示「OpenCam 已毀損，無法打開」時，才在終端機執行：

  ```bash
  xattr -cr /Applications/OpenCam.app
  ```

### macOS v0.2.0 注意事項

- 系統音訊錄製需要 macOS 13 或以上版本，並使用 Apple ScreenCaptureKit。
- 麥克風錄製使用 macOS 系統目前的預設輸入裝置。若要改用另一支麥克風，請先在 macOS 聲音設定中切換預設輸入裝置，再開始或暫停後繼續錄影。
- 點選「開始錄影」後，錄影來源、區域、畫質、FPS 與儲存位置會鎖定；暫停時只有系統聲音、麥克風與游標樣式可調整。

## 🛠️ 技術架構

- **UI 框架**：Avalonia UI、CommunityToolkit.Mvvm
- **框架語言**：C#、.NET 8
- **核心引擎**：FFmpeg 獨立進程；Windows 使用 gdigrab、DirectShow 與 WASAPI，macOS 使用 AVFoundation、ScreenCaptureKit 與 AVAudioEngine
- **硬體編碼**：NVENC、QSV、AMF、VideoToolbox，並提供 libx264 回退
- **日誌**：Serilog

## ⚖️ 授權與免責聲明

- **專案授權**：專案自有原始碼依 [GNU Affero General Public License v3.0 or later](LICENSE)（`AGPL-3.0-or-later`）發布；請參閱 [版權與原始碼說明](NOTICE.md)。
- **第三方元件**：發布套件包含 FFmpeg；實際適用授權取決於各平台封裝的 FFmpeg 組態。官方發布套件使用啟用 GPL 元件的 FFmpeg，包括 libx264。請參閱 [macOS FFmpeg 建置腳本](scripts/build-ffmpeg-macos-arm64.sh)、[FFmpeg Legal](https://ffmpeg.org/legal.html) 與 [x264 原始碼](https://code.videolan.org/videolan/x264)。Avalonia UI、CommunityToolkit.Mvvm、Serilog 及其他相依套件分別適用其各自授權。
- **完整第三方聲明**：[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 列出元件與授權檔案；`third-party/licenses` 保留上游授權原文、原生元件聲明及 Inter 字型 OFL。封裝時加入實際 .NET Runtime 聲明與 FFmpeg 授權／建置資訊。Windows 位於安裝目錄，macOS 位於 App 的 `Contents/Resources`。Windows 第三方 FFmpeg 建置的完整對應原始碼仍需完成發布前核對；增加聲明不代表該核對已通過。
- **免責聲明與使用條款**：本軟體按「原樣（AS IS）」提供，不帶任何明示或暗示的擔保。作者不對使用本軟體造成的資料遺失、硬體損壞或衍生性損失負責。使用者必須自行確保錄影行為遵守所在地的隱私權、機密保護與著作權法律。

---

# OpenCam (Screen Recorder) 🎥

OpenCam is a cross-platform desktop screen recorder built with reliability as its top priority. It records to a crash-resilient MKV working file and remuxes it to MP4 after a normal stop, reducing the risk of losing recorded content after a crash, power outage, or audio-device failure.

OpenCam is built with .NET 8, Avalonia UI, and FFmpeg, and supports Windows and macOS.

📖 [Complete User Guide](docs/USER_GUIDE.en-US.md) | [繁體中文使用說明](docs/USER_GUIDE.zh-TW.md)

🌐 [OpenCam official website](https://kaoshou.github.io/OpenCam/) (English / 繁體中文)

The current source version is **0.2.3**, licensed under **AGPL-3.0-or-later**. Download published Windows and Apple Silicon macOS packages from [Releases](https://github.com/kaoshou/OpenCam/releases).

## v0.2.3 Security Changes and Validation Status

This update hardens local recorder control and recovery file handling; it does not resolve every security issue. Release acceptance is still pending. The source version does not imply that installers have been published.

**Scope**: These fixes cover v0.2.2 local process control, recovery-session file handling, and macOS audio transport. The risks involve other processes on the same computer or tampered session data; they are not confirmed remote attacks. Users of older versions should update after v0.2.3 installers pass acceptance and are published. Preserve original MKVs and session data awaiting recovery before updating.

- **Recorder control**: A pipe name alone did not adequately authenticate command senders. The recorder now receives a per-launch key through a private bootstrap channel, checks both IPC peer processes, and authenticates messages with HMAC, replay protection, frame-size limits, and authentication deadlines. Direct daemon startup without a valid UI bootstrap is rejected.
- **Recovery paths**: Session metadata cannot select MKVs outside the actual session directory. Loaded metadata is bound to that directory, and unsafe paths and detected links are rejected. Recovery snapshots source media through opened file handles before invoking media tools, reducing source-substitution risks between validation and use.
- **Overwrite protection**: Atomic metadata replacement avoids overwriting unrelated files through hard links. Recovered MP4s are validated before publication without overwrite; original MKVs are preserved. Recovery snapshots require additional space on the system temporary volume.
- **Media-tool arguments**: FFmpeg and FFprobe receive structured arguments, and line breaks in concat input paths are rejected to prevent special filenames from altering argument or playlist interpretation.
- **UI fix**: Restored the About page's version bindings, fixing the empty green badge beside the OpenCam title.
- **Recovery directory races**: Metadata, source snapshots, recovery locks and publication use pinned directories: directory-relative operations on Unix and ancestor handles denying delete-sharing on Windows. macOS tests cover replacement of the session, Sessions and recordings-root directories; Windows runtime validation is pending.
- **macOS audio transport**: Anonymous pipes carry raw PCM to FFmpeg instead of named audio FIFOs. System audio and microphone retain separate inputs, mixing and waveform messages.

**Known limitations and precautions**: Use a local recordings directory you control. Symbolic links in a custom directory or its ancestors may be rejected; select a physical directory instead. Private temporary directories do not isolate malicious processes under the same user identity. Process injection, memory access and replacement of application/media-tool binaries are outside this protection. Do not directly recover unknown or attacker-writable sessions. These precautions do not imply confirmed exploitation.

**Validation**: Automated coverage includes IPC authentication, recovery path/directory substitution and anonymous audio transport. Real FFmpeg tests verify two-input mixing with nonzero audio, not actual device capture. Release acceptance for actual recording, system audio, microphone, pause/resume, and forced-interruption recovery, plus Windows/Linux runtime validation, remains pending. Skipped checks are not passes. See the [manual acceptance checklist](MANUAL_TEST_CHECKLIST.md). Application signing and macOS notarization status are unchanged.

## Previous Updates

v0.2.2 strengthens recording health monitoring, error reporting, version consistency, dependency security checks, and GitHub Actions release safeguards, while improving icon packaging compatibility with newer macOS versions.

v0.2.1 updates the shared flat icon on Windows, macOS, and the website, and adds a GitHub project link to the site navigation. The main screenshot now shows recording-time audio status and separate waveforms; the preferences image remains an actual v0.2.0 capture.

The main screen is rendered from the **0.2.1** production UI with illustrative audio-level data. Select an image to view it at full size so the interface text stays legible.

[![OpenCam 0.2.1 macOS recording screen with audio waveforms](docs/images/preview_main_enus.png)](docs/images/preview_main_enus.png)
[![OpenCam 0.2.0 macOS preferences](docs/images/preview_settings_enus.png)](docs/images/preview_settings_enus.png)

## What's New in v0.2.0

- In monitor mode, use the identify icon beside the monitor list to briefly show a numbered label on every connected display and highlight the selected one.
- The custom-region frame remains transparent and can be moved and resized.
- Separate live level waveforms for system audio and microphone help confirm input activity; they do not guarantee the quality of the finished audio track.
- Settings that cannot take effect mid-session lock as soon as recording starts; system audio, microphone, and cursor style remain adjustable while paused.
- Closing the window during recording shows a warning and protects the active session against accidental interruption.

Download the v0.2.0 installers from the [OpenCam v0.2.0 Release](https://github.com/kaoshou/OpenCam/releases/tag/v0.2.0).

## Earlier v0.1.5 Changes

- Fixed preferences that were not fully propagated to the Recorder, including video quality, disk-warning thresholds, and MKV cleanup after successful remuxing.
- Fixed a case where the UI could interpret the final status message after a normal stop as an interrupted recording.
- Changes to system audio, microphone, and cursor settings while paused are now passed correctly to the next recording segment.
- Added complete Traditional Chinese and English user guides covering recording modes, options, file handling, preferences, working-file locations, and Crash Recovery.
- Reworked Crash Recovery to support current `segment_*.mkv` recordings and legacy `recording.mkv` working files.
- Interrupted sessions can be reconstructed from surviving MKV segments when their metadata is missing or damaged.
- A damaged or empty trailing segment no longer blocks recovery of the readable continuous content; partial results are reported explicitly.
- Session heartbeats now remain current while recording, paused, stopping, and remuxing so active sessions are not offered for recovery.
- Improved MKV durability and MP4 remux cancellation, long-recording, special-path, and multi-instance handling while preserving every original MKV file.
- Recording controls are locked during recovery, with accurate counts for full, partial, and failed results.
- Closing OpenCam while recording, paused, or initializing now shows a warning and cancels the close request to prevent accidental interruption.
- Official packages prefer the bundled FFmpeg and FFprobe before falling back to versions found on the system PATH.
- Fixed the FFmpeg argument order for silent macOS recordings so monitor and custom-region capture both start correctly.

Download the installers from [OpenCam Releases](https://github.com/kaoshou/OpenCam/releases).

## 🌟 Key Features

- **Monitor and Region Capture**: Record a selected display or define an exact area with a transparent, resizable region selector.
- **Identify Displays**: Briefly show the matching number on every connected monitor so you can choose the right capture target.
- **System Audio and Microphone**: Enable or disable system audio and microphone recording independently, supporting all four audio combinations.
- **Separate Live Level Waveforms**: Check system-audio and microphone activity independently while recording.
- **Evidence-Based Recorder Health**: The status area monitors working-file growth, frames, the encoder, and selected audio sources. Insufficient startup evidence stays unknown instead of being reported as healthy.
- **Cursor Effects**: Use the native cursor, add a halo, add a halo with click ripples, or hide the cursor from the recording.
- **Crash Recovery**: Uses MKV as a resilient working container. After an interruption, OpenCam attempts to recover content that was already written.
- **Close and Exit Protection**: The normal close button is blocked during active work. If the UI is terminated abnormally, the Recorder safely stops and finalizes the current working file instead of continuing unnoticed in the background.
- **Audio Hotplug Protection**: On Windows, if a microphone disappears during recording, OpenCam uses a virtual silence track to keep the recording pipeline running whenever possible.
- **Paused-Session Adjustments**: While paused, system audio, microphone, and cursor settings can be changed. Fixed settings such as capture source, resolution, and FPS remain locked.
- **Hardware Acceleration**: Supports NVIDIA NVENC, Intel QSV, AMD AMF, and Apple VideoToolbox, with CPU encoding as a fallback.
- **Automatic Remuxing**: After a normal stop, OpenCam stream-copies the MKV working file into a compatible MP4 without re-encoding the video.
- **Disk Space Monitor**: Warns before storage is exhausted and attempts a safe stop at the critical threshold.
- **Cross-Platform**: Supports Windows 10/11 x64 and macOS 13 Ventura or later on Apple Silicon.

## 🚀 Requirements and Installation

### Windows 10/11 (x64)

- Go to the [Releases](https://github.com/kaoshou/OpenCam/releases) page.
- Download `OpenCam_*_Setup.exe` (installer) or `OpenCam_Windows_Portable.zip` (portable).
- Run OpenCam. The required .NET Runtime is included.

### macOS 13 Ventura or later (Apple Silicon/arm64)

> OpenCam does not currently provide an Intel Mac (x64) build.

- Go to the [Releases](https://github.com/kaoshou/OpenCam/releases) page and download `OpenCam_macOS_AppleSilicon.dmg`.
- Mount the DMG and drag `OpenCam.app` to the Applications folder.
- On first launch, follow the macOS prompts and allow OpenCam access to **Screen & System Audio Recording** and **Microphone** under **System Settings → Privacy & Security**. If a permission change does not take effect immediately, quit OpenCam completely and reopen it.
- If Gatekeeper blocks the app, first Control-click `OpenCam.app` in Finder and select **Open**, or select **Open Anyway** under **System Settings → Privacy & Security**.
- Only if the DMG came from this project's official GitHub Releases and macOS still reports that OpenCam is damaged, run:

  ```bash
  xattr -cr /Applications/OpenCam.app
  ```

### macOS v0.2.0 Notes

- System-audio recording requires macOS 13 or later and uses Apple ScreenCaptureKit.
- Microphone recording uses the current macOS default input device. To use another microphone, select it as the default input in macOS Sound settings before starting, or while the recording is paused.
- After **Start Recording** is selected, the capture source, region, quality, FPS, and output location are locked. Only system audio, microphone, and cursor settings can be changed while paused.

## 🛠️ Architecture

- **UI**: Avalonia UI and CommunityToolkit.Mvvm
- **Language and Runtime**: C# and .NET 8
- **Media Engine**: FFmpeg in a separate process; gdigrab, DirectShow, and WASAPI on Windows, and AVFoundation, ScreenCaptureKit, and AVAudioEngine on macOS
- **Hardware Encoding**: NVENC, QSV, AMF, and VideoToolbox, with libx264 fallback
- **Logging**: Serilog

## ⚖️ License and Disclaimers

- **Project License**: Project-owned source code is released under the [GNU Affero General Public License v3.0 or later](LICENSE) (`AGPL-3.0-or-later`). See the [copyright and source notice](NOTICE.md).
- **Third-Party Components**: Release packages include FFmpeg. The applicable FFmpeg license depends on the bundled configuration; the official release packages use GPL-enabled FFmpeg components, including libx264. See the [macOS FFmpeg build script](scripts/build-ffmpeg-macos-arm64.sh), [FFmpeg Legal](https://ffmpeg.org/legal.html), and the [x264 source repository](https://code.videolan.org/videolan/x264). Avalonia UI, CommunityToolkit.Mvvm, Serilog, and other dependencies remain subject to their respective licenses.
- **Full third-party notices**: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) indexes original license texts, native-component notices and the Inter font OFL in `third-party/licenses`. Packaging adds the selected .NET Runtime notices and FFmpeg license/build information. Find these in the Windows installation directory or macOS app's `Contents/Resources`. Complete corresponding-source coverage for the Windows third-party FFmpeg build still requires pre-release verification; adding notices does not establish that this check passed.
- **Disclaimer**: This software is provided “AS IS,” without warranty of any kind. The authors are not responsible for damage or data loss. Users are responsible for complying with applicable privacy, confidentiality, and copyright laws when recording.
