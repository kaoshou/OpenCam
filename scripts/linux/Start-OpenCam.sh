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
for tool in pactl ldd xdg-open; do
  if ! command -v "$tool" >/dev/null; then
    echo "Missing required command: $tool" >&2
    echo 'Install all declared prerequisites: bash ./Install-Dependencies.sh --install' >&2; exit 2
  fi
done
for executable in "$app_directory/OpenCam" "$app_directory/ffmpeg" "$app_directory/obs-runtime/bin/OpenCam.ObsCapture" "$app_directory/OpenCam.PulsePlayback"; do
  if [[ ! -x "$executable" ]]; then echo "Incomplete package: $executable" >&2; exit 2; fi
  dependency_error=0
  dependencies=$(ldd "$executable" 2>&1) || dependency_error=$?
  if [[ "$dependencies" == *'not found'* || "$dependency_error" != 0 ]]; then
    echo "Missing or incompatible libraries for: ${executable##*/}" >&2
    while IFS= read -r line; do
      [[ "$line" != *'not found'* ]] || printf '%s\n' "$line" >&2
    done <<< "$dependencies"
    if [[ "$dependencies" != *'not found'* ]]; then
      echo "Unable to inspect this executable (ldd exit=$dependency_error). Check that the full Ubuntu x64 package was extracted." >&2
    fi
    echo 'Install declared Ubuntu 24.04 prerequisites: bash ./Install-Dependencies.sh --install' >&2
    echo 'No packages were installed automatically. Do not manually replace GLIBC.' >&2; exit 2
  fi
done
exec "$app_directory/OpenCam" "$@"
