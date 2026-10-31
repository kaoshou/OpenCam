#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
[[ $# == 2 && "${OPENCAM_DISPOSABLE_CI_DESKTOP:-}" == 1 && "$(uname -s)" == Linux ]] || exit 2
runtime=$1
evidence=$2
private=$(mktemp -d)
owned=()
cleanup() {
  for pid in "${owned[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${owned[@]}"; do wait "$pid" 2>/dev/null || true; done
  # Private metadata contains a one-use grant: never upload it as test evidence.
  rm -f "$private/events" "$private/preview" "$private/input"
  rmdir "$private" 2>/dev/null || true
}
trap cleanup EXIT
export XDG_RUNTIME_DIR="$private/runtime" XDG_CONFIG_HOME="$private/config"
mkdir -m 700 "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME"
mkdir "$XDG_CONFIG_HOME/xdg-desktop-portal"
printf '[preferred]\ndefault=gnome;gtk\norg.freedesktop.impl.portal.ScreenCast=gnome\n' > "$XDG_CONFIG_HOME/xdg-desktop-portal/portals.conf"
export XDG_SESSION_TYPE=wayland XDG_CURRENT_DESKTOP=GNOME GNOME_SHELL_SESSION_MODE=user LIBGL_ALWAYS_SOFTWARE=1
unset WAYLAND_DISPLAY
pipewire > "$evidence/pipewire.log" 2>&1 & owned+=("$!")
wireplumber > "$evidence/wireplumber.log" 2>&1 & owned+=("$!")
gnome-shell --headless --wayland --virtual-monitor 640x360 > "$evidence/gnome-shell.log" 2>&1 & owned+=("$!")
for ((i=0; i<150; i++)); do
  for socket in "$XDG_RUNTIME_DIR"/wayland-*; do [[ -S "$socket" ]] && export WAYLAND_DISPLAY="$(basename "$socket")"; done
  [[ -n "${WAYLAND_DISPLAY:-}" ]] && break
  sleep .1
done
[[ -n "${WAYLAND_DISPLAY:-}" ]] || exit 1
dbus-update-activation-environment WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_SESSION_TYPE XDG_RUNTIME_DIR XDG_CONFIG_HOME DISPLAY
export GTK_A11Y=atspi
GDK_BACKEND=wayland /usr/libexec/xdg-desktop-portal-gnome > "$evidence/portal-gnome.log" 2>&1 & owned+=("$!")
/usr/libexec/xdg-desktop-portal -r > "$evidence/portal.log" 2>&1 & owned+=("$!")
for ((i=0; i<100; i++)); do
  if gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop > "$evidence/portal-api.txt" 2>/dev/null && grep -Fq org.freedesktop.portal.ScreenCast "$evidence/portal-api.txt"; then break; fi
  sleep .1
done
mkfifo "$private/input"
# Keep stdin owned/open until the test completes. No fake portal responses.
exec 9<> "$private/input"
"$runtime/bin/OpenCam.ObsCapture" --runtime "$runtime" --prepare-wayland <&9 > "$private/preview" 2> "$private/events" &
capture=$!; owned+=("$capture")
for ((i=0; i<100; i++)); do
  if ! kill -0 "$capture" 2>/dev/null || grep -Fq '"event":"selected"' "$private/events"; then break; fi
  if ((i == 30)); then
    # AT-SPI activates actual chooser controls on the owned Wayland desktop;
    # no synthetic D-Bus permission replies or production input workaround.
    timeout 10 python3 scripts/linux/gnome-chooser-fixture.py > "$evidence/chooser-controls.txt" 2>&1 || true
  fi
  sleep .2
done
printf '\n' >&9
wait "$capture" || true
node scripts/linux/gnome-portal-recording-fixture.mjs "$runtime" "$private/events" "$private/preview" "$evidence" "$RUNNER_TEMP/opencam-linux/ffmpeg-prefix/bin"
