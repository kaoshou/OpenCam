# SPDX-License-Identifier: AGPL-3.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ObsSource,
    [Parameter(Mandatory = $true)][string]$ObsRuntime,
    [Parameter(Mandatory = $true)][string]$FFmpegHeaders,
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [string]$ImportLibraries,
    [string]$StageDirectory,
    [string]$LicenseDirectory
)
$ErrorActionPreference = 'Stop'
function Assert-Revision([string]$Directory, [string]$Expected, [string]$Label) {
    $revision = & git -C $Directory rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or $revision -ne $Expected) { throw "Expected pinned $Label source revision $Expected" }
    $modified = & git -C $Directory status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $modified) { throw "Pinned $Label source contains modified tracked files" }
}
Assert-Revision $ObsSource '72e86655715d9a25d69ce446aa4dd44ba58e10d2' 'OBS'
Assert-Revision $FFmpegHeaders '1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7' 'FFmpeg'
if ($env:OS -ne 'Windows_NT' -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'This wrapper requires Windows x64 and a Visual Studio developer PowerShell' }
if (Test-Path -LiteralPath $BuildDirectory) { throw 'Build directory already exists; choose a new isolated directory' }
if ($StageDirectory -and (Test-Path -LiteralPath $StageDirectory)) { throw 'Stage directory already exists' }
if ([bool]$StageDirectory -ne [bool]$LicenseDirectory) { throw 'StageDirectory and LicenseDirectory must be specified together' }
foreach ($tool in @('cmake', 'dumpbin', 'lib', 'cl')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing Visual Studio build tool: $tool" } }
if (-not (Test-Path -LiteralPath (Join-Path $ObsRuntime 'bin/64bit/obs.dll'))) { throw 'Expected extracted official OBS Windows x64 ZIP' }
New-Item -ItemType Directory -Path $BuildDirectory | Out-Null
if (-not $ImportLibraries) {
    $ImportLibraries = Join-Path $BuildDirectory 'import-libraries'
    New-Item -ItemType Directory -Path $ImportLibraries | Out-Null
    # Generate only the import libraries consumed by the helper, from the exact runtime exports.
    foreach ($library in @('obs', 'avformat', 'avcodec', 'avutil', 'w32-pthreads')) {
        $dllPattern = if ($library -in @('obs', 'w32-pthreads')) { "$library.dll" } else { "$library-*.dll" }
        $dlls = @(Get-ChildItem -LiteralPath (Join-Path $ObsRuntime 'bin/64bit') -Filter $dllPattern -File)
        if ($dlls.Count -ne 1) { throw "Expected exactly one runtime DLL matching $dllPattern" }
        $exports = & dumpbin /nologo /exports $dlls[0].FullName
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect exports of $($dlls[0].Name)" }
        $symbols = @($exports | ForEach-Object { if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+([A-Za-z_?][A-Za-z0-9_?@$]*)\s*(?:=.+)?$') { $Matches[1] } })
        if (-not $symbols.Count) { throw "No supported DLL exports in $($dlls[0].Name)" }
        $definition = Join-Path $ImportLibraries "$library.def"
        @("LIBRARY $($dlls[0].Name)", 'EXPORTS') + $symbols | Set-Content -LiteralPath $definition -Encoding ascii
        & lib /nologo /machine:x64 "/def:$definition" "/out:$(Join-Path $ImportLibraries "$library.lib")"
        if ($LASTEXITCODE -ne 0) { throw "Import-library generation failed for $library" }
    }
}
$nativeSource = Join-Path $PSScriptRoot '../native/obs-capture'
& cmake -S $nativeSource -B $BuildDirectory -A x64 "-DOBS_SOURCE=$ObsSource" "-DOBS_RUNTIME=$ObsRuntime" "-DFFMPEG_HEADERS=$FFmpegHeaders" "-DOBS_IMPORT_LIBS=$ImportLibraries"
if ($LASTEXITCODE -ne 0) { throw 'Native helper configuration failed' }
& cmake --build $BuildDirectory --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Native helper build failed' }
if ($StageDirectory) {
    & node (Join-Path $PSScriptRoot 'assemble-obs-runtime.mjs') $StageDirectory win-x64 $ObsRuntime (Join-Path $BuildDirectory 'Release/OpenCam.ObsCapture.exe') $LicenseDirectory (Join-Path $BuildDirectory 'Release/win-capture.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Runtime staging failed' }
}
