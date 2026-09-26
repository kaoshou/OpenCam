#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
publish_input="${1:-}"
app_input="${2:-}"
configuration="${3:-Release}"
version_text="$(cat "$repo_root/VERSION"; printf x)"
version_text="${version_text%x}"
[[ "$version_text" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'\n'$ ]] || {
  printf 'macOS packaging failed: VERSION must contain numeric SemVer and exactly one LF\n' >&2
  exit 1
}
product_version="${version_text%$'\n'}"

fail() {
  printf 'macOS packaging failed: %s\n' "$1" >&2
  exit 1
}

[[ -n "$publish_input" && -n "$app_input" ]] || \
  fail "usage: $0 /path/to/publish /path/to/OpenCam.app [configuration]"
[[ -d "$publish_input" ]] || fail "publish directory does not exist: $publish_input"
if [[ -n "$(git -C "$repo_root" status --porcelain --untracked-files=no)" ]] || \
   [[ -n "$(git -C "$repo_root" ls-files --others --exclude-standard -- src scripts installer .github)" ]]; then
  fail "commit source changes before packaging so SOURCE.txt identifies the exact revision"
fi

publish_dir="$(cd "$publish_input" && pwd)"
app_name="$(basename "$app_input")"
[[ "$app_name" == *.app && "$app_name" != ".app" ]] || fail "output must be a named .app bundle"
mkdir -p "$(dirname "$app_input")"
app_parent="$(cd "$(dirname "$app_input")" && pwd)"
app_path="$app_parent/$app_name"
[[ "$app_path" != "/" ]] || fail "refusing unsafe output path"

for executable in OpenCam ffmpeg ffprobe; do
  [[ -f "$publish_dir/$executable" ]] || fail "publish directory is missing $executable"
done

source_file="$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift"
microphone_source="$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamMicrophone/main.swift"
cursor_source="$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamCursorOverlay/main.swift"
icon_source="$repo_root/src/ScreenRecorder.UI/Assets/app_icon.png"
[[ -f "$source_file" ]] || fail "missing system audio helper source"
[[ -f "$microphone_source" ]] || fail "missing microphone helper source"
[[ -f "$cursor_source" ]] || fail "missing cursor overlay helper source"
[[ -f "$icon_source" ]] || fail "missing app icon source"
[[ -f "$repo_root/LICENSE" ]] || fail "missing project license"
[[ -f "$repo_root/NOTICE.md" ]] || fail "missing project notice"

temp_root="$(mktemp -d)"
trap 'rm -rf "$temp_root"' EXIT
"$repo_root/scripts/build-macos-icon.sh" "$icon_source" "$temp_root/OpenCam.icns"

rm -rf "$app_path"
mkdir -p "$app_path/Contents/MacOS" "$app_path/Contents/Resources"
cp -R "$publish_dir"/. "$app_path/Contents/MacOS/"
cp "$temp_root/OpenCam.icns" "$app_path/Contents/Resources/OpenCam.icns"
cp "$repo_root/LICENSE" "$app_path/Contents/Resources/LICENSE"
cp "$repo_root/NOTICE.md" "$app_path/Contents/Resources/NOTICE.md"

source_revision="${GITHUB_SHA:-}"
if [[ ! "$source_revision" =~ ^[0-9a-fA-F]{40}$ ]]; then
  source_revision="$(git -C "$repo_root" rev-parse HEAD)"
fi
printf 'OpenCam %s\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/%s\n' \
  "$product_version" "$source_revision" > "$app_path/Contents/Resources/SOURCE.txt"

helper="$app_path/Contents/MacOS/OpenCam.SystemAudio"
xcrun swiftc "$source_file" \
  -O \
  -target arm64-apple-macos13.0 \
  -framework ScreenCaptureKit \
  -framework AVFoundation \
  -framework CoreMedia \
  -framework CoreGraphics \
  -o "$helper"

microphone_helper="$app_path/Contents/MacOS/OpenCam.Microphone"
xcrun swiftc "$microphone_source" \
  -O \
  -target arm64-apple-macos13.0 \
  -framework AVFoundation \
  -o "$microphone_helper"

cursor_helper="$app_path/Contents/MacOS/OpenCam.CursorOverlay"
xcrun swiftc "$cursor_source" \
  -O \
  -target arm64-apple-macos13.0 \
  -framework AppKit \
  -o "$cursor_helper"

chmod +x \
  "$app_path/Contents/MacOS/OpenCam" \
  "$app_path/Contents/MacOS/ffmpeg" \
  "$app_path/Contents/MacOS/ffprobe" \
  "$helper" \
  "$microphone_helper" \
  "$cursor_helper"

plist="$app_path/Contents/Info.plist"
plutil -create xml1 "$plist"
plist_buddy=/usr/libexec/PlistBuddy
"$plist_buddy" -c 'Add :CFBundleExecutable string OpenCam' "$plist"
"$plist_buddy" -c 'Add :CFBundleIdentifier string com.kaoshou.opencam' "$plist"
"$plist_buddy" -c 'Add :CFBundleName string OpenCam' "$plist"
"$plist_buddy" -c 'Add :CFBundleDisplayName string OpenCam' "$plist"
"$plist_buddy" -c "Add :CFBundleVersion string $product_version" "$plist"
"$plist_buddy" -c "Add :CFBundleShortVersionString string $product_version" "$plist"
"$plist_buddy" -c 'Add :CFBundlePackageType string APPL' "$plist"
"$plist_buddy" -c 'Add :CFBundleIconFile string OpenCam.icns' "$plist"
"$plist_buddy" -c 'Add :CFBundleSupportedPlatforms array' "$plist"
"$plist_buddy" -c 'Add :CFBundleSupportedPlatforms:0 string MacOSX' "$plist"
"$plist_buddy" -c 'Add :NSMicrophoneUsageDescription string OpenCam needs microphone access to record audio.' "$plist"
"$plist_buddy" -c 'Add :NSScreenCaptureUsageDescription string OpenCam needs screen capture access to record your screen.' "$plist"
"$plist_buddy" -c 'Add :LSMinimumSystemVersion string 13.0' "$plist"
"$plist_buddy" -c 'Add :NSHighResolutionCapable bool true' "$plist"
"$plist_buddy" -c "Add :OpenCamBuildConfiguration string $configuration" "$plist"

codesign --force --deep --sign - "$app_path"
"$repo_root/scripts/verify-macos-bundle.sh" "$app_path"

printf 'Created macOS app bundle: %s\n' "$app_path"
