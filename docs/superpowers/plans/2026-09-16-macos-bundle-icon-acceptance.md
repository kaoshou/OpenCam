# macOS Bundle, Icon, and Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a repeatable macOS 13 OpenCam app bundle containing the native system-audio helper, a valid `.icns` icon, FFmpeg tools, correct metadata, and an ad-hoc signature, then run local acceptance checks.

**Architecture:** Put macOS assembly logic in one checked-in shell script used by both local builds and GitHub Actions. Generate `OpenCam.icns` from the repository PNG, compile the Swift helper before bundle assembly, write deterministic `Info.plist` metadata, and verify the bundle before signing and distribution.

**Tech Stack:** zsh/bash, `dotnet publish`, `xcrun swiftc`, `sips`, `iconutil`, `plutil`, `codesign`, FFmpeg/ffprobe, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-09-16-macos-audio-stability-and-app-integration-design.md`

## Global Constraints

- Use `src/ScreenRecorder.UI/Assets/app_icon.png` as the macOS icon source.
- Set `CFBundleIconFile` to `OpenCam.icns`.
- Set `LSMinimumSystemVersion` to `13.0`.
- Compile `OpenCam.SystemAudio` for `arm64-apple-macos13.0`.
- Sign only after all executables, libraries, icon resources, and metadata are in the bundle.
- Preserve Windows `.ico`, Windows workflow, and installer behavior.
- Do not copy a build into `/Applications`; local acceptance uses `artifacts/macos/OpenCam.app`.

---

## File Map

- `scripts/package-macos.sh`: compiles helper, generates iconset/ICNS, assembles plist and app bundle, verifies, and signs.
- `scripts/verify-macos-bundle.sh`: read-only bundle assertions usable locally and in CI.
- `.github/workflows/build-and-release.yml`: calls the shared packaging script.
- `.gitignore`: ignores local `artifacts/` output.
- `src/ScreenRecorder.UI/Assets/app_icon.png`: unchanged artwork source.
- `artifacts/macos/OpenCam.app`: ignored local acceptance artifact.

### Task 1: Add a read-only bundle verifier

**Files:**
- Create: `scripts/verify-macos-bundle.sh`

**Interfaces:**
- Consumes: one `.app` path argument.
- Produces: exit 0 only when required files, metadata, architectures, permissions, and signature are valid.

- [ ] **Step 1: Write the verifier first**

Create an executable script with `set -euo pipefail`. Resolve its only argument to `app_path`, then assert:

```bash
test -x "$app_path/Contents/MacOS/OpenCam"
test -x "$app_path/Contents/MacOS/ffmpeg"
test -x "$app_path/Contents/MacOS/ffprobe"
test -x "$app_path/Contents/MacOS/OpenCam.SystemAudio"
test -f "$app_path/Contents/Resources/OpenCam.icns"
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app_path/Contents/Info.plist")" = "OpenCam.icns"
test "$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$app_path/Contents/Info.plist")" = "13.0"
plutil -lint "$app_path/Contents/Info.plist"
file "$app_path/Contents/MacOS/OpenCam.SystemAudio" | grep -q 'Mach-O 64-bit executable arm64'
codesign --verify --deep --strict "$app_path"
```

Print `Bundle verification passed: <path>` only after all assertions succeed.

- [ ] **Step 2: Run against the old local bundle and observe failure**

```bash
chmod +x scripts/verify-macos-bundle.sh
scripts/verify-macos-bundle.sh artifacts/macos/OpenCam.app
```

Expected: nonzero exit because the old bundle lacks `OpenCam.SystemAudio`, `OpenCam.icns`, or the icon plist key.

- [ ] **Step 3: Commit the failing verifier**

```bash
git add scripts/verify-macos-bundle.sh
git commit -m "test: verify macOS bundle metadata and resources"
```

### Task 2: Create one repeatable macOS packaging script

**Files:**
- Create: `scripts/package-macos.sh`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `publish_dir`, `app_path`, and optional `configuration` arguments.
- Produces: verified, ad-hoc-signed `OpenCam.app`.

- [ ] **Step 1: Implement strict input validation**

The script starts with `set -euo pipefail`, rejects non-macOS hosts, requires the publish directory, `OpenCam`, `ffmpeg`, and `ffprobe`, and creates the app only under the explicitly supplied `app_path`. It removes only that exact prevalidated `.app` path before assembly; it never uses `~`, `$HOME`, `/`, a workspace root, or an unresolved glob as a destructive target.

- [ ] **Step 2: Compile the native helper into the publish directory**

Use:

```bash
xcrun swiftc \
  src/ScreenRecorder.Platform.macOS/Native/OpenCamSystemAudio/main.swift \
  -O \
  -target arm64-apple-macos13.0 \
  -framework ScreenCaptureKit \
  -framework AVFoundation \
  -framework CoreMedia \
  -framework CoreGraphics \
  -o "$publish_dir/OpenCam.SystemAudio"
