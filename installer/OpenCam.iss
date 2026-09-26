; SPDX-License-Identifier: AGPL-3.0-or-later
#define MyAppName "OpenCam 螢幕錄影工具"
#define MyAppShortcutName "OpenCam"
#ifndef MyAppVersion
  #error MyAppVersion must be provided with /DMyAppVersion=<version>
#endif
#define MyAppPublisher "Yu-Han Cheng"
#define MyAppURL "https://github.com/kaoshou/OpenCam"
#define MyAppExeName "OpenCam.exe"

[Setup]
; 唯一識別碼，請勿隨意更改
AppId={{9F5B2D2F-4A73-4E84-88A9-9C5F1C6B4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
LicenseFile=publish\LICENSE.txt
; 預設安裝目錄 (例如: C:\Program Files\OpenCam)
DefaultDirName={autopf}\OpenCam
; 開始功能表資料夾名稱
DefaultGroupName={#MyAppShortcutName}
; 升級安裝時不要沿用舊版的開始功能表資料夾名稱
UsePreviousGroup=no
; 允許使用者在安裝時跳過選擇目錄
DisableProgramGroupPage=yes
; 輸出的安裝檔位置與名稱
OutputDir=.\Output
OutputBaseFilename=OpenCam_v{#MyAppVersion}_Setup
; 壓縮方式 (高壓縮率)
Compression=lzma2/ultra64
SolidCompression=yes
; 支援 64 位元架構
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; 在控制台中顯示漂亮的圖示 (需要的話可後續加上 SetupIconFile)

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; 可選：Name: "tradchinese"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑 (Create a desktop shortcut)"; GroupDescription: "額外工作 (Additional tasks):"; Flags: unchecked

[Files]
; 將 publish 資料夾內的所有檔案打包 (需先執行 dotnet publish 產出至此)
Source: ".\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 僅移除舊版安裝程式建立的捷徑，避免升級後同時出現兩種名稱
Type: files; Name: "{autoprograms}\OpenCam 螢幕錄影工具\OpenCam 螢幕錄影工具.lnk"
Type: files; Name: "{autoprograms}\OpenCam 螢幕錄影工具\解除安裝 OpenCam 螢幕錄影工具.lnk"
Type: dirifempty; Name: "{autoprograms}\OpenCam 螢幕錄影工具"
Type: files; Name: "{autodesktop}\OpenCam 螢幕錄影工具.lnk"

[Icons]
; 應用程式選單 (開始選單) 捷徑
Name: "{group}\{#MyAppShortcutName}"; Filename: "{app}\{#MyAppExeName}"
; 解除安裝捷徑
Name: "{group}\解除安裝 {#MyAppShortcutName}"; Filename: "{uninstallexe}"
; 桌面捷徑
Name: "{autodesktop}\{#MyAppShortcutName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安裝完成後提供勾選「立即啟動」的選項
Filename: "{app}\{#MyAppExeName}"; Description: "立即啟動 {#MyAppName}"; Flags: nowait postinstall skipifsilent
