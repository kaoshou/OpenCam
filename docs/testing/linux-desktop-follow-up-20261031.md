# Ubuntu desktop follow-up — 2026-10-31

## Current status — 2026-11-01 follow-up

The sections below describe historical batches, not the current feature boundary.
The development package at `f4bd2c6` passed Ubuntu virtual X11/PulseAudio recording,
300-second 30 FPS plus pause/resume, 5/10/20 FPS, streaming editor preview, MP4
decoding/timestamps and clean `.deb` installation/removal/reinstallation. Actual
virtual GNOME selection/restoration/cropping and X11 pointer/annotations/shortcuts
also passed. Evidence: Actions 41225694765, 41225694732 and 41225694728.

| Feature | Ubuntu X11 | Ubuntu Wayland | Remaining validation / implementation |
| --- | --- | --- | --- |
| Monitor / custom-region recording | Implemented; virtual source tested | Authorized source-local crop implemented; virtual GNOME tested | Physical multimonitor/mixed-DPI/hotplug |
| System / microphone audio, gains and preflight | Implemented; virtual Pulse sources tested | Same audio path | Physical endpoints and subjective sync |
| Pause/resume, MKV retention, MP4 | Virtual production-service acceptance passed | Same-source authorization verified across helper processes | Long physical endurance, suspend and forced-kill acceptance |
| Streaming editor preview | Audio and muted playback tested | Same Linux media backend | Physical audible quality |
| Video import, subtitle, GIF and web export | Real Linux bound-descriptor MP4/AAC import, one Chinese subtitle, GIF and web export passed | Same Linux media backend | Other imported codecs/containers and multi-subtitle combinations need broader Linux fixtures/manual validation |
| Teaching pointer and annotations | Implemented and recorded into actual virtual-source media | Not implemented in approved scope | X11 mixed-DPI/compositor hardware testing; Wayland requires separate desktop integration |
| Halo / click-ripple cursor effects | Not enabled; live teaching pointer is available | Not enabled | Separate overlay rendering/input work; not a declaration-only capability toggle |
| Configurable global shortcuts | Real XCB/XKB registration and recovery tested | Not implemented; focused controls remain | Desktop shortcut conflicts and physical keyboards |
| Hardware recording encoding | Not enabled; CPU only | Not enabled; CPU only | Native capability/probing work; not a small safe UI change |
| Install / app-menu launch / dependencies | Clean `.deb` install and reinstall passed | Same package; compatible portal backend required | Other distributions/older Ubuntu not certified |
| Folder opening | `xdg-open` implemented and argument-tested | Same | Actual desktop file-manager acceptance remains manual |
| Proactive display-configuration events | No dedicated Linux `IDisplayChangeMonitor` registered | Same | Needs session-specific integration and hotplug acceptance; do not simulate with guessed dimensions |
| Live video in recording preflight | Not implemented in NativeOnly builds | Same | Also unavailable in Windows/macOS NativeOnly; requires an OBS preview channel, not revival of FFmpeg recording |

This follow-up fixes two small confirmed inconsistencies first: encoder selection
must expose only the Linux runtime's CPU encoder without probing unrelated FFmpeg
hardware encoders, and the Debian description must describe X11 teaching tools
plus authorized Wayland recording. Windows/macOS native implementations remain
untouched; their existing build/package/managed regressions will be run without
publishing a Release. New evidence and failures must be recorded separately from
physical acceptance, never inferred from successful packaging.

### Final Linux package evidence — `10e239f`