chmod 755 "$publish_dir/OpenCam.SystemAudio"
```

- [ ] **Step 3: Generate a standards-compliant ICNS**

Create a temporary iconset with `mktemp -d`, install a trap that removes only that directory, and generate:

```text
icon_16x16.png       16×16
icon_16x16@2x.png    32×32
icon_32x32.png       32×32
icon_32x32@2x.png    64×64
icon_128x128.png     128×128
icon_128x128@2x.png  256×256
icon_256x256.png     256×256
icon_256x256@2x.png  512×512
icon_512x512.png     512×512
icon_512x512@2x.png  1024×1024
```

Run `sips -z <height> <width> src/ScreenRecorder.UI/Assets/app_icon.png --out <target>` for each size, then:

```bash
iconutil -c icns "$iconset_dir" -o "$resources_dir/OpenCam.icns"
```

The existing 256×256 artwork is the source for this increment; larger slots are upscaled so macOS receives a structurally complete ICNS.

- [ ] **Step 4: Assemble the bundle and metadata**

Copy publish files to `Contents/MacOS`, copy `OpenCam.icns` to Resources, and write an XML plist containing at least:

```xml
<key>CFBundleExecutable</key><string>OpenCam</string>
<key>CFBundleIdentifier</key><string>com.kaoshou.opencam</string>
<key>CFBundleName</key><string>OpenCam</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>OpenCam.icns</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSMicrophoneUsageDescription</key><string>OpenCam needs microphone access to record audio.</string>
<key>NSScreenCaptureUsageDescription</key><string>OpenCam needs screen capture access to record your screen and system audio.</string>
```

Keep the existing bundle version values unless the release workflow supplies explicit version variables.

- [ ] **Step 5: Sign, verify, and ignore local artifacts**

Make OpenCam, FFmpeg, ffprobe, and the helper executable; run:

```bash
codesign --force --deep --sign - "$app_path"
scripts/verify-macos-bundle.sh "$app_path"
```

Add `artifacts/` to `.gitignore` if it is not already present.

- [ ] **Step 6: Build a fresh local bundle**

```bash
dotnet publish src/ScreenRecorder.UI/ScreenRecorder.UI.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -o artifacts/macos/publish
cp /opt/homebrew/bin/ffmpeg artifacts/macos/publish/ffmpeg
cp /opt/homebrew/bin/ffprobe artifacts/macos/publish/ffprobe
chmod +x scripts/package-macos.sh
scripts/package-macos.sh artifacts/macos/publish artifacts/macos/OpenCam.app Release
```

Expected: verifier prints success.

- [ ] **Step 7: Commit**

```bash
git add scripts/package-macos.sh .gitignore
git commit -m "build: package native audio and icon in macOS app"
```

### Task 3: Make GitHub Actions use the shared packaging contract

**Files:**
- Modify: `.github/workflows/build-and-release.yml`

**Interfaces:**
- Consumes: `scripts/package-macos.sh publish/osx-arm64 OpenCam.app Release`.
- Produces: the same verified bundle locally and in CI.

- [ ] **Step 1: Replace inline bundle assembly**

Keep checkout, .NET setup, publish, FFmpeg download, DMG creation, and release upload. Replace the inline `mkdir`, PNG copy, plist heredoc, chmod, and codesign body with:

```yaml
- name: Create and verify macOS App Bundle
  run: |
    chmod +x scripts/package-macos.sh scripts/verify-macos-bundle.sh
    scripts/package-macos.sh publish/osx-arm64 OpenCam.app Release
