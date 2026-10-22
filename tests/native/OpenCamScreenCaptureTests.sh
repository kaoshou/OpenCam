#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT
xcrun swiftc "$repo_root/src/ScreenRecorder.Platform.macOS/Native/OpenCamScreenCapture/main.swift" \
  -parse-as-library -O -target arm64-apple-macos13.0 -module-cache-path "$test_dir/cache" \
  -framework ScreenCaptureKit -framework VideoToolbox -framework CoreMedia -framework CoreGraphics \
  -o "$test_dir/capture"
if "$test_dir/capture" 2> "$test_dir/invalid.log"; then
  echo 'Invalid helper arguments unexpectedly succeeded' >&2
  exit 1
fi
grep -q '^ERROR Invalid arguments$' "$test_dir/invalid.log"
if [[ "${1:-}" == '--synthetic' ]]; then
  "$test_dir/capture" --synthetic-test > "$test_dir/video.mkv"
  ffprobe -v error -show_entries stream=codec_name,width,height -show_entries packet=pts_time -of json \
    "$test_dir/video.mkv" > "$test_dir/probe.json"
  node - "$test_dir/probe.json" <<'JS'
const fs = require('node:fs');
const assert = require('node:assert/strict');
const probe = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
assert.equal(probe.streams[0].codec_name, 'h264');
assert.equal(probe.streams[0].width, 320);
assert.equal(probe.streams[0].height, 180);
assert.equal(probe.packets.length, 60);
const pts = probe.packets.map(p => Number(p.pts_time));
assert.equal(pts[0], 0);
assert.ok(pts.every((p, i) => i === 0 || p > pts[i - 1]));
assert.ok(Math.abs(pts[30] - pts[29] - 0.533) < 0.002);
assert.ok(Math.abs(pts.at(-1) - 2.467) < 0.002);
JS
  ffmpeg -v error -xerror -i "$test_dir/video.mkv" -f null -
  ffmpeg -v error -xerror -i "$test_dir/video.mkv" -f lavfi -i sine=frequency=440:sample_rate=48000 \
    -map 0:v -map 1:a -c:v copy -c:a aac -shortest "$test_dir/muxed.mkv"
  ffprobe -v error -select_streams v -show_entries packet=pts_time -of json "$test_dir/muxed.mkv" > "$test_dir/muxed.json"
  node - "$test_dir/muxed.json" <<'JS'
const assert = require('node:assert/strict');
const pts = JSON.parse(require('node:fs').readFileSync(process.argv[2], 'utf8')).packets.map(p => Number(p.pts_time));
assert.equal(pts.length, 60);
assert.ok(Math.abs(pts[30] - pts[29] - 0.533) < 0.002);
assert.ok(Math.abs(pts.at(-1) - pts[0] - 2.467) < 0.002);
JS
  ffmpeg -v error -xerror -i "$test_dir/muxed.mkv" -f null -
  echo 'Hardware encoder / timestamp / complete decode tests passed (synthetic, no desktop/audio capture).'
fi
echo 'ScreenCaptureKit helper compile / argument tests passed.'
