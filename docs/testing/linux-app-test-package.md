# Ubuntu application development test package

Version: **1.2.0**, development branch `codex/linux-app-test-20261030`.
This is not a Release or a full Linux-support announcement. The archive's
`LINUX-TEST-INFO.json` and `SOURCE.txt` identify the exact tested commit.

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

Local pre-review regressions: Core 378 passed; Media 1496 passed, 25 skipped;
scripts 126 passed, 3 skipped. Skipped physical/platform tests are not passes.
Later Linux-focused test additions and the actual package workflow retain their
own results; consult the artifact logs rather than extrapolating these totals.

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
