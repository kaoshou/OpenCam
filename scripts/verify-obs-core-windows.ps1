# SPDX-License-Identifier: AGPL-3.0-or-later
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$FFmpegDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$reportPath = Join-Path $InputDirectory 'acquisition.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'Pinned acquisition report is missing' }
$inputs = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'obs-dependencies.lock.json') -Raw | ConvertFrom-Json
if ($inputs.schema -ne 1 -or $inputs.obsVersion -ne $lock.obsVersion -or $inputs.archive.sha256 -cne $lock.windowsRuntime.sha256 -or $inputs.enabled -ne $false) { throw 'Acquisition report does not identify the disabled pinned Windows runtime' }
if ($env:OS -ne 'Windows_NT' -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'Actual core validation requires Windows x64 and Visual Studio developer PowerShell' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Validation output directory already exists' }
foreach ($tool in @('cl','dumpbin','lib','cmake','node')) { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Missing developer tool: $tool" } }
foreach ($tool in @('ffmpeg.exe','ffprobe.exe')) { if (-not (Test-Path -LiteralPath (Join-Path $FFmpegDirectory $tool) -PathType Leaf)) { throw "Missing validation tool: $tool" } }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$prerequisites = foreach ($name in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')) {
    $file = Join-Path ([Environment]::SystemDirectory) $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Microsoft VC redistributable prerequisite missing from SystemDirectory: $name" }
    $item = Get-Item -LiteralPath $file
    @{ name = $name; fileVersion = $item.VersionInfo.FileVersion; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(); redistributed = $false }
}
$prerequisites | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'vc-runtime-prerequisites.json') -Encoding utf8
$build = Join-Path $OutputDirectory 'build'
$stage = Join-Path $OutputDirectory 'runtime'
& (Join-Path $PSScriptRoot 'build-obs-capture.ps1') -ObsSource $inputs.sources.obs -ObsRuntime $inputs.runtime -FFmpegHeaders $inputs.sources.ffmpeg -BuildDirectory $build -StageDirectory $stage -LicenseDirectory $inputs.notices *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'native-build.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $stage 'runtime.json'))) { throw 'Pinned native build or disabled runtime staging failed' }
$manifest = Get-Content -LiteralPath (Join-Path $stage 'runtime.json') -Raw | ConvertFrom-Json
if ($manifest.enabled -ne $false -or $manifest.protocol -ne 1 -or $manifest.platform -cne 'win-x64') { throw 'Staged runtime must remain disabled with the expected protocol and platform' }
Copy-Item -LiteralPath (Join-Path $stage 'runtime.json') -Destination (Join-Path $OutputDirectory 'runtime-manifest.json')
$identity = Join-Path $OutputDirectory 'obs-device-identity-tests.exe'
& cl /nologo /std:c++17 /EHsc /utf-8 /W4 (Join-Path $PSScriptRoot '../tests/native/obs-device-identity-tests.cpp') "/Fe:$identity" "/Fo:$(Join-Path $OutputDirectory 'obs-device-identity-tests.obj')" *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'device-identity-build.log')
if ($LASTEXITCODE -ne 0) { throw 'C++ audio endpoint identity test compilation failed' }
& $identity *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'device-identity.log')
if ($LASTEXITCODE -ne 0) { throw 'C++ audio endpoint identity tests failed' }
$node = (Get-Command node).Source
$env:OPENCAM_C_COMPILER = (Get-Command cl).Source
$env:OPENCAM_OBS_FFMPEG_SOURCE = $inputs.sources.ffmpeg
& $node --test --test-reporter=spec tests/scripts/obs-runtime.test.mjs tests/scripts/obs-validation.test.mjs tests/scripts/obs-display-module.test.mjs *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'packaging-tests.log')
if ($LASTEXITCODE -ne 0) { throw 'Packaging, provenance or display-only entry fixture tests failed' }
$env:OPENCAM_OBS_HELPER = Join-Path $stage 'OpenCam.ObsCapture.exe'
$env:OPENCAM_OBS_RUNTIME = $stage
$env:OPENCAM_TEST_FFMPEG = Join-Path $FFmpegDirectory 'ffmpeg.exe'
$env:OPENCAM_TEST_FFPROBE = Join-Path $FFmpegDirectory 'ffprobe.exe'
$env:OPENCAM_OBS_TEST_ARTIFACTS = Join-Path $OutputDirectory 'native'
New-Item -ItemType Directory -Path $env:OPENCAM_OBS_TEST_ARTIFACTS | Out-Null
$savedPath = $env:PATH
$started = Get-Date
try {
    # PE imports are resolved before main(): exactly the same loader environment as the managed adapter.
    $env:PATH = (Join-Path $stage 'bin/64bit') + [IO.Path]::PathSeparator + [Environment]::SystemDirectory
    & $node --test --test-reporter=spec tests/native/obs-capture.test.mjs *>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'native-synthetic-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Actual Windows libobs H264/AAC content/sync/kill-recovery acceptance failed' }
}
finally {
    $env:PATH = $savedPath
    # Compatibility with the initial test harness: collect only this run's explicitly owned synthetic directories.
    foreach ($directory in Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory | Where-Object { ($_.Name -like 'opencam-obs-test-*' -or $_.Name -like 'opencam-obs-kill-*') -and $_.LastWriteTime -ge $started }) {
        Copy-Item -LiteralPath $directory.FullName -Destination $env:OPENCAM_OBS_TEST_ARTIFACTS -Recurse
    }
}
if ($env:GITHUB_ENV) {
    foreach ($name in @('OPENCAM_OBS_HELPER','OPENCAM_OBS_RUNTIME','OPENCAM_TEST_FFMPEG','OPENCAM_TEST_FFPROBE')) {
        "$name=$([Environment]::GetEnvironmentVariable($name))" | Out-File -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Append
    }
}
@{ scope = 'Development synthetic core validation only'; physicalDesktop = 'NOT TESTED'; physicalAudio = 'NOT TESTED'; longDurationSync = 'NOT TESTED'; runtimeEnabled = $false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'acceptance-scope.json') -Encoding utf8
