# SPDX-License-Identifier: AGPL-3.0-or-later
param(
    [Parameter(Mandatory = $true)]
    [string]$VersionFile
)

$ErrorActionPreference = "Stop"
$VersionText = [System.IO.File]::ReadAllText($VersionFile)
$StrictNumericSemVer = '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\n\z'
if (-not [System.Text.RegularExpressions.Regex]::IsMatch(
        $VersionText,
        $StrictNumericSemVer,
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
    throw "VERSION must contain numeric SemVer without leading zeros and exactly one final LF"
}

$VersionText.Substring(0, $VersionText.Length - 1)
