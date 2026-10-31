OpenCam 1.2.0 — Ubuntu 24.04 x64 development test
NOT A RELEASE. Check LINUX-TEST-INFO.json for the exact commit.

Recommended Ubuntu 24.04 amd64 installation (network required for dependencies):
  sudo apt install ./OpenCam_*_Ubuntu24.04_amd64_Beta.deb
Use the actual downloaded filename, or select this package in your desktop's
package installer if supported. After installation, open "OpenCam Beta" in the
application menu. No manual shell-script launch or .NET installation is needed.
The application runs as your ordinary desktop user, never through sudo.
Uninstall with: sudo apt remove opencam-beta
Uninstalling does not remove your recordings, projects or personal settings.
Application files and notices: /opt/opencam-beta/
The Beta targets Ubuntu 24.04 x64; this does not imply Ubuntu 20.04 support.

Alternative extracted test archive: extract the entire archive to a local
writable directory, then run:
  ./Start-OpenCam.sh
No .NET installation is required. Keep obs-runtime and all bundled files together.

OS desktop prerequisites (the launcher checks, never installs automatically):
  bash ./Install-Dependencies.sh --check
To install missing prerequisites in one operation (APT asks for confirmation):
  bash ./Install-Dependencies.sh --install
This helper is limited to Ubuntu 24.04 x64. It does not change repositories,
replace the audio server, install .NET, or manually replace GLIBC. Do not run
OpenCam itself as root. See Install-Dependencies.sh for the single package list.
For teaching pointers, annotations and global shortcuts, use an X11 session:
on Ubuntu's login screen select your account, then the gear icon and
"Ubuntu on Xorg" before logging in. Do not run OpenCam as root.

Wayland recording (Beta): choose "Select screen" in OpenCam and authorize one
screen in GNOME's system chooser. The authorized screen thumbnail is used for
source-local cropping; desktop coordinates from an earlier session are ignored.
Pause/resume restores the same source and verifies its identity before recording.
If permission expires, a source changes or the recorder restarts, select again.
Permission cancellation does not create a new recording or delete existing clips.

This is the first application integration test package, not full Linux parity.
Recording uses OBS/libobs only and CPU H.264. No FFmpeg video fallback is present.
FFmpeg/ffprobe are bundled for audio confirmation, editing, import and export.
Windows/macOS release packages and their native implementations are unchanged.

Current test limitations, also shown in the application:
- X11 supports the original/hidden cursor and the live teaching pointer, screen
  annotations and configurable global shortcuts. Annotations require a desktop
  compositor; failure closes the optional overlay, not the recording.
- Wayland supports recording with the original/hidden cursor, not the X11
  teaching pointer, desktop annotations or global shortcuts. Keyboard commands
  within the focused application remain available. No GNOME extension is installed.
- GNOME permissions, physical devices, multi-monitor scaling and long-duration
  real audio/video synchronization still require manual validation.
- A virtual X11/PulseAudio test is not proof of physical microphone quality.
- Virtual GNOME portal tests verify selection, source identity, restored segments
  and crop decoding; they do not replace physical screen/audio validation.

Please first record 20–30 seconds with moving content and speech/system sound.
Check the original MKV, editor playback and exported MP4 before longer work.
Then test pause/resume, 20/30 FPS, silent start and sound stopping/restarting.
Keep original project/MKV files. Do not delete them if a preview/export fails.

Trouble: run ./Collect-Diagnostics.sh, review the resulting archive, and provide
the archive plus the exact time, desktop/session (X11/Wayland), selected screen,
audio sources and steps. No recordings are collected or uploaded automatically.

License: AGPL-3.0-or-later for OpenCam; OBS/FFmpeg and other components retain
their own licenses. See LICENSE, NOTICE.md, THIRD-PARTY-NOTICES.md, linux-notices/
and the corresponding-source archive delivered alongside this test package.
Linux-specific OBS embedded-component notices are in linux-notices/obs-embedded/.
This Linux build uses FFmpeg n8.1.3 with GPL/libx264 enabled; the Windows/macOS
runtime inventories in the general notices do not describe this Linux artifact.
See SOURCE.txt and build-evidence/ffmpeg-config.mak for its exact inputs/options.
CoreCLR's optional LTTng tracing provider may lack the older liblttng-ust.so.0
ABI on Ubuntu 24.04. This affects optional PerfCollect/LTTng tracing, not the
recording, preview/export, logging or EventPipe paths tested here. Do not create
an ABI symlink to the newer library. Other missing native libraries remain errors.
