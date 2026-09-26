# SPDX-License-Identifier: AGPL-3.0-or-later
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = "Stop"
$FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-25-15-37/ffmpeg-n8.1.3-win64-gpl-8.1.zip"
$ExpectedSha256 = "8efaa4e62db01a71580dc5a7ec0625dea7a4dfc5fac2d680f94804503d18a34c"
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("opencam-ffmpeg-" + [Guid]::NewGuid().ToString("N"))
$ArchivePath = Join-Path $TempRoot "ffmpeg.zip"
$ExtractRoot = Join-Path $TempRoot "extracted"

try {
    New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
    $null = Invoke-WebRequest -Uri $FfmpegUrl -OutFile $ArchivePath
    $ActualSha256 = (Get-FileHash -Algorithm SHA256 $ArchivePath).Hash
    if (-not $ActualSha256.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "FFmpeg archive checksum mismatch: expected $ExpectedSha256, got $ActualSha256"
    }

    Expand-Archive -Path $ArchivePath -DestinationPath $ExtractRoot -Force
    if (-not (Test-Path "$ExtractRoot\ffmpeg-n8.1.3-win64-gpl-8.1\bin\ffmpeg.exe")) {
        throw "Pinned FFmpeg archive layout is missing bin\ffmpeg.exe"
    }
    if (-not (Test-Path "$ExtractRoot\ffmpeg-n8.1.3-win64-gpl-8.1\bin\ffprobe.exe")) {
        throw "Pinned FFmpeg archive layout is missing bin\ffprobe.exe"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item "$ExtractRoot\ffmpeg-n8.1.3-win64-gpl-8.1\bin\ffmpeg.exe" $Destination -Force
    Copy-Item "$ExtractRoot\ffmpeg-n8.1.3-win64-gpl-8.1\bin\ffprobe.exe" $Destination -Force
    (Resolve-Path $Destination).Path
}
finally {
    if (Test-Path $TempRoot) {
        Remove-Item -Path $TempRoot -Recurse -Force
    }
}
