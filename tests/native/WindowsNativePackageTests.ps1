# SPDX-License-Identifier: AGPL-3.0-or-later
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$copy = Join-Path $repo 'scripts/copy-windows-capture.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('OpenCam-native-package-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'owned-helper.exe'
$publish = Join-Path $fixture 'publish'
[IO.Directory]::CreateDirectory($publish) | Out-Null
try {
    [IO.File]::WriteAllBytes($source, [byte[]](1, 2, 3, 4))
    $manifest = & $copy -HelperPath $source -PublishDirectory $publish
    $target = Join-Path $publish 'OpenCam.ScreenCapture.exe'
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($target)) -cne '01020304') { throw 'Packaged helper bytes changed.' }
    if ($manifest.Sha256.ToLowerInvariant() -cne '9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a') { throw 'Packaged helper digest is incorrect.' }
    if ($manifest.Length -ne 4) { throw 'Packaged helper size is incorrect.' }
    $rejected = $false
    try { & $copy -HelperPath (Join-Path $fixture 'missing.exe') -PublishDirectory $publish | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Missing native helper was silently accepted.' }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($target)) -cne '01020304') { throw 'Failed packaging changed existing helper.' }
    $rejected = $false
    try { & $copy -HelperPath $source -PublishDirectory (Join-Path $fixture 'missing-publish') | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Missing managed publish directory was silently accepted.' }
    Write-Output 'Windows native helper packaging: PASS'
} finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