```

- [ ] **Step 2: Validate workflow syntax and retained Windows job**

```bash
rg -n 'build-windows|build-macos|package-macos|OpenCam.icns|LSMinimumSystemVersion' .github/workflows/build-and-release.yml scripts
```

Expected: both jobs remain; macOS invokes the shared script; no workflow still declares macOS 11 or copies `app_icon.png` as the bundle icon.

- [ ] **Step 3: Run local packaging again**

Run Task 2 Step 6 without reusing an older `.app` bundle.

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/build-and-release.yml
git commit -m "ci: use verified macOS bundle packaging"
```

### Task 4: Automated release-candidate verification

**Files:**
- No production changes expected.

**Interfaces:**
- Consumes: plans 1 and 2 plus Tasks 1–3 in this plan.
- Produces: a verified local release candidate.

- [ ] **Step 1: Run all tests and Release build**

```bash
dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release
dotnet build ScreenRecorder.sln -c Release
```

Expected: zero failures and zero build errors.

- [ ] **Step 2: Verify bundle metadata and signature**

```bash
scripts/verify-macos-bundle.sh artifacts/macos/OpenCam.app
/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' artifacts/macos/OpenCam.app/Contents/Info.plist
/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' artifacts/macos/OpenCam.app/Contents/Info.plist
```

Expected: `OpenCam.icns` and `13.0`.

- [ ] **Step 3: Launch the release candidate**

```bash
open artifacts/macos/OpenCam.app
```

Expected: OpenCam starts, Finder and Dock display the project icon, and macOS does not report a damaged bundle.

- [ ] **Step 4: Verify selected-display and custom-region output**

Record at least ten seconds in each mode. For every final file run:

```bash
/opt/homebrew/bin/ffprobe -v error -show_entries stream=index,codec_type,codec_name,width,height,r_frame_rate,sample_rate,channels -show_entries format=duration -of json '<recording.mp4>'
```

Expected: valid H.264 video, nonzero duration, correct dimensions, and no FFmpeg encoder failure. During region adjustment the desktop remains visible through the selection interior.

- [ ] **Step 5: Verify Finder action**

Press **Open Folder** after a completed recording.

Expected: Finder opens and selects that MP4. Change the output directory to a path containing spaces and repeat.

### Task 5: Audio acceptance and user handoff

**Files:**
- No production changes expected unless an acceptance test reveals a reproducible defect; any such defect begins a new red-green cycle in the owning task.

**Interfaces:**
- Consumes: packaged ScreenCaptureKit helper and FFmpeg audio modes.
- Produces: user-testable `artifacts/macos/OpenCam.app` and evidence for all three audio modes.

- [ ] **Step 1: Record system audio**

Play a continuous known source and record at least fifteen seconds with system audio on and microphone off. Probe the output.

Expected: one AAC stream, 48,000 Hz, two channels, duration within one second of video duration.

- [ ] **Step 2: Record the built-in microphone**

Speak continuously for at least fifteen seconds with microphone on and system audio off. Probe the output and inspect timestamps:

```bash
/opt/homebrew/bin/ffprobe -v error -select_streams a:0 -show_entries packet=pts_time,duration_time -of csv=p=0 '<microphone-recording.mp4>'
```

Expected: monotonically increasing packet timestamps with no periodic multi-packet gaps; listening reveals no repeated blocks, clipping, or intermittent noise.

- [ ] **Step 3: Record mixed audio**

Play a continuous source while speaking for at least fifteen seconds with both checkboxes enabled.

Expected: one 48 kHz AAC stream containing both sources, no obvious drift, and neither source periodically disappears.

- [ ] **Step 4: Confirm Windows regression boundaries**

```bash
git diff master...HEAD -- src/ScreenRecorder.Platform.Windows src/ScreenRecorder.UI/Assets/app_icon.ico .github/workflows/build-and-release.yml
```

Expected: Windows FFmpeg provider and `.ico` are unchanged; WASAPI has only the interface signature adaptation; the Windows workflow steps remain intact.

- [ ] **Step 5: Present the running app to the user**

Keep `artifacts/macos/OpenCam.app` running and provide the absolute app path plus the five acceptance cases. Ask the user to exercise selected monitor, transparent region, Finder reveal, system audio, and built-in microphone/mixed audio.
