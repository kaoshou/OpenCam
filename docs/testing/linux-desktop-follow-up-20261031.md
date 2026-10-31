# Ubuntu desktop follow-up — 2026-10-31

Scope: first fixes on `codex/linux-app-test-20261030`, version 1.2.0. No Release, merge, version/tag change or new downloadable application package in this batch. The previously delivered 2e9b213 package does not contain these fixes.

## Confirmed defects and changes

- Folder opener: the original shared helper only handled Windows/macOS and returned null on Linux. Added explicit Linux selection and `xdg-open` with one absolute directory argument, including opening a media file's containing folder rather than its player. Existing Windows/macOS argument behavior remains covered. Actual GNOME file-manager opening is still manual validation.
- Streaming preview: reproduced with the same packaged native capture runtime and actual production project/preview services on Ubuntu. The still frame test and one-block PCM EOF/drain test had missed this defect. Playback froze around one sample while the producer waited for its 4800-frame queue to drain. PulseAudio default prebuffer can exceed that queue, and the helper only published clocks after new PCM. Periodic latency-derived clock publication alone did not fix it; explicit 4800-frame target, 1024-frame prebuffer/minreq and 9600-frame maximum plus idle clock polling passed. No fabricated wall clock; recording source/core untouched.
- Prerequisites: named missing commands/libraries, caught nonzero `ldd` rather than exiting silently, checked `xdg-open`, consolidated runtime packages in `Install-Dependencies.sh`. Read-only `--check`, explicit `--install` with APT confirmation. Ubuntu24 x64 only, no repository/audio-server replacement or manual GLIBC ABI workaround. Clean-image test caught and fixed the virtual `libpipewire-0.3-0` versus installed canonical `libpipewire-0.3-0t64` mismatch.

## Evidence

- Folder tests: 2 reproducible failures before Linux branch; 8/8 after.
- Media/UI regression on macOS: 1505 passed, 26 skipped, 0 failed (6m42s). Platform/physical skips are not passes.
- Core: 378 passed, 0 skipped/failed.
- Scripts, final normal-AppKit run: 128 passed, 3 skipped, 0 failed. Earlier sandbox-only AppKit SIGABRT disappeared with normal host access; no macOS code change.
- Baseline actual streaming preview failures: Actions 41202301098 and 41202526663.
- Idle-clock-only fix still failed: 41202779728, stronger native test stayed at clock=1 before EOF. Do not describe the first simple prebuffer hypothesis/test as conclusive.
- Full corrected native clock + production streaming preview/muted restart/capture pause-resume/export passed: 41202989941, repeated 41203191095 (that run's independent clean-package test failed on the package alias).
- Final all-green Ubuntu diagnostics: https://github.com/kaoshou/OpenCam/actions/runs/41203376474 at `558cd3a6cb4ba0958b68062627a8fe4dfc0b3bd7`. Jobs: clean Ubuntu24 container prerequisite check/install/recheck, real PulseAudio bounded producer, actual project streaming preview/muted restart/export with virtual X11/Pulse sources.
- No diff to protected Windows/macOS platform/native builders, release workflows or VERSION from e0c369e6495532cc0393c349dc428ffb8a00327e.

## Not completed or not verified

Wayland product recording remains fail-closed; Linux teaching pointer, annotation overlays and global shortcuts remain unimplemented/disabled. No physical GNOME/Windows/macOS recording acceptance in this batch, no subjective listening or physical A/V sync proof, no full new-package endurance or final whole-branch review. The Ubuntu fixtures use virtual display/audio sources, not user recordings.

Next desktop feature design must account for GNOME version and portal capabilities. GNOME 48 introduced the standard GlobalShortcuts portal backend; older GNOME cannot be assumed to provide it. Do not silently edit unrelated desktop bindings or install a Shell extension to simulate universal support.

References: [PulseAudio buffer semantics](https://www.freedesktop.org/software/pulseaudio/doxygen/structpa__buffer__attr.html), [GNOME48 portal enhancements](https://release.gnome.org/48/developers/), [ScreenCast portal](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html).
