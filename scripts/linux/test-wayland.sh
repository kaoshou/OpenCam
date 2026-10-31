#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
[[ $# == 3 && "${OPENCAM_DISPOSABLE_CI_DESKTOP:-}" == 1 && "$(uname -s)" == Linux ]] || exit 2
helper=$1
runtime=$2
evidence=$3
mkdir "$evidence"
export XDG_RUNTIME_DIR="$evidence/runtime"
export XDG_CONFIG_HOME="$evidence/config"
mkdir -m 700 "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME"
mkdir "$XDG_CONFIG_HOME/xdg-desktop-portal"
cp tests/fixtures/linux-wayland/portals.conf "$XDG_CONFIG_HOME/xdg-desktop-portal/portals.conf"
export XDG_SESSION_TYPE=wayland XDG_CURRENT_DESKTOP=sway WLR_BACKENDS=headless WLR_HEADLESS_OUTPUTS=1 WLR_RENDERER=pixman WLR_LIBINPUT_NO_DEVICES=1 LIBGL_ALWAYS_SOFTWARE=1
unset DISPLAY WAYLAND_DISPLAY SWAYSOCK
owned_pids=()
cleanup() {
  for pid in "${owned_pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${owned_pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT
pipewire > "$evidence/pipewire.log" 2>&1 & owned_pids+=("$!")
wireplumber > "$evidence/wireplumber.log" 2>&1 & owned_pids+=("$!")
sway --unsupported-gpu --config "$PWD/tests/fixtures/linux-wayland/sway.conf" > "$evidence/sway.log" 2>&1 & owned_pids+=("$!")
for ((i=0; i<100; i++)); do
  for socket in "$XDG_RUNTIME_DIR"/wayland-*; do [[ -S "$socket" ]] && export WAYLAND_DISPLAY="$(basename "$socket")"; done
  for socket in "$XDG_RUNTIME_DIR"/sway-ipc.*.sock; do [[ -S "$socket" ]] && export SWAYSOCK="$socket"; done
  [[ -n "${WAYLAND_DISPLAY:-}" && -n "${SWAYSOCK:-}" ]] && break
  sleep .1
done
[[ -n "${WAYLAND_DISPLAY:-}" && -n "${SWAYSOCK:-}" ]]
swaymsg -t get_outputs > "$evidence/outputs.json"
node -e 'const fs=require("fs"); const o=JSON.parse(fs.readFileSync(process.argv[1])); if(o.length!==1||o[0].name!=="HEADLESS-1") process.exit(2)' "$evidence/outputs.json"
dbus-update-activation-environment WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_SESSION_TYPE XDG_RUNTIME_DIR XDG_CONFIG_HOME
/usr/libexec/xdg-desktop-portal-wlr -c "$PWD/tests/fixtures/linux-wayland/wlr.conf" > "$evidence/portal-wlr.log" 2>&1 & owned_pids+=("$!")
/usr/libexec/xdg-desktop-portal -r > "$evidence/portal.log" 2>&1 & owned_pids+=("$!")
for ((i=0; i<100; i++)); do
  if timeout 2 gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop > "$evidence/portal-api.txt" 2>/dev/null && grep -Fq org.freedesktop.portal.ScreenCast "$evidence/portal-api.txt"; then break; fi
  sleep .1
done
(while true; do swaymsg output HEADLESS-1 bg '#e04020' solid_color >/dev/null; sleep .2; swaymsg output HEADLESS-1 bg '#2040e0' solid_color >/dev/null; sleep .2; done) & owned_pids+=("$!")
"$helper" --runtime "$runtime" --mode enumerate --backend pipewire > "$evidence/enumeration.log" 2>&1
node scripts/linux/run-capture-probe.mjs "$helper" "$runtime" "$evidence/capture" 3 '{"protocol":1,"synthetic":false,"backend":"pipewire","width":640,"height":360,"fps":20,"x":0,"y":0,"monitorWidth":640,"monitorHeight":360,"cropX":0,"cropY":0,"monitor":"portal:HEADLESS-1","audio":"none","systemDevice":"","microphone":"","cursor":true,"encoder":"cpu","quality":23}' > "$evidence/driver.log" 2>&1
