#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
unset LD_PRELOAD LD_AUDIT LD_LIBRARY_PATH
mode=${1:---check}
if [[ $# -gt 1 || ( "$mode" != --check && "$mode" != --install ) ]]; then
  echo 'Usage: bash ./Install-Dependencies.sh [--check|--install]' >&2; exit 2
fi
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 || ! -r /etc/os-release ]]; then
  echo 'This setup helper is for Ubuntu 24.04 x64 only; no packages were changed.' >&2; exit 2
fi
# /etc/os-release is the system-owned distribution declaration, not package input.
source /etc/os-release
if [[ "${ID:-}" != ubuntu || "${VERSION_ID:-}" != 24.04 ]]; then
  echo 'Automatic setup is validated only for Ubuntu 24.04 x64. No packages were changed.' >&2
  echo 'Do not replace GLIBC or create library ABI symlinks to bypass a version mismatch.' >&2; exit 2
fi
packages=(pulseaudio-utils xdg-utils libstdc++6 libgcc-s1 zlib1g libicu74 libssl3t64
  libgssapi-krb5-2 libpulse0 libx264-164 libpipewire-0.3-0
  libdrm2 libwayland-client0 libegl1 libgl1 libfontconfig1 libx11-6 libx11-xcb1
  libxcb-xinput0 libxkbcommon0 libjansson4 libxext6 libxrandr2 libxi6 libxcursor1
  libxrender1 libice6 libsm6 libxcb-shm0 libxcb-randr0 libxcb-xfixes0
  libxcb-xinerama0 libxcb-composite0)
missing=()
for package in "${packages[@]}"; do
  if ! status=$(dpkg-query -W -f='${Status}' "$package" 2>/dev/null) || [[ "$status" != 'install ok installed' ]]; then
    missing+=("$package")
  fi
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
if [[ "$(id -u)" == 0 ]]; then
  apt-get update
  apt-get install "${missing[@]}"
else
  sudo apt-get update
  sudo apt-get install "${missing[@]}"
fi
echo 'Installation command completed. Run ./Start-OpenCam.sh as your normal desktop user, not root.'
