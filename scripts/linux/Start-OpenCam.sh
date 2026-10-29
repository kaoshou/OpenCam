#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
unset LD_PRELOAD LD_AUDIT LD_LIBRARY_PATH OBS_PLUGINS_PATH OBS_PLUGINS_DATA_PATH
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'This test package requires Ubuntu 24.04 x64.' >&2; exit 2
fi
app_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
  echo 'A desktop session is required. Run this from your Ubuntu desktop, not a headless SSH session.' >&2; exit 2
fi
for tool in pactl ldd; do
  if ! command -v "$tool" >/dev/null; then
    echo 'Missing desktop prerequisites. See README-LINUX-TEST.txt (pulseaudio-utils and runtime libraries).' >&2; exit 2
  fi
done
for executable in "$app_directory/OpenCam" "$app_directory/ffmpeg" "$app_directory/obs-runtime/bin/OpenCam.ObsCapture" "$app_directory/OpenCam.PulsePlayback"; do
  if [[ ! -x "$executable" ]]; then echo "Incomplete package: $executable" >&2; exit 2; fi
  dependencies=$(ldd "$executable" 2>&1)
  if [[ "$dependencies" == *'not found'* ]]; then
    echo "$dependencies" >&2
    echo 'Missing OS libraries. See README-LINUX-TEST.txt. No packages were installed automatically.' >&2; exit 2
  fi
done
exec "$app_directory/OpenCam" "$@"
