# SPDX-License-Identifier: AGPL-3.0-or-later
$ErrorActionPreference = "Stop"

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$InstallerDir = Join-Path $ProjectRoot "installer"
$PublishDir = Join-Path $InstallerDir "publish"

# SOURCE.txt must not identify HEAD when the packaged inputs differ from HEAD.
$TrackedChanges = @(git -C $ProjectRoot status --porcelain --untracked-files=no)
$UntrackedBuildInputs = @(git -C $ProjectRoot ls-files --others --exclude-standard -- src scripts installer .github)
if ($LASTEXITCODE -ne 0 -or $TrackedChanges.Count -gt 0 -or $UntrackedBuildInputs.Count -gt 0) {
    throw "發布打包前請先提交程式碼變更；SOURCE.txt 必須對應實際原始碼版本"
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " OpenCam v0.2.0 獨立發布打包腳本" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. 清理舊的發布檔案
if (Test-Path $PublishDir) {
    Write-Host "[1/3] 清理舊的發布資料夾..." -ForegroundColor Yellow
    Remove-Item -Path $PublishDir -Recurse -Force
}

# 2. 執行 dotnet publish
Write-Host "[2/3] 正在發佈 (Self-contained) .NET 8 應用程式..." -ForegroundColor Yellow
$CsprojPath = Join-Path $ProjectRoot "src\ScreenRecorder.UI\ScreenRecorder.UI.csproj"

# 發布為單一執行檔、無須依賴系統安裝 .NET 8，隱藏控制台視窗，且支援 x64
dotnet publish $CsprojPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded `
    -o $PublishDir

Copy-Item (Join-Path $ProjectRoot "LICENSE") (Join-Path $PublishDir "LICENSE")
Copy-Item (Join-Path $ProjectRoot "LICENSE") (Join-Path $PublishDir "LICENSE.txt")
Copy-Item (Join-Path $ProjectRoot "NOTICE.md") (Join-Path $PublishDir "NOTICE.md")
$Revision = (git -C $ProjectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $Revision -notmatch '^[0-9a-fA-F]{40}$') {
    throw "無法確認對應的 Git 原始碼版本"
}
@"
OpenCam 0.2.0
SPDX-License-Identifier: AGPL-3.0-or-later
Corresponding source: https://github.com/kaoshou/OpenCam/tree/$Revision
"@ | Set-Content (Join-Path $PublishDir "SOURCE.txt") -Encoding utf8

Write-Host "`n[3/3] 發佈完成！檔案已存放於 $PublishDir" -ForegroundColor Green

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "接下來，請依照以下步驟產出安裝精靈 (Setup.exe)："
Write-Host "1. 請確保您已下載並安裝 [Inno Setup 6] (https://jrsoftware.org/isdl.php)"
Write-Host "2. 進入 $InstallerDir 目錄"
Write-Host "3. 點擊兩下開啟 OpenCam.iss"
Write-Host "4. 在 Inno Setup 中點擊上方的 [Build] -> [Compile] (或按 Ctrl+F9)"
Write-Host "5. 完成後，安裝檔將會產生在 $InstallerDir\Output\OpenCam_v0.2.0_Setup.exe"
Write-Host "==========================================" -ForegroundColor Cyan
