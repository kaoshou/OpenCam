# OpenCam（螢幕錄影工具）

[![Latest Release](https://img.shields.io/github/v/release/kaoshou/OpenCam?display_name=tag&sort=semver)](https://github.com/kaoshou/OpenCam/releases/latest)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/License-AGPL--3.0--or--later-blue.svg)](LICENSE)

[English](#english) ｜ [官方網站](https://kaoshou.github.io/OpenCam/) ｜ [下載最新版](https://github.com/kaoshou/OpenCam/releases/latest) ｜ [完整使用說明](docs/USER_GUIDE.zh-TW.md)

OpenCam 是 Windows 與 macOS 桌面螢幕錄影工具，讓你錄影、剪輯，再繼續錄影。錄影片段與剪輯保存在可重開的專案中，需要時可輸出 MP4、GIF 或附帶章節與字幕的網頁播放器。

## v0.3.12 更新重點

- 原本影片與編輯視窗的「輸出…」在 MP4 下方新增 GIF。
- 片段清單與時間軸右鍵的「匯出片段…」可滑鼠移入展開，再選擇 MP4 或 GIF；只匯出該片段的保留範圍，合併片段包含全部子段。
- GIF 可選寬度與 FPS，預設為 640／15 FPS，保持比例並循環播放。GIF 沒有音訊，不限制長度；達五分鐘會提醒轉換時間、檔案大小與無音訊，確認後仍可繼續。
- 錄製前檢查改為確認系統聲音與麥克風，提供五秒聲音試錄及回放。
- 音源卡片下方新增獨立狀態區，顯示載入、試錄啟動、收音、回放與正式錄製準備；實際啟動後才開始五秒計時。

[完整更新內容](docs/releases/v0.3.12.md) ｜ [歷史版本](https://github.com/kaoshou/OpenCam/releases)

## 主要功能

- **螢幕與區域錄影**：錄製全螢幕、指定顯示器或自訂矩形區域，提供螢幕編號辨識、30／60 FPS 與游標效果。
- **系統聲音與麥克風**：獨立開關音源，查看各自的即時音量，錄製前可試錄及回放確認聲音；可啟用麥克風連續無聲 30 秒提醒。
- **專案與繼續錄影**：命名、儲存、重開專案，暫停後編輯，或在既有內容上追加錄影。
- **影片剪輯**：片段排序、修剪、分割、合併、群組、刪除範圍、復原／重做與改名；可調整音量、靜音、淡入淡出、裁切、縮放及位置。
- **預覽與重點標記**：可調整畫質與預覽大小、全螢幕播放及拖曳定位；錄影時按「標記重點」或快捷鍵（預設 Ctrl+Shift+M），稍後在編輯器定位、改名或刪除。
- **多語字幕與搜尋**：匯入 SRT／VTT，切換字幕語言及外觀；網頁播放器提供逐字稿搜尋、關鍵字標示與點擊跳轉。
- **多種輸出**：整個影片或單一片段可輸出 MP4／GIF；整部影片也可輸出為離線網頁播放器。可設定停止後自動輸出 MP4。
- **錄製內容保存**：原始影片以 MKV 分段保存，剪輯不覆寫素材；提供中斷救援、磁碟空間提醒及錄影中的關閉保護。
- **硬體編碼**：支援 NVIDIA NVENC、Intel QSV、AMD AMF 與 Apple VideoToolbox，並提供 CPU 編碼。
- **繁體中文與英文介面**。

## 下載與安裝

| 平台 | 系統需求 | 下載選項 |
| --- | --- | --- |
| Windows | Windows 10／11，x64 | Setup 安裝版、Portable 便攜版 |
| macOS | macOS 13 以上，Apple Silicon／arm64 | DMG；不適用 Intel Mac |

到 [最新 Release](https://github.com/kaoshou/OpenCam/releases/latest) 下載。套件包含所需的 .NET Runtime 與媒體工具，無須另外安裝。

- **Windows 安裝版**：執行 `OpenCam_v0.3.12_Windows_x64_Setup.exe`。
- **Windows 便攜版**：完整解壓 `OpenCam_v0.3.12_Windows_x64_Portable.zip`，保留同資料夾的所有檔案，再執行 `OpenCam.exe`。
- **macOS**：打開 `OpenCam_v0.3.12_macOS_arm64.dmg`，將 OpenCam 拖入「應用程式」。首次使用時，在「系統設定 → 隱私權與安全性」允許螢幕／系統音訊錄製及麥克風權限；變更權限後可重新啟動程式。
- 如作業系統顯示開啟提醒，請先確認檔案來自本專案的官方 Releases，再依系統提示操作。

## 開始使用

1. 選擇螢幕或錄影範圍、系統聲音與麥克風。
2. 按「開始錄影」，建立或使用目前專案，依聲音檢查與準備狀態開始錄製。
3. 錄影中可暫停、繼續及標記重點；暫停並保存片段後可開啟編輯器。
4. 完成後停止錄影，可自動輸出 MP4，或稍後透過「輸出…」選擇 MP4、GIF、網頁播放器。
5. 若只需要其中一段，在片段清單或時間軸按右鍵，移至「匯出片段…」再選擇格式。

儲存專案與輸出影片是不同操作。請保留整個專案資料夾（包含錄影片段、字幕及重點資料），不要只搬移 `project.opencam`。有字幕的專案使用格式 4，v0.3.3 及以前無法開啟；字幕不燒入 MP4／GIF，也不會自動調整時間。

網頁輸出包含 `index.html`、`media.mp4` 與 `opencam-player.js`，請一起搬移或上傳。GIF 沒有音訊；長影片或原始解析度需要較多轉換時間與暫存磁碟空間。相容完整片段可快速輸出，裁剪與效果等操作可能需要重新轉檔。

## 介面預覽

以下為 v0.3.5 的介面示意，最新功能請參考上方介紹及使用說明。點選圖片可查看原尺寸。

[![錄影主畫面（v0.3.5）](docs/images/preview_main_zhtw.png)](docs/images/preview_main_zhtw.png)
[![偏好設定（v0.3.5）](docs/images/preview_settings_zhtw.png)](docs/images/preview_settings_zhtw.png)
[![錄製內容編輯器（v0.3.5）](docs/images/preview_editor_zhtw.png)](docs/images/preview_editor_zhtw.png)
[![多語字幕管理（v0.3.5）](docs/images/preview_subtitles_zhtw.png)](docs/images/preview_subtitles_zhtw.png)

## 授權

OpenCam 自有原始碼採 [GNU Affero General Public License v3.0 或後續版本](LICENSE)（AGPL-3.0-or-later）。版權與原始碼資訊見 [NOTICE.md](NOTICE.md)；FFmpeg、.NET Runtime、字型及其他元件適用各自授權，詳見 [第三方聲明](THIRD-PARTY-NOTICES.md) 及套件內附文件。

軟體依「現狀」提供。錄製時請遵守適用的隱私、保密與著作權規範。

---

## English

[Official website](https://kaoshou.github.io/OpenCam/) | [Download latest](https://github.com/kaoshou/OpenCam/releases/latest) | [User guide](docs/USER_GUIDE.en-US.md)

OpenCam is a desktop screen recorder for Windows and macOS. Record, edit, and continue recording in a project you can save and reopen, then export MP4, GIF, or a Web player with chapters and captions.

### What's new in v0.3.12

- GIF is now available below MP4 in the original video's Export menu and the editor.
- Hover **Export clip…** in clip-list or timeline context menus to select MP4 or GIF. Export respects the selected clip's retained range, including all parts of a compound clip.
- GIF offers width and frame-rate options, defaulting to 640/15 FPS, with preserved aspect ratio and continuous looping. It has no audio or duration cap. At five minutes or longer, a confirmation explains conversion time, file size and missing audio before continuing.
- Preflight now checks system audio and microphone through a five-second audio trial and replay.
- A separate status area shows loading, trial startup, capture, replay and formal recording preparation. The five-second timer begins when capture actually starts.

[Full release notes](docs/releases/v0.3.12.md) | [Previous releases](https://github.com/kaoshou/OpenCam/releases)

### Features

- **Screen and region capture**: Record a whole screen, selected display or custom rectangle, with display identification, 30/60 FPS and cursor effects.
- **System audio and microphone**: Enable each input independently, view separate live levels, and check sound through a preflight trial and replay. Optional reminders warn after 30 seconds of microphone silence.
- **Projects and continued recording**: Name, save and reopen projects, edit after pausing, and append new recordings.
- **Video editing**: Reorder, trim, split, merge, group, delete ranges, undo/redo and rename clips. Adjust volume, mute, fades, crop, scale and position.
- **Preview and bookmarks**: Adjust quality/size, play fullscreen and scrub. Mark important moments while recording using the button or shortcut (default Ctrl+Shift+M), then seek, rename or delete them in the editor.
- **Multilingual captions and search**: Import SRT/VTT, select language/appearance, and search transcripts with highlighting and click-to-seek in Web exports.
- **Export options**: Export the whole video or a selected clip as MP4/GIF, or the whole video as an offline Web player. Optional automatic MP4 export on stop.
- **Recording preservation**: Keep original MKV segments while editing, with interruption recovery, disk-space reminders and close protection during recording.
- **Hardware encoding**: NVIDIA NVENC, Intel QSV, AMD AMF and Apple VideoToolbox, with CPU encoding available.
- **Traditional Chinese and English interfaces**.

### Download and install

| Platform | Requirements | Downloads |
| --- | --- | --- |
| Windows | Windows 10/11, x64 | Setup installer or Portable ZIP |
| macOS | macOS 13+, Apple Silicon/arm64 | DMG; not for Intel Macs |

Download from the [latest Release](https://github.com/kaoshou/OpenCam/releases/latest). Packages include the required .NET Runtime and media tools.

- **Windows installer**: Run `OpenCam_v0.3.12_Windows_x64_Setup.exe`.
- **Windows portable**: Fully extract `OpenCam_v0.3.12_Windows_x64_Portable.zip`, keep all companion files together, then run `OpenCam.exe`.
- **macOS**: Open `OpenCam_v0.3.12_macOS_arm64.dmg` and drag OpenCam into Applications. Allow screen/system-audio recording and microphone access in System Settings → Privacy & Security. Restart OpenCam after changing permissions if needed.
- If the operating system shows an opening prompt, confirm the download came from this project's official Releases and follow the system instructions.

### Getting started

1. Choose the recording display/region and audio inputs.
2. Select Start Recording, create or use the current project, and follow the audio-check/preparation status.
3. Pause, resume or add bookmarks while recording. Open the editor after pausing and saving a segment.
4. Stop to export MP4 automatically, or use Export… later for MP4, GIF or Web player.
5. To export one clip, right-click it in the clip list or timeline and hover Export clip… to choose a format.

Saving a project does not export a video. Keep the entire project folder, including recordings, subtitles and bookmarks, rather than moving only `project.opencam`. Captioned projects use format 4, incompatible with v0.3.3 and earlier. Captions are not burned into MP4/GIF or automatically retimed.

Web exports contain `index.html`, `media.mp4` and `opencam-player.js`; move or upload them together. GIF has no audio, and long videos or original-resolution exports require more time and temporary disk space. Compatible complete clips can use fast export, while trims and effects may require rendering.

### Interface preview

These illustrations show v0.3.5. See the current feature descriptions and user guide for the latest functionality. Select an image to view full size.

[![Recording home (v0.3.5)](docs/images/preview_main_enus.png)](docs/images/preview_main_enus.png)
[![Preferences (v0.3.5)](docs/images/preview_settings_enus.png)](docs/images/preview_settings_enus.png)
[![Recording editor (v0.3.5)](docs/images/preview_editor_enus.png)](docs/images/preview_editor_enus.png)
[![Multilingual subtitle manager (v0.3.5)](docs/images/preview_subtitles_enus.png)](docs/images/preview_subtitles_enus.png)

### License

OpenCam-owned source code is licensed under the [GNU Affero General Public License v3.0 or later](LICENSE) (AGPL-3.0-or-later). See [NOTICE.md](NOTICE.md) for copyright/source information and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) plus the bundled notices for FFmpeg, .NET Runtime, fonts and other components.

The software is provided as is. Follow applicable privacy, confidentiality and copyright laws when recording.
