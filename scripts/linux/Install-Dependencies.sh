#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
unset LD_PRELOAD LD_AUDIT LD_LIBRARY_PATH
mode=${1:---check}
if [[ $# -gt 1 || ( "$mode" != --check && "$mode" != --install ) ]]; then
  echo 'Usage: bash ./Install-Dependencies.sh [--check|--install]' >&2; exit 2
fi
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 || ! -r /etc/os-release ]]; then
  echo 'This setup helper is for Ubuntu 24.04 / 25.04 x64 only; no packages were changed.' >&2; exit 2
fi
# /etc/os-release is the system-owned distribution declaration, not package input.
source /etc/os-release
if [[ "${ID:-}" != ubuntu || ( "${VERSION_ID:-}" != 24.04 && "${VERSION_ID:-}" != 25.04 ) ]]; then
  echo 'Automatic setup targets Ubuntu 24.04 / 25.04 x64. No packages were changed.' >&2
  echo 'Do not replace GLIBC or create library ABI symlinks to bypass a version mismatch.' >&2; exit 2
fi
package_manifest="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)/runtime-packages.txt"
if [[ ! -r "$package_manifest" ]]; then
  echo 'Incomplete package: runtime-packages.txt is missing. No packages were changed.' >&2; exit 2
fi
packages=()
while IFS= read -r package || [[ -n "$package" ]]; do
  [[ "$package" =~ ^[a-z0-9][a-z0-9+.-]+(\ \|\ [a-z0-9][a-z0-9+.-]+)*$ ]] || { echo 'Invalid runtime dependency manifest.' >&2; exit 2; }
  packages+=("$package")
done < "$package_manifest"
[[ ${#packages[@]} -gt 0 ]] || { echo 'Empty runtime dependency manifest.' >&2; exit 2; }
missing=()
for package in "${packages[@]}"; do
  installed=false
  IFS='|' read -r -a alternatives <<< "$package"
  for alternative in "${alternatives[@]}"; do
    alternative=${alternative// /}
    if status=$(dpkg-query -W -f='${Status}' "$alternative" 2>/dev/null) && [[ "$status" == 'install ok installed' ]]; then
      installed=true; break
    fi
  done
  if [[ "$installed" == false ]]; then missing+=("$package"); fi
done
if [[ ${#missing[@]} == 0 ]]; then
  echo 'All declared desktop prerequisite packages are installed. Run ./Start-OpenCam.sh for executable checks.'
  exit 0
fi
printf 'Missing desktop packages: %s\n' "${missing[*]}"
if [[ "$mode" == --check ]]; then
  echo 'To install with the system package manager: bash ./Install-Dependencies.sh --install'
  exit 2
fi
echo 'APT will show the proposed changes and ask for confirmation. No repositories or audio services will be replaced.'
# Let APT resolve declared alternatives against this distribution's repositories.
# Never turn a missing ABI into a symlink or silently disable globalization.
dependency_expression=$(IFS=,; printf '%s' "${missing[*]}")
if [[ "$(id -u)" == 0 ]]; then
  apt-get update
  apt-get satisfy --no-install-recommends "$dependency_expression"
else
  sudo apt-get update
  sudo apt-get satisfy --no-install-recommends "$dependency_expression"
fi
bash "${BASH_SOURCE[0]}" --check
echo 'Installation command completed. Run ./Start-OpenCam.sh as your normal desktop user, not root.'
