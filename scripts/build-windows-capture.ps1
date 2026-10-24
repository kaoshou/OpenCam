# SPDX-License-Identifier: AGPL-3.0-or-later
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$compiler = Get-Command cl.exe -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$source = Join-Path $repo 'src/ScreenRecorder.Platform.Windows/Native/OpenCamScreenCapture/main.cpp'
$test = Join-Path $repo 'tests/native/capture-clock-tests.cpp'
Push-Location $output
try {
    & $compiler.Source /nologo /std:c++20 /EHsc /W4 /WX /O2 /utf-8 /DWINVER=0x0A00 /D_WIN32_WINNT=0x0A00 $source /Fe:OpenCam.ScreenCapture.exe /link d3d11.lib dxgi.lib windowsapp.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Windows capture helper compilation failed.' }
    & $compiler.Source /nologo /std:c++17 /EHsc /W4 /WX /O2 /utf-8 $test /Fe:capture-clock-tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Native protocol tests compilation failed.' }
    & (Join-Path $output 'capture-clock-tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Native clock protocol tests failed.' }
    & (Join-Path $output 'OpenCam.ScreenCapture.exe') 2> invalid-arguments.log
    if ($LASTEXITCODE -ne 1) { throw 'Invalid helper arguments did not return the expected exit code.' }
    if ((Get-Content invalid-arguments.log -Raw).Trim() -cne 'ERROR Expected monitor crop and rate arguments') {
        throw 'Native helper invalid-argument diagnostics changed.'
    }
} finally { Pop-Location }
if ($PublishDirectory) {
    & (Join-Path $PSScriptRoot 'copy-windows-capture.ps1') -HelperPath (Join-Path $output 'OpenCam.ScreenCapture.exe') -PublishDirectory $PublishDirectory
}
exit 0
