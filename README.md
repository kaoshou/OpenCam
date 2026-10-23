# OpenCam（螢幕錄影工具）

[![Latest Release](https://img.shields.io/github/v/release/kaoshou/OpenCam?display_name=tag&sort=semver)](https://github.com/kaoshou/OpenCam/releases/latest)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/License-AGPL--3.0--or--later-blue.svg)](LICENSE)

[English](#english) ｜ [官方網站](https://kaoshou.github.io/OpenCam/) ｜ [下載最新版](https://github.com/kaoshou/OpenCam/releases/latest) ｜ [完整使用說明](docs/USER_GUIDE.zh-TW.md)

OpenCam 是 Windows 與 macOS 桌面螢幕錄影工具，讓你錄影、剪輯，再繼續錄影。錄影片段與剪輯保存在可重開的專案中，需要時可輸出 MP4、GIF 或附帶章節與字幕的網頁播放器。

## v0.4.1 更新重點

- 網頁播放器新增閱讀設定浮層：字級、行距、深色高對比與暖白背景，預設關閉，不擠壓逐字稿閱讀空間。
- 可下載所選語言的完整閱讀版 HTML 或純文字 TXT，支援離線閱讀、列印及選擇是否包含時間標記。
- 改善播放器鍵盤操作、焦點提示、字幕設定關閉操作，以及手機、平板橫直向版面。
- 官方網站與使用說明改善鍵盤導覽、文字對比及窄螢幕排版。

[完整更新內容](docs/releases/v0.4.1.md) ｜ [歷史版本](https://github.com/kaoshou/OpenCam/releases)

## 主要功能

- **螢幕與區域錄影**：錄製全螢幕、指定顯示器或自訂矩形區域，提供螢幕編號辨識、5／10／20／30／40 FPS（預設 10 FPS）與游標效果。
- **高效能擷取**：影片、動畫或遊戲的一般擷取不夠順暢時可嘗試開啟；一般操作與簡報可維持關閉。Windows 使用 WGC，開啟後優先於 GDI／桌面擷取選單；Mac 使用 ScreenCaptureKit，搭配自動或 Apple 硬體編碼。不會提高畫質或保證 FPS，若表現較差可切回一般擷取。
- **系統聲音與麥克風**：獨立開關音源，查看各自的即時音量，錄製前可試錄及回放確認聲音。可選關閉確認、僅開始錄影時確認（預設），或開始及暫停恢復時都確認；另可啟用麥克風連續無聲 30 秒提醒。
- **專案與繼續錄影**：命名、儲存、重開專案，暫停後編輯，或在既有內容上追加錄影。
- **影片剪輯**：片段排序、修剪、分割、合併、群組、刪除範圍、復原／重做與改名；可調整音量、靜音、淡入淡出、裁切、縮放及位置。
- **預覽與重點標記**：可調整畫質與預覽大小、全螢幕播放及拖曳定位；錄影時按「標記重點」或快捷鍵（預設 Ctrl+Shift+M），稍後在編輯器定位、改名或刪除。
- **多語字幕與搜尋**：匯入 SRT／VTT，切換字幕語言及外觀；網頁播放器提供逐字稿搜尋、關鍵字標示與點擊跳轉。
- **多種輸出**：整個影片或單一片段可輸出 MP4／GIF；整部影片也可輸出為離線網頁播放器。可設定停止後自動輸出 MP4。
- **錄製內容保存**：原始影片以 MKV 分段保存，剪輯不覆寫素材；提供中斷救援、磁碟空間提醒及錄影中的關閉保護。
- **硬體編碼**：支援 NVIDIA NVENC、Intel QSV、AMD AMF 與 Apple VideoToolbox，並提供 CPU 編碼。
- **匯出品質**：選擇 MP4 或網頁播放器後，可選保留目前設定、標準壓縮或較小檔案，以及原始／最高 1080p／最高 720p。記住上次選擇，保留比例且不放大；壓縮需重新編碼，檔案大小依內容而異。停止後自動輸出仍沿用原設定。
- **繁體中文、英文與日文介面**：時間軸片段右鍵也提供更改名稱、刪除及 MP4／GIF 匯出。

## 下載與安裝

| 平台 | 系統需求 | 下載選項 |
| --- | --- | --- |
| Windows | Windows 10／11，x64 | Setup 安裝版、Portable 便攜版 |
| macOS | macOS 13 以上，Apple Silicon／arm64 | DMG；不適用 Intel Mac |

到 [最新 Release](https://github.com/kaoshou/OpenCam/releases/latest) 下載。套件包含所需的 .NET Runtime 與媒體工具，無須另外安裝。

- **Windows 安裝版**：執行 `OpenCam_v0.4.1_Windows_x64_Setup.exe`。
- **Windows 便攜版**：完整解壓 `OpenCam_v0.4.1_Windows_x64_Portable.zip`，保留同資料夾的所有檔案，再執行 `OpenCam.exe`。
- **macOS**：打開 `OpenCam_v0.4.1_macOS_arm64.dmg`，將 OpenCam 拖入「應用程式」。首次使用時，在「系統設定 → 隱私權與安全性」允許螢幕／系統音訊錄製及麥克風權限；變更權限後可重新啟動程式。
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

### What's new in v0.4.1

- A closed-by-default reading settings overlay adds text size, spacing, dark contrast and warm backgrounds without shrinking the transcript.
- Download the complete selected-language transcript as standalone HTML or plain TXT for offline reading and printing, with optional timestamps.
- Improved player keyboard navigation, focus indicators, caption-menu dismissal and phone/tablet portrait and landscape layouts.
- Improved keyboard navigation, text contrast and narrow-screen layout on the website and user guides.

[Full release notes](docs/releases/v0.4.1.md) | [Previous releases](https://github.com/kaoshou/OpenCam/releases)

### Features

- **Screen and region capture**: Record a whole screen, selected display or custom rectangle, with display identification, 5/10/20/30/40 FPS (10 FPS by default) and cursor effects.
- **High-performance capture**: Try it when standard capture is not smooth for video, animation or games; leave it off for everyday tasks and slides. Windows uses WGC instead of the GDI/Desktop Duplication selection; Mac uses ScreenCaptureKit with Auto or Apple hardware encoding. It does not enhance quality or guarantee FPS; switch back if performance is worse.
- **System audio and microphone**: Enable each input independently, view separate live levels, and check sound through a trial and replay. Choose audio checks off, at recording start only (default), or at both start and resume. Optional reminders warn after 30 seconds of microphone silence.
- **Projects and continued recording**: Name, save and reopen projects, edit after pausing, and append new recordings.
- **Video editing**: Reorder, trim, split, merge, group, delete ranges, undo/redo and rename clips. Adjust volume, mute, fades, crop, scale and position.
- **Preview and bookmarks**: Adjust quality/size, play fullscreen and scrub. Mark important moments while recording using the button or shortcut (default Ctrl+Shift+M), then seek, rename or delete them in the editor.
- **Multilingual captions and search**: Import SRT/VTT, select language/appearance, and search transcripts with highlighting and click-to-seek in Web exports.
- **Export options**: Export the whole video or a selected clip as MP4/GIF, or the whole video as an offline Web player. Optional automatic MP4 export on stop.
- **Recording preservation**: Keep original MKV segments while editing, with interruption recovery, disk-space reminders and close protection during recording.
- **Hardware encoding**: NVIDIA NVENC, Intel QSV, AMD AMF and Apple VideoToolbox, with CPU encoding available.
- **Export quality**: After selecting MP4 or Web player, choose Keep current settings, Standard compression or Smaller file, with original/up to 1080p/up to 720p resolution. The last choice is remembered; aspect ratio is preserved without upscaling. Compression re-encodes and size depends on content. Automatic export on stop retains its existing settings.
- **Traditional Chinese, English and Japanese interfaces**: Timeline clip context menus also offer Rename, Delete and MP4/GIF export.

### Download and install

| Platform | Requirements | Downloads |
| --- | --- | --- |
| Windows | Windows 10/11, x64 | Setup installer or Portable ZIP |
| macOS | macOS 13+, Apple Silicon/arm64 | DMG; not for Intel Macs |

Download from the [latest Release](https://github.com/kaoshou/OpenCam/releases/latest). Packages include the required .NET Runtime and media tools.

- **Windows installer**: Run `OpenCam_v0.4.1_Windows_x64_Setup.exe`.
- **Windows portable**: Fully extract `OpenCam_v0.4.1_Windows_x64_Portable.zip`, keep all companion files together, then run `OpenCam.exe`.
- **macOS**: Open `OpenCam_v0.4.1_macOS_arm64.dmg` and drag OpenCam into Applications. Allow screen/system-audio recording and microphone access in System Settings → Privacy & Security. Restart OpenCam after changing permissions if needed.
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
