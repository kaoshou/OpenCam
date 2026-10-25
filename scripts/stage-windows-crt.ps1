# SPDX-License-Identifier: AGPL-3.0-or-later
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
$ErrorActionPreference='Stop'
if (-not $env:VCToolsRedistDir) { throw 'Run inside the selected Visual Studio developer environment.' }
$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs=(& $vswhere -latest -version '[17.0,18.0)' -products Microsoft.VisualStudio.Product.Enterprise -format json | ConvertFrom-Json)
if (-not $vs -or $vs.installationVersion -notlike '17.*') { throw 'Expected the licensed VS2022 Enterprise toolchain.' }
$folders=@(Get-ChildItem -LiteralPath (Join-Path $env:VCToolsRedistDir 'x64') -Directory -Filter 'Microsoft.VC*.CRT')
if ($folders.Count -ne 1) { throw 'Ambiguous x64 release CRT directory.' }
$crt=$folders[0].FullName
$files=@(Get-ChildItem -LiteralPath $crt -File -Filter '*.dll')
if ($files.Count -lt 3) { throw 'Incomplete release CRT.' }
$evidence=@()
foreach($file in $files){
  if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse-point CRT input.'}
  $signature=Get-AuthenticodeSignature -LiteralPath $file.FullName
  if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)'){
    throw "Invalid Microsoft signature: $($file.Name)"
  }
  $evidence+=@{name=$file.Name;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();
    signatureStatus='Valid';signerSubject=$signature.SignerCertificate.Subject;
    signerThumbprint=$signature.SignerCertificate.Thumbprint;fileVersion=$file.VersionInfo.FileVersion}
}
$licenseUrl='https://visualstudio.microsoft.com/wp-content/uploads/2021/11/Visual-Studio-2022-Enterprise-Professional-License-EN.docx'
$licenseHash='9c0cd52b20db9d081854c75bd1b50c75514b8f8cb09c8cad15e89d90b97b5bf3'
$metadata=Join-Path ([IO.Path]::GetTempPath()) ('opencam-crt-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $metadata | Out-Null
try{
  $license=Join-Path $metadata 'MSVC-2022-LICENSE.docx'
  Invoke-WebRequest -Uri $licenseUrl -OutFile $license
  if((Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash.ToLowerInvariant() -cne $licenseHash){throw 'VC license checksum mismatch.'}
  @{schema=1;visualStudioVersion=$vs.installationVersion;edition='Enterprise 2022';toolset=$env:VCToolsVersion;
    source='Visual Studio VC/redist/x64 release CRT';licenseUrl=$licenseUrl;licenseSha256=$licenseHash;files=$evidence} |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $metadata 'provenance.json') -Encoding utf8
  node (Join-Path $PSScriptRoot 'stage-windows-crt.mjs') $RuntimeDirectory $crt $metadata
  if($LASTEXITCODE -ne 0){throw 'CRT staging/PE closure verification failed.'}
}finally{Remove-Item -LiteralPath $metadata -Recurse -Force}
