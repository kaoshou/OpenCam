#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# CI acceptance only: clean disposable Ubuntu 24.04 container, not a user installer.
set -euo pipefail
[[ "$(id -u)" == 0 && -d /delivery && -d /checks ]] || exit 2
shopt -s nullglob
archives=(/delivery/*.deb)
[[ ${#archives[@]} == 1 ]] || exit 2
apt-get update
apt-get install -y --no-install-recommends "${archives[0]}"
test "$(dpkg-query -W -f='${Status}' opencam-beta)" = 'install ok installed'
command -v pactl
command -v xdg-open
# Audit the installed tree BEFORE adding any desktop test/build dependencies.
while IFS= read -r -d '' binary; do
  magic=$(od -An -tx1 -N4 "$binary" | tr -d ' \n')
  [[ "$magic" == 7f454c46 ]] || continue
  ldd_status=0
  dependencies=$(ldd "$binary" 2>&1) || ldd_status=$?
  bash /checks/check-installed-elf.sh "${binary#/opt/opencam-beta/}" "$ldd_status" <<< "$dependencies"
done < <(find /opt/opencam-beta -type f -print0)
# Desktop test harness is separate from the application dependency contract.
apt-get install -y --no-install-recommends xvfb xauth dbus-x11 desktop-file-utils
desktop-file-validate /usr/share/applications/opencam-beta.desktop
useradd -m -s /bin/bash opencamtest
install -d -o opencamtest -g opencamtest /home/opencamtest/Recordings
printf 'preserve user recording\n' > /home/opencamtest/Recordings/sentinel.mkv
chown opencamtest:opencamtest /home/opencamtest/Recordings/sentinel.mkv
runuser -u opencamtest -- env HOME=/home/opencamtest XDG_SESSION_TYPE=x11 LIBGL_ALWAYS_SOFTWARE=1 \
  dbus-run-session -- xvfb-run -a -s '-screen 0 1280x1000x24' bash -c '
    /usr/bin/opencam-beta > /tmp/opencam-menu.log 2>&1 &
    app_pid=$!
    trap "kill $app_pid 2>/dev/null || true" EXIT
    sleep 8
    kill -0 "$app_pid"
  '
cat /tmp/opencam-menu.log
apt-get remove -y opencam-beta
test ! -e /usr/share/applications/opencam-beta.desktop
test "$(cat /home/opencamtest/Recordings/sentinel.mkv)" = 'preserve user recording'
apt-get install -y --no-install-recommends "${archives[0]}"
test "$(cat /home/opencamtest/Recordings/sentinel.mkv)" = 'preserve user recording'
echo 'PASS clean deb install, native dependency audit, user process launch, removal/reinstall preservation'
