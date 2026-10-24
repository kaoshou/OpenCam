# SPDX-License-Identifier: AGPL-3.0-or-later
param(
    [Parameter(Mandatory=$true)][string]$HelperPath,
    [Parameter(Mandatory=$true)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $HelperPath -PathType Leaf)) { throw 'Compiled Windows native helper is missing.' }
if (!(Test-Path -LiteralPath $PublishDirectory -PathType Container)) { throw 'Managed publish directory is missing.' }
$source = (Resolve-Path -LiteralPath $HelperPath).Path
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$length = (Get-Item -LiteralPath $source).Length
if ($length -le 0) { throw 'Compiled Windows native helper is empty.' }
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$destination = Join-Path $publish 'OpenCam.ScreenCapture.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -cne $hash) {
    throw 'Packaged Windows native helper differs from the verified binary.'
}
[PSCustomObject]@{ Helper = 'OpenCam.ScreenCapture.exe'; Sha256 = $hash; Length = $length }
