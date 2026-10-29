#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ $(uname -s) != Linux || $(uname -m) != x86_64 ]]; then
  echo 'This packaging spike requires Ubuntu Linux x64.' >&2; exit 2
fi
if [[ -z ${DISPLAY:-} ]]; then
  echo 'No X11/XWayland DISPLAY. This package does not implement native Wayland support.' >&2; exit 2
fi
echo 'OpenCam Ubuntu PACKAGING SPIKE: recording is NOT supported; read README-UBUNTU-TEST.txt.' >&2
task_data=${OPENCAM_UBUNTU_TEST_DATA:-${XDG_STATE_HOME:-$HOME/.local/state}/OpenCam-Ubuntu-Spike}
mkdir -p "$task_data/config" "$task_data/data"
cd "$root"
exec env XDG_CONFIG_HOME="$task_data/config" XDG_DATA_HOME="$task_data/data" "$root/OpenCam" "$@"
