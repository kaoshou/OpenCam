# OpenCam (螢幕錄影工具) 🎥

[![Build and Release](https://img.shields.io/github/actions/workflow/status/kaoshou/OpenCam/build-and-release.yml?logo=github&label=Build%20and%20Release)](https://github.com/kaoshou/OpenCam/actions/workflows/build-and-release.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

*(English version below)*

OpenCam 是一款強調「**極致可靠性 (High-Reliability)**」的跨平台桌面螢幕錄影工具。
不論是遭遇當機、停電，還是錄影中途麥克風突然被拔除，OpenCam 都能最大程度保證您的錄影檔案安全，絕不輕易報廢。

本軟體以 .NET 8 與 Avalonia UI 打造，並使用 FFmpeg 作為核心多媒體引擎，支援 Windows 與 macOS 雙系統。

![OpenCam 主畫面](docs/images/preview_main_zhtw.png)
![OpenCam 偏好設定](docs/images/preview_settings_zhtw.png)


## 🌟 核心特色

- **防中斷安全機制 (Crash Recovery)**：強制使用 MKV 作為工作檔，若錄影途中遭遇斷電或崩潰，下次啟動可自動恢復，絕不丟失已錄內容。
- **音訊熱拔插防護 (Audio Hotplug Watchdog)**：錄影途中若不慎拔除 USB 麥克風，系統將自動啟動「虛擬靜音補償 (anullsrc)」，保持錄影不中斷，最終影片仍可完美無縫轉出。
- **動態切換麥克風**：錄影途中若遭遇意外，只需按下暫停即可在設定中立刻更換新的音訊來源，按下繼續後無縫錄影。
- **硬體加速支援 (Hardware Encoding)**：支援 NVIDIA (NVENC)、Intel (QSV)、AMD (AMF) 以及 Apple Silicon (VideoToolbox)，錄製不掉幀。
- **自動無損轉檔**：錄影正常結束後，將自動進行 Stream Copy，快速將安全的 MKV 封裝 (Remux) 轉換為普及的 MP4，不重新編碼、不損失畫質。
- **磁碟守護機制 (Disk Monitor)**：在磁碟空間耗盡前發出警告，並在臨界點啟動主動安全停止，避免因為 0 bytes remaining 導致檔案毀損。
- **輕量與跨平台**：支援 Windows 10/11 (x64) 以及 macOS (Apple Silicon)。

## 🚀 系統需求與安裝

### Windows 10/11
- 前往 [Releases](https://github.com/kaoshou/OpenCam/releases) 頁面。
- 下載 OpenCam_*_Setup.exe (安裝版) 或 OpenCam_Windows_Portable.zip (免安裝版)。
- 執行應用程式（內建所需的 .NET Runtime，無需額外安裝）。

### macOS (Apple Silicon M1/M2/M3)
- 前往 [Releases](https://github.com/kaoshou/OpenCam/releases) 頁面。
- 下載 OpenCam_macOS_AppleSilicon.dmg。
- 打開 DMG 檔案，將 OpenCam 拖曳至您的「應用程式」資料夾。

## 🛠️ 技術架構
- **UI 框架**: Avalonia UI, CommunityToolkit.Mvvm
- **框架語言**: C#, .NET 8
- **核心引擎**: FFmpeg (獨立進程，支援 dshow 與 avfoundation)
- **日誌**: Serilog

## ⚖️ 授權與免責聲明

- **開源授權**: 本專案採用 [Apache License 2.0](LICENSE) 授權條款發布。
- **第三方聲明**: 本軟體核心多媒體處理使用了 [FFmpeg](http://ffmpeg.org) 的程式碼，受 GPLv3 / LGPLv3 授權保護。原始碼可從其官方網站下載。特別感謝 Avalonia UI, CommunityToolkit.Mvvm 與 Serilog 的貢獻者。
- **免責聲明與使用條款**: 
  1. 本軟體按「原樣 (AS IS)」提供，不帶任何明示或暗示的擔保。
  2. 作者不對使用本軟體造成的任何資料遺失、硬體損壞或衍生性損失負責。
  3. 使用者必須自行確保錄影行為遵守當地之隱私權與機密保護法律，請勿錄製未經授權的受版權保護內容或侵犯他人隱私。
  4. 使用本軟體即代表您同意上述條款。

---

# OpenCam (Screen Recorder) 🎥 (English)

OpenCam is a cross-platform desktop screen recording tool built with a focus on **High-Reliability**. 
Whether you encounter a crash, power outage, or accidentally unplug your microphone during recording, OpenCam ensures your footage remains safe and recoverable.

Built with .NET 8, Avalonia UI, and FFmpeg, it supports both Windows and macOS.

![OpenCam Main Window](docs/images/preview_main_enus.png)
![OpenCam Settings Window](docs/images/preview_settings_enus.png)


## 🌟 Key Features

- **Crash Recovery**: Uses MKV as a safe working container. If the app or OS crashes, your footage is preserved and can be recovered on the next launch.
- **Audio Hotplug Protection**: If your USB microphone is disconnected during a session, OpenCam automatically falls back to a "virtual silence track" to keep the recording process alive and ensure the final MP4 concatenates flawlessly.
- **Dynamic Mic Switching**: Pause your recording at any time to switch to a different audio device without breaking your final video file.
- **Hardware Acceleration**: Out-of-the-box support for NVIDIA (NVENC), Intel (QSV), AMD (AMF), and Apple Silicon (VideoToolbox).
- **Auto-Remuxing**: Automatically remuxes the safe MKV to a highly compatible MP4 format instantly upon stopping, without re-encoding.
- **Disk Space Monitor**: Actively monitors disk space and safely finalizes your recording before the drive runs out of space to prevent file corruption.

## 🚀 Installation

### Windows 10/11
- Go to the [Releases](https://github.com/kaoshou/OpenCam/releases) page.
- Download OpenCam_*_Setup.exe (Installer) or OpenCam_Windows_Portable.zip (Portable).
- Run the application (Self-Contained, no separate .NET runtime required).

### macOS (Apple Silicon)
- Go to the [Releases](https://github.com/kaoshou/OpenCam/releases) page.
- Download OpenCam_macOS_AppleSilicon.dmg.
- Mount the DMG and drag OpenCam to your Applications folder.

## ⚖️ License & Disclaimers

- **License**: This project is licensed under the [Apache License 2.0](LICENSE). 
- **Third-Party Acknowledgements**: This software uses code of [FFmpeg](http://ffmpeg.org) licensed under the GPLv3 / LGPLv3 and its source can be downloaded from their official website. Special thanks to Avalonia UI, CommunityToolkit.Mvvm, and Serilog contributors.
- **Disclaimer**: 
  This software is provided "AS IS", without warranty of any kind. The authors are not responsible for any damage or data loss. Please comply with your local privacy and security laws when recording sensitive information.


