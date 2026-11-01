#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# CI acceptance only: clean disposable Ubuntu container, not a user installer.
set -euo pipefail
[[ "$(id -u)" == 0 && -d /delivery && -d /checks ]] || exit 2
shopt -s nullglob
archives=(/delivery/*.deb)
[[ ${#archives[@]} == 1 ]] || exit 2
cat /etc/os-release
apt-get update
apt-get install -y --no-install-recommends "${archives[0]}"
test "$(dpkg-query -W -f='${Status}' opencam-beta)" = 'install ok installed'
command -v pactl
command -v xdg-open
bash /opt/opencam-beta/Install-Dependencies.sh --check
dpkg-query -W 'libicu*' libc6 libstdc++6
# Audit the installed tree BEFORE adding any desktop test/build dependencies.
audit_failed=0
while IFS= read -r -d '' binary; do
  magic=$(od -An -tx1 -N4 "$binary" | tr -d ' \n')
  [[ "$magic" == 7f454c46 ]] || continue
  ldd_status=0
  dependencies=$(ldd "$binary" 2>&1) || ldd_status=$?
  if ! bash /checks/check-installed-elf.sh "${binary#/opt/opencam-beta/}" "$ldd_status" <<< "$dependencies"; then
    audit_failed=1
  fi
done < <(find /opt/opencam-beta -type f -print0)
[[ "$audit_failed" == 0 ]] || exit 1
# Desktop test harness is separate from the application dependency contract.
apt-get install -y --no-install-recommends xvfb xauth dbus-x11 desktop-file-utils x11-utils xdotool fonts-noto-cjk
desktop-file-validate /usr/share/applications/opencam-beta.desktop
grep -x 'Name=OpenCam' /usr/share/applications/opencam-beta.desktop
useradd -m -s /bin/bash opencamtest
install -d -o opencamtest -g opencamtest /home/opencamtest/Recordings
printf 'preserve user recording\n' > /home/opencamtest/Recordings/sentinel.mkv
chown opencamtest:opencamtest /home/opencamtest/Recordings/sentinel.mkv
runuser -u opencamtest -- env HOME=/home/opencamtest XDG_SESSION_TYPE=x11 LIBGL_ALWAYS_SOFTWARE=1 \
  dbus-run-session -- xvfb-run -a -s '-screen 0 1280x1000x24' bash -c '
    set -euo pipefail
    /usr/bin/opencam-beta > /tmp/opencam-menu.log 2>&1 &
    app_pid=$!
    trap "kill $app_pid 2>/dev/null || true; cat /tmp/opencam-menu.log" EXIT
    visible=false
    for attempt in $(seq 1 40); do
      if xdotool search --onlyvisible --name OpenCam >/dev/null; then visible=true; break; fi
      kill -0 "$app_pid"
      sleep .5
    done
    [[ "$visible" == true ]]
    kill -0 "$app_pid"
    xwininfo -root -tree
  '
cat /tmp/opencam-menu.log
apt-get remove -y opencam-beta
test ! -e /usr/share/applications/opencam-beta.desktop
test "$(cat /home/opencamtest/Recordings/sentinel.mkv)" = 'preserve user recording'
apt-get install -y --no-install-recommends "${archives[0]}"
test "$(cat /home/opencamtest/Recordings/sentinel.mkv)" = 'preserve user recording'
# Exercise the archive setup path's real APT alternative handling, not only deb installation.
# Removing a required package also removes the deb; use an extracted helper copy.
setup_root=$(mktemp -d)
cp /opt/opencam-beta/Install-Dependencies.sh /opt/opencam-beta/runtime-packages.txt "$setup_root/"
source /etc/os-release
case "$VERSION_ID" in
  24.04) icu_package=libicu74 ;;
  25.04) icu_package=libicu76 ;;
  *) exit 2 ;;
esac
apt-get remove -y pulseaudio-utils "$icu_package"
if bash "$setup_root/Install-Dependencies.sh" --check; then
  echo 'Setup failed to detect the removed prerequisite.' >&2; exit 1
fi
printf 'Y\n' | bash "$setup_root/Install-Dependencies.sh" --install
bash "$setup_root/Install-Dependencies.sh" --check
test "$(dpkg-query -W -f='${Status}' "$icu_package")" = 'install ok installed'
echo 'PASS clean deb install, native dependency audit, visible app window, removal/reinstall preservation, archive prerequisite repair'
