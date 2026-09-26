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
script_dir="$(cd "$(dirname "$0")" && pwd)"
temp_root="$(mktemp -d)"
trap 'rm -rf "$temp_root"' EXIT
iconset="$temp_root/OpenCam.iconset"

xcrun swiftc \
  "$script_dir/build-macos-icon.swift" \
  -framework AppKit \
  -o "$temp_root/build-macos-icon"
"$temp_root/build-macos-icon" "$source_png" "$iconset"

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
  width="$(sips -g pixelWidth "$iconset/$name" | awk '/pixelWidth:/ { print $2 }')"
  height="$(sips -g pixelHeight "$iconset/$name" | awk '/pixelHeight:/ { print $2 }')"
  [[ "$width" == "$size" && "$height" == "$size" ]] || {
    printf 'invalid generated icon size for %s: %sx%s (expected %sx%s)\n' \
      "$name" "$width" "$height" "$size" "$size" >&2
    exit 1
  }
done

mkdir -p "$(dirname "$output_icns")"
iconutil -c icns "$iconset" -o "$output_icns"
