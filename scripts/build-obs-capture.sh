#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
if [[ $# -ne 5 && $# -ne 7 ]]; then
  echo "Usage: bash build-obs-capture.sh OBS_SOURCE OBS_RUNTIME FFMPEG_HEADERS SIMDE_HEADERS NEW_BUILD_DIRECTORY [NEW_STAGE_DIRECTORY LICENSE_DIRECTORY]" >&2
  exit 2
fi
obs_source=$1
obs_runtime=$2
ffmpeg_headers=$3
simde_headers=$4
build_directory=$5
if [[ "$(git -C "$obs_source" rev-parse HEAD 2>/dev/null || true)" != '72e86655715d9a25d69ce446aa4dd44ba58e10d2' ]]; then
  echo 'Expected pinned OBS source revision 72e86655715d9a25d69ce446aa4dd44ba58e10d2 (33.0.0)' >&2; exit 1
fi
if [[ "$(git -C "$ffmpeg_headers" rev-parse HEAD 2>/dev/null || true)" != '1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7' ]]; then
  echo 'Expected pinned FFmpeg n8.1.3 headers (1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7)' >&2; exit 1
fi
if [[ "$(git -C "$simde_headers" rev-parse HEAD 2>/dev/null || true)" != '71fd833d9666141edcd1d3c109a80e228303d8d7' ]]; then
  echo 'Expected pinned SIMDe v0.8.2 headers' >&2; exit 1
fi
for source_directory in "$obs_source" "$ffmpeg_headers" "$simde_headers"; do
  if [[ -n "$(git -C "$source_directory" status --porcelain --untracked-files=no)" ]]; then
    echo 'Pinned build sources must not contain modified tracked files' >&2; exit 1
  fi
done
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then echo 'This wrapper builds macOS arm64; use build-obs-capture.ps1 on Windows x64' >&2; exit 1; fi
if [[ -e "$build_directory" ]]; then echo 'Build directory already exists; choose a new isolated directory' >&2; exit 1; fi
if [[ ! -d "$obs_runtime/Contents/Frameworks/libobs.framework" ]]; then echo 'Expected extracted official OBS.app runtime' >&2; exit 1; fi
if [[ $# -eq 7 && -e "$6" ]]; then echo 'Stage directory already exists' >&2; exit 1; fi
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cmake -S "$script_directory/../native/obs-capture" -B "$build_directory" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 \
  -DOBS_SOURCE="$obs_source" -DOBS_RUNTIME="$obs_runtime" \
  -DFFMPEG_HEADERS="$ffmpeg_headers" -DSIMDE_HEADERS="$simde_headers"
cmake --build "$build_directory" --config Release --parallel 2
if [[ $# -eq 7 ]]; then
  node "$script_directory/assemble-obs-runtime.mjs" "$6" osx-arm64 "$obs_runtime" "$build_directory/OpenCam.ObsCapture" "$7"
fi