[Application/package workflow 41228953165](https://github.com/kaoshou/OpenCam/actions/runs/41228953165)
succeeded for both linux-core and deb-install. Core 384 passed; Linux/media/factory
contracts 100 passed, including the original failing GIF test; no failures/skips.
The original-source/project-source hashes survived MP4 import, subtitle attachment,
GIF and web export, and complete media decoding. No GIF verifier was weakened.

Actual virtual X11/Pulse production services passed silent/live audio transitions,
audio confirmation, 300-second 30 FPS plus pause/resume, 5/10/20 FPS, muted/unmuted
streaming preview and MP4 packet/dynamic-content/full-decode validation. Actual
annotation capture recorded 46 frames with 51 red mark pixels at the expected
location; teaching pointer recording and export also passed. Clean Ubuntu `.deb`
install/native dependency audit/user-process launch/remove/reinstall preservation
passed. These are virtual services, not physical desktop/perceptual sync evidence.

Downloaded `.deb`, application archive and corresponding-source archive all match
SHA256SUMS. LINUX-TEST-INFO/SOURCE identify exact commit
`10e239f98d44cbabac3708e08c857ee8fcbb9787`, version 1.2.0, NativeOnly=true and
compatibilityFallback=false. The installable artifact is
`OpenCam_10e239f_Ubuntu24.04_amd64_Beta.deb`. No merge, tag, version bump or Release.
FFmpeg PNG/GIF components are within the existing pinned/source-delivered FFmpeg
license profile; no new external library/dependency was introduced by these fixes.

Same-final-commit desktop reruns also succeeded:
[GNOME portal 41230328345](https://github.com/kaoshou/OpenCam/actions/runs/41230328345)
and [X11 tools 41230330674](https://github.com/kaoshou/OpenCam/actions/runs/41230330674).
These exercise the real virtual GNOME chooser, source identity/restoration/crop,
and actual X11 input/pointer/annotation/settings bindings, respectively. They do
not remove the physical-device/manual boundaries in the inventory.

Artifacts (GitHub sign-in required):
- [Ubuntu Beta, `10e239f`](https://github.com/kaoshou/OpenCam/actions/runs/41228953165/artifacts/13053298777)
- [Windows test ZIP, `6481cc8`](https://github.com/kaoshou/OpenCam/actions/runs/41227699078/artifacts/13052817082)
- [macOS arm64 test ZIP, `6481cc8`](https://github.com/kaoshou/OpenCam/actions/runs/41227699078/artifacts/13052124403)

All downloaded package checksums were independently checked locally. Windows ZIP
SHA-256: `59616844a7b97c20c17a346873d0d25b6a93398559e6b0576dc94b19cb73a74d`.

### Windows/macOS regression evidence for this follow-up

- Product source `6481cc8`: [core/package workflow](https://github.com/kaoshou/OpenCam/actions/runs/41227699078).
  `windows-core` and `macos-package` both succeeded; this does **not** mean the
  overall workflow succeeded. Its original Ubuntu portable job failed the two
  legacy factory assumptions described below, and that failure is preserved.
- Windows full solution: Core 381 passed / 3 skipped; Media 1429 passed / 135
  skipped; no failures. Packaged-runtime/export/import subset: 64 passed.
  Actual native synthetic OBS media/synchronization/forced-kill tests passed.
  `acceptance-scope.json` explicitly says physical desktop/audio and long-duration
  sync were NOT TESTED. Do not relabel them as physical WGC/WASAPI acceptance.
- macOS CI: settings 19 passed; managed recording/display/export contracts 153
  passed / 1 skipped; actual packaged media-tool export subset 50 passed.
  The helper compiled, display identity checks ran, NativeOnly packaging and
  ad-hoc signature verification passed. No physical recording is implied.
- Final local Mac solution after test corrections: Core 384 passed; Media 1577
  passed / 27 skipped; no failures, process exit 0 (6m44s Media). The first run's
  Core telemetry-gate test failed once after the client received Stop but before
  server cleanup released the gate; its individual rerun passed. The corrected
  assertion tolerates only RecorderBusy until cleanup, then requires one real
  telemetry handler call. Twenty consecutive repetitions passed. No product IPC
  implementation was changed to hide that failure.
- Script/player/CI suite: 190 passed / 3 skipped / 0 failed. Website: 47 passed.
- Local GUI smoke used the newly built `6481cc8` app, not an installed older app:
  ZIP hash, bundle version 1.2.0, SOURCE revision and `codesign --verify --deep
  --strict` passed; the running executable's absolute path was checked. Starting
  recording was blocked by missing macOS screen-recording permission and returned
  to idle without creating a recording. Physical capture/sound/sync therefore
  remain BLOCKED pending consent. The startup UI also displayed a shortcut
  registration warning; actual conflicting keys/cause were not established.
  Original output directory and system/microphone choices were restored and the
  test app was closed. No old recordings were opened, changed or uploaded.
- Changes after `6481cc8` up to `10e239f` only touch Linux media packaging, Linux CI,
  tests and this report: Windows/macOS product source is identical across that
  range. Protected native/platform/build/release/VERSION diffs remain empty.

Static review of the first follow-up patch found no blocking selector/DI/UI
regression. One deferred coverage improvement remains: the new Linux feature
export test verifies complete decoding and immutable originals, but does not
independently measure output pixel variation/audio amplitude or subjective
quality. Real recording fixtures separately check dynamic content/timing; neither
is a physical listening or long-duration synchronization test.

The new actual Linux feature test at `6481cc8` then reproduced a real GIF export
failure: `Unknown input format: 'png_pipe'`. The package configuration had
`CONFIG_IMAGE_PNG_PIPE_DEMUXER 0`: configure's component name is
`image_png_pipe`, while the runtime input name is `png_pipe`. Correct the Linux
media build profile and rebuild rather than changing shared export commands or
adding a recording fallback. The new regression must pass against rebuilt tools.
The next run (`549dcd0`) passed palette decoding but failed full GIF frame
verification. Its build inventory had `CONFIG_GIF_PARSER 0`; pinned FFmpeg
`libavformat/gifdec.c:226` requests `AVSTREAM_PARSE_FULL_RAW`. Enable the GIF parser
too and rerun the unchanged production export verifier; never relax frame-count
or complete-decode checks to make the test pass.

The independent portable Ubuntu job also exposed two legacy-factory tests that
expected entry into the removed compatibility core. Linux intentionally refuses
that route before native capture when its runtime/display service is unavailable.
Keep the Windows/macOS legacy-boundary assertions and assert the Linux rejection
plus zero compatibility/native-provider calls instead of enabling a fallback.

### Manual acceptance still required

Use a disposable project and a 20–30-second moving screen with both speech and
system sound; check original MKV, editor playback and MP4. Repeat under X11 and
Wayland, selecting the second display and a crop where available. Expected:
correct source/crop, audible selected inputs and no visible accumulating delay.
Failure: wrong/black source, missing selected audio, stalled preview or drift.
Collect the exact build/desktop session, settings, timestamps, app log and media
probe report; retain original MKVs. Then test shortcut conflicts, mixed scaling,
device unplug/reconnect and suspend in separate disposable sessions. These tests
are not marked PASS by virtual CI.

## Historical first follow-up batch

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
