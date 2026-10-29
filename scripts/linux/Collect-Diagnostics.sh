#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
unset LD_PRELOAD LD_AUDIT LD_LIBRARY_PATH
app_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
diagnostics_directory=$(mktemp -d "${TMPDIR:-/tmp}/OpenCam-Linux-Diagnostics.XXXXXX")
printf '%s\n' 'Collects OpenCam logs and OS/package versions only. No recordings, audio, environment dump or automatic upload.'
cp -- "$app_directory/LINUX-TEST-INFO.json" "$diagnostics_directory/"
uname -a > "$diagnostics_directory/uname.txt"
if [[ -r /etc/os-release ]]; then cp /etc/os-release "$diagnostics_directory/"; fi
printf 'Session: %s\nDesktop: %s\n' "${XDG_SESSION_TYPE:-unknown}" "${XDG_CURRENT_DESKTOP:-unknown}" > "$diagnostics_directory/session.txt"
log_directory="${XDG_DATA_HOME:-$HOME/.local/share}/ScreenRecorder/Logs"
if [[ -d "$log_directory" ]]; then
  mkdir "$diagnostics_directory/logs"
  find "$log_directory" -maxdepth 1 -type f -name 'app-*.log' -mtime -3 -size -20M -exec cp -- {} "$diagnostics_directory/logs/" \;
fi
"$app_directory/ffmpeg" -version > "$diagnostics_directory/ffmpeg-version.txt" 2>&1
tar -czf "$diagnostics_directory.tar.gz" -C "$diagnostics_directory" .
printf 'Review the logs before sharing; logs may contain file paths/device names.\nDiagnostics: %s.tar.gz\n' "$diagnostics_directory"
