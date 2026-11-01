#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
set -euo pipefail
[[ $# == 2 && "${OPENCAM_DISPOSABLE_CI_DESKTOP:-}" == 1 && "$(uname -s)" == Linux ]] || exit 2
app=$1
evidence=$2
private=$(mktemp -d)
owned=()
cleanup() {
  for pid in "${owned[@]}"; do kill "$pid" 2>/dev/null || true; done
  for pid in "${owned[@]}"; do wait "$pid" 2>/dev/null || true; done
  # Only this isolated fresh directory is removed; it contains private portal grants.
  rm -f "$private/events" "$private/preview" "$private/input"
}
trap cleanup EXIT
export XDG_RUNTIME_DIR="$private/runtime" XDG_CONFIG_HOME="$private/config" XDG_DATA_HOME="$private/data"
mkdir -m 700 "$XDG_RUNTIME_DIR" "$XDG_CONFIG_HOME" "$XDG_DATA_HOME"
mkdir "$XDG_CONFIG_HOME/xdg-desktop-portal"
printf '[preferred]\ndefault=gnome;gtk\norg.freedesktop.impl.portal.ScreenCast=gnome\n' > "$XDG_CONFIG_HOME/xdg-desktop-portal/portals.conf"
uuid=teaching-tools@kaoshou.github.io
mkdir -p "$XDG_DATA_HOME/gnome-shell/extensions"
node scripts/linux/package-gnome-extension.mjs "$XDG_DATA_HOME/gnome-shell/extensions/$uuid" "$GITHUB_SHA"
# Substitute ONLY the isolated test extension entry point, never a product archive.
cp scripts/linux/gnome-teaching-extension-fixture.js "$XDG_DATA_HOME/gnome-shell/extensions/$uuid/extension.js"
export XDG_SESSION_TYPE=wayland XDG_CURRENT_DESKTOP=GNOME GNOME_SHELL_SESSION_MODE=user LIBGL_ALWAYS_SOFTWARE=1 GSETTINGS_BACKEND=keyfile
gsettings set org.gnome.shell enabled-extensions "['$uuid']"
gsettings set org.gnome.shell disable-user-extensions false
unset WAYLAND_DISPLAY
pipewire > "$evidence/pipewire.log" 2>&1 & owned+=("$!")
wireplumber > "$evidence/wireplumber.log" 2>&1 & owned+=("$!")
gnome-shell --headless --wayland --virtual-monitor 640x360 --virtual-monitor 640x360 > "$evidence/gnome-shell.log" 2>&1 & owned+=("$!")
gnome-shell --version > "$evidence/gnome-version.txt"
for ((i=0;i<200;i++));do
  for socket in "$XDG_RUNTIME_DIR"/wayland-*;do [[ -S "$socket" ]] && export WAYLAND_DISPLAY="$(basename "$socket")";done
  [[ -n "${WAYLAND_DISPLAY:-}" ]] && break
  sleep .1
done
[[ -n "${WAYLAND_DISPLAY:-}" ]]
dbus-update-activation-environment WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_SESSION_TYPE XDG_RUNTIME_DIR XDG_CONFIG_HOME XDG_DATA_HOME DISPLAY
for ((i=0;i<150;i++));do
  if timeout 2 gdbus introspect --session --dest org.gnome.Mutter.ScreenCast --object-path /org/gnome/Mutter/ScreenCast > /dev/null 2>&1;then break;fi
  sleep .1
done
for ((i=0;i<150;i++));do
  if timeout 2 gdbus introspect --session --dest org.gnome.Shell --object-path /io/github/kaoshou/OpenCam/TeachingFixture > /dev/null 2>&1;then break;fi
  sleep .1
done
export GTK_A11Y=atspi
GDK_BACKEND=wayland python3 scripts/linux/gnome-teaching-surface.py > "$evidence/surface.log" 2>&1 & owned+=("$!")
GDK_BACKEND=wayland /usr/libexec/xdg-desktop-portal-gnome > "$evidence/portal-gnome.log" 2>&1 & owned+=("$!")
/usr/libexec/xdg-desktop-portal -r > "$evidence/portal.log" 2>&1 & owned+=("$!")
for ((i=0;i<150;i++));do
  if gdbus introspect --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop 2>/dev/null | grep -q org.freedesktop.portal.ScreenCast;then break;fi
  sleep .1
done
node scripts/linux/gnome-teaching-fixture.mjs "$app" "$private" "$evidence"
