#!/usr/bin/env bash
set -euo pipefail

app_path="${1:-}"

fail() {
  printf 'Bundle verification failed: %s\n' "$1" >&2
  exit 1
}

[[ -n "$app_path" ]] || fail "usage: $0 /path/to/OpenCam.app"
[[ -d "$app_path" ]] || fail "app bundle does not exist: $app_path"

contents="$app_path/Contents"
macos="$contents/MacOS"
resources="$contents/Resources"
plist="$contents/Info.plist"

[[ -f "$plist" ]] || fail "missing Info.plist"
plutil -lint "$plist" >/dev/null || fail "invalid Info.plist"

for executable in OpenCam ffmpeg ffprobe OpenCam.SystemAudio; do
  [[ -x "$macos/$executable" ]] || fail "missing executable: Contents/MacOS/$executable"
done

[[ -f "$resources/OpenCam.icns" ]] || fail "missing Resources/OpenCam.icns"

icon_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$plist" 2>/dev/null || true)"
[[ "$icon_name" == "OpenCam.icns" ]] || fail "CFBundleIconFile must be OpenCam.icns"

minimum_version="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$plist" 2>/dev/null || true)"
[[ "$minimum_version" == "13.0" ]] || fail "LSMinimumSystemVersion must be 13.0"

helper_description="$(file "$macos/OpenCam.SystemAudio")"
[[ "$helper_description" == *"Mach-O"* ]] || fail "system audio helper is not a Mach-O executable"
[[ "$helper_description" == *"arm64"* ]] || fail "system audio helper is not arm64"

codesign --verify --deep --strict "$app_path" >/dev/null 2>&1 || fail "code signature verification failed"

printf 'Verified macOS bundle: %s\n' "$app_path"
