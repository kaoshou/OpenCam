#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Usage: bash scripts/build-macos-icon.sh app_icon.png OpenCam.icns
set -euo pipefail

[[ $# -eq 2 && -f "$1" ]] || {
  printf 'usage: %s app_icon.png OpenCam.icns\n' "$0" >&2
  exit 1
}

source_png="$1"
output_icns="$2"
temp_root="$(mktemp -d)"
trap 'rm -rf "$temp_root"' EXIT
iconset="$temp_root/OpenCam.iconset"
mkdir "$iconset"

for entry in \
  icon_16x16.png:16 \
  icon_16x16@2x.png:32 \
  icon_32x32.png:32 \
  icon_32x32@2x.png:64 \
  icon_128x128.png:128 \
  icon_128x128@2x.png:256 \
  icon_256x256.png:256 \
  icon_256x256@2x.png:512 \
  icon_512x512.png:512 \
  icon_512x512@2x.png:1024; do
  name="${entry%%:*}"
  size="${entry##*:}"
  sips -z "$size" "$size" "$source_png" --out "$iconset/$name" >/dev/null
done

iconutil -c icns "$iconset" -o "$output_icns"
