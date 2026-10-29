# Ubuntu application development test package

Version: **1.2.0**, development branch `codex/linux-app-test-20261030`.
This is not a Release or a full Linux-support announcement. The archive's
`LINUX-TEST-INFO.json` and `SOURCE.txt` identify the exact tested commit.

## Delivered build

- Tested commit: `2e9b213ca720d90b06df472f52bc1f429600bfc4`.
- [Successful Ubuntu workflow and logs](https://github.com/kaoshou/OpenCam/actions/runs/40948688441).
- [Application, corresponding sources and checksums](https://github.com/kaoshou/OpenCam/actions/runs/40948688441/artifacts/12930179478).
- Application archive: `OpenCam_v1.2.0_Ubuntu24.04_x64_Test_2e9b213.tar.gz` (about 63 MiB).
- Application SHA-256: `b6579475028eb7e9a7e1780c7048cee426b887ba6f886d3fb77e768c6bd5d376`.
- Sources SHA-256: `78d3d1e2006cb02dbc0b70b9cb722304a85e2801e9db2a127b1b2f9251de3a00`.

Both downloaded archives passed SHA-256 verification. The application archive
has 699 safe-path entries; required executables/notices were checked. Extracted
OpenCam, FFmpeg and OBS helper are Linux x86-64 ELF binaries. All 380 deployed
native-runtime file hashes match the manifest. No merge, tag or Release was made.

### Measured final acceptance

| Test | Observed result |
| --- | --- |
| Native display enumeration | 10 consecutive queries passed in isolated X11 |
| Normal UI | Real main window observed/alive, screenshot captured; not screenshot-mode initialization |
| Linux managed integration | 22 passed, zero failed/skipped on actual Ubuntu |
| Audio confirmation/playback | Real Pulse capture trial sealed; playback acknowledged drained samples |
| Recording meters | System and microphone each observed live and healthy silent states |
| 30 FPS endurance | First MKV: 300.8 s video, 9,024 frames; audio/video packet ends both 300.821 s |
| Pause/resume and MP4 | Two retained sources; MP4 303.9 s, 9,117 frames; packet ends agree within timestamp precision |
| 20 FPS comparison | Two sources; MP4 13.2 s, 264 frames |
| All tested MKV/MP4 media | Full decode, monotonic DTS, positive packet durations, duration/timeline checks and actual red/blue scene colors passed |
| Relocation | Real recording/export from a path containing spaces, without build-prefix library environment |
| Native dependency audit | Required dependencies resolved; only the explicitly optional CoreCLR LTTng tracing ABI marked unavailable |
| Wayland diagnostic | Attempt exited 1 (`portal_session_closed`); product recording remains disabled, not passed |

The final run used real libobs X11 and PulseAudio virtual devices, with software
rendering/CPU encoding. This is **not physical-display or perceptual A/V sync
acceptance**. Independent decoded-audio analysis of the preceding identical
recording-core build also found audible and quiet intervals in both exported
20/30 FPS MP4s; it is not a speech-quality or speaker-listening test.

## Scope

- Ubuntu 24.04, x86-64, **Ubuntu on Xorg / X11**, CPU H.264.
- Recording uses OBS/libobs 33.0.0 only. No FFmpeg video-recording fallback.
- Bundled FFmpeg/ffprobe handle audio confirmation, project media and export.
- .NET is self-contained. Desktop/driver libraries are Ubuntu prerequisites;
  the launcher checks dependencies and never installs software automatically.
- Existing Windows/macOS native implementations, builders and Release workflows
  remain unchanged. Shared composition has explicit Linux branches.

## Deliberate limitations

Wayland product recording is blocked, not silently redirected to XWayland.
The OBS portal source does not expose an independently verifiable selected
monitor identity. Equal dimensions do not establish that the user selected the
requested screen. Diagnostic Wayland experiments are separate from application
acceptance and cannot authorize a false `ready` identity.

Original/hidden cursor modes are available. Teaching-pointer overlays, screen
annotations and global shortcuts are not enabled in this Linux package. A
localized notice appears in the main window. Linux is not at feature parity.

Physical microphone/speaker quality, perceptual synchronization, GNOME portal
permissions, hotplug and multi-monitor/DPI operation need manual validation.
Virtual sources and successful decoding do not prove those properties.

## Verification gates

The Linux application workflow preserves individual logs and only packages after
the required X11 application gates succeed:

| Gate | Evidence / meaning |
| --- | --- |
| Pinned sources and platform isolation | Exact OBS/FFmpeg commits; unchanged Windows/macOS native/build trees |
| Runtime integrity | Complete file inventory and SHA-256; tampered helper is not executed even during enumeration |
| Product protocol | Invalid geometry and unverified Wayland identity fail before media |
| Native process ownership | glibc shim, inherited-FD closure and three-input mapping collision |
| Bound media | Actual Linux decoding of a pinned inode after path replacement; read-only input, cancellation/reaping and quota checks |
| X11 diagnostic matrix | No audio, system only, microphone only, both, including healthy silence |
| Production service graph | Audio confirmation/trial, sample-clock playback, recording, pause/resume, editor frame, project export |
| Endurance / comparison | 300-second first segment at 30 FPS, resumed segment; separate 20 FPS run |
| Audio metering | Actual callbacks; audible/silent transitions from an isolated Pulse fixture |
| Relocation | Paths containing spaces; no build-prefix `LD_LIBRARY_PATH` for recording/export; ELF dependency audit |
| MP4 | Checked full decode, all-packet DTS monotonicity, positive durations, expected length, timeline difference and changing frame hashes |
| Packaging | Licenses, build options, corresponding-source archives, exact commit and SHA256SUMS |

The Wayland fixture is attempted in a private compositor/bus/runtime directory.
Its exit status and logs are retained even if it cannot run; it is not a release
gate for this explicitly X11-only test package.

## Findings and fixes during development

- A diagnostic-only helper initially rejected the application's product CLI.
  Product OCOBS entrypoint and negative-input tests were added independently of
  the old diagnostic protocol.
- The initial private FFmpeg media profile omitted the `lavfi` input and then
  its `wrapped_avframe` decoder. Ubuntu logs identified fixture creation failures
  before any application recording; these were not claimed to be OBS failures.
- Display enumeration originally executed the helper before runtime validation.
  Enumeration now uses the same complete manifest/hash validation first.
- `find -exec ffmpeg` did not propagate decode failures. An explicitly checked
  verifier now prevents failed decoding from reaching packaging.
- Recording meters originally reported unavailable placeholder data. They now
  derive peaks and ages from real source callbacks, with independent C++ tests.
- PulseAudio's JSON field is `monitor_source`, distinct from the C structure's
  `monitor_of_sink`; literal upstream-format tests prevent monitor sources from
  being listed as microphones.
- The Linux wrapper forwarded underlying event senders, which the orchestrator's
  engine-identity guard ignored. A failing event-identity test reproduced this;
  relaying with the wrapper identity now updates failure state immediately.
- A callback can publish a newer sample timestamp after the polling thread reads
  its clock. The old future-timestamp check falsely marked such fresh samples as
  lost. Deterministic tests reproduced the race; fresh age now saturates at zero
  while the genuine two-second missing-callback check remains. Earlier logs
  reported `audio_health_unverifiable`, but cannot prove this was their only cause.
- Synchronous catalog calls during UI construction captured the UI context for
  async pipe draining and deadlocked. A paused-context test failed before the
  Linux-only `ConfigureAwait(false)` fix and passed afterward.
- An intermittent enumeration failure was retained as an explicit fail-closed
  error, not replaced with a guessed source. Sanitized exit/code diagnostics,
  repeated enumeration and an Xvfb `-noreset` fixture now cover it. Its earlier
  generic log does not establish a unique cause on physical desktops.
- CoreCLR's optional LTTng provider references `liblttng-ust.so.0`, an older ABI.
  The audit exempts only that exact optional dependency on that exact provider;
  all other missing libraries still fail. No ABI symlink or OS replacement is
  used. [CoreCLR explicitly permits this provider's load failure](https://github.com/dotnet/runtime/blob/v8.0.21/src/coreclr/pal/src/misc/tracepointprovider.cpp#L118).

Local pre-review regressions: Core 378 passed; Media 1496 passed, 25 skipped;
scripts 126 passed, 3 skipped. Skipped physical/platform tests are not passes.
Later Linux-focused test additions and the actual package workflow retain their
own results; consult the artifact logs rather than extrapolating these totals.
After the Linux UI/event fixes, the complete local Media/UI suite passed 1,500
tests with 26 platform/physical skips. Final scripts/CI tests passed 130 with
three skips. Core remained 378 passed. The final Ubuntu-focused suite separately
passed all 22 tests. Skips and physical/manual items are not counted as passed.

## Tester instructions

1. Select **Ubuntu on Xorg** on the Ubuntu login screen.
2. Extract all files to a writable local folder and run `./Start-OpenCam.sh`.
3. Follow `README-LINUX-TEST.txt` if desktop libraries are missing; no SDK build
   or FFmpeg compilation is required on the tester's machine.
4. Start with 20–30 seconds of moving content plus speech/system sound. Check
   original MKV, editor playback and exported MP4 before longer recording.
5. Keep source projects/MKVs. For a failure, run `./Collect-Diagnostics.sh`,
   review its archive and provide the time, session type, selected display,
   audio sources and reproduction steps. Recordings are not collected/uploaded.

## Remaining engineering work

Verified Wayland source provenance, desktop pointer/annotation/hotkey integration,
physical audio/video tests, device hotplug, more distributions and native Linux
installation formats are later milestones, not implied by this test archive.
The initial private media profile supports a bounded codec set, not every media
format that a distribution's unrestricted FFmpeg may support.
