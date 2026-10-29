OpenCam 1.2.0 — Ubuntu 24.04 x64 development test
NOT A RELEASE. Check LINUX-TEST-INFO.json for the exact commit.

Extract the entire archive to a local writable directory, then run:
  ./Start-OpenCam.sh
No .NET installation is required. Keep obs-runtime and all bundled files together.

OS desktop prerequisites (the launcher checks, never installs automatically):
  sudo apt install pulseaudio-utils libpulse0 libx264-164 libpipewire-0.3-0 libdrm2 libwayland-client0 libegl1 libgl1 libfontconfig1 libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 libxrender1 libice6 libsm6 libxcb-shm0 libxcb-randr0 libxcb-xfixes0 libxcb-xinerama0 libxcb-composite0
Use an X11 session: on Ubuntu's login screen select your account, then the gear
icon and "Ubuntu on Xorg" before logging in. Do not run OpenCam as root.

This is the first application integration test package, not full Linux parity.
Recording uses OBS/libobs only and CPU H.264. No FFmpeg video fallback is present.
FFmpeg/ffprobe are bundled for audio confirmation, editing, import and export.
Windows/macOS release packages and their native implementations are unchanged.

Current test limitations, also shown in the application:
- Original/hidden cursor only. Teaching pointer, annotations and global shortcuts
  are not enabled in this Linux test build; they remain later Linux work.
- Wayland recording is not enabled in this package: portal output identity is
  not yet independently verifiable. Diagnostic portal research is separate;
  matching dimensions alone cannot safely distinguish two monitors.
- GNOME permissions, physical devices, multi-monitor scaling and long-duration
  real audio/video synchronization still require manual validation.
- A virtual X11/PulseAudio test is not proof of physical microphone quality.

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
