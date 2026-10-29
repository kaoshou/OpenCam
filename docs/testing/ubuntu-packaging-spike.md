# Ubuntu packaging spike

Requested experiment, not Linux feature implementation or a Release. Baseline: OpenCam 1.2.0, e0c369e6495532cc0393c349dc428ffb8a00327e.

## Scope and approach

- Publish the unchanged application for Ubuntu 24.04 x64 with self-contained .NET and the existing NativeOnly build policy. Do not enable legacy recording fallback or change Windows/macOS code.
- Execute a Linux platform probe and normal application startup under an isolated Xvfb X11 session in GitHub Actions. Record process exit, visible-window evidence, screenshot and explicit unsupported-platform results.
- Package a clearly marked diagnostic tarball with license notices and exact source revision. Do not merge, change version, publish a Release or advertise Linux support.

This throwaway spike is directly requested by the user; a full Linux port is outside its scope. The clean separate checkout is reused on a dedicated branch, without creating another worktree.

## Already confirmed in source

- ObsRuntime.CurrentPlatform/TryLoad only accept win-x64 and osx-arm64.
- MainViewModel selects macOS providers for non-Windows, and wires the project recording workflow only on Windows/macOS.
- Native helper CMake selects macOS or Windows build/link inputs; it has no Linux target.
- Linux media preview/export backend selection must also be implemented/verified before claiming editing support.

## Acceptance interpretation

Publishing, native-library loading, an X11 window and successful rendering are separate from screen/audio capture. A passing safety probe means an unsupported platform fails closed, not that it records successfully. No real screen/audio recording or long-run sync may be claimed from Xvfb.

## Manual checks (REQUIRES MANUAL VALIDATION)

On Ubuntu 24.04 x64 with a normal desktop, extract the entire tarball, read README-UBUNTU-TEST.txt and run Start-OpenCam.sh from a terminal. Collect console output and test-data logs. Expect the current build not to support recording; do not use important material. Failure includes an unlabelled package, silently enabled legacy capture, or a misleading recording-success claim.

Wayland, real PipeWire/PulseAudio devices, hardware encoding, multi-monitor/DPI, annotations and long-duration A/V synchronization are not covered. A usable Linux recorder needs a separately designed Linux platform adapter and OBS deployment/capture/audio path.

Reference: https://docs.avaloniaui.net/docs/platform-specific-guides/linux

## Executed result — 2026-10-30 Asia/Taipei

Tested commit: `680469c57cce4a409bee8460168e5e8610128c93`. [Ubuntu run 40912458217](https://github.com/kaoshou/OpenCam/actions/runs/40912458217) completed successfully. An earlier run failed before publishing because the checkout lacked the baseline commit; fetching history fixed that test-infrastructure issue. It is not a recording success result.

Environment reported Ubuntu 24.04.5 LTS, x64, .NET 8.0.33 and an Xvfb X11 virtual desktop.

| Check | Actual result | Evidence |
| --- | --- | --- |
| Self-contained linux-x64 publish | PASS | publish.log, probe-build.log |
| Normal startup, not screenshot mode | Window observed, still alive after observation | normal-startup.json, normal-window-tree.txt, normal-startup.png |
| Process cleanup | Test requested termination after observation; exit 143 | normal-startup.json; not a spontaneous crash |
| OBS Linux recognition | NOT SUPPORTED: CurrentPlatform null | platform-probe.json |
| Missing platform safety | NativeOnly true, legacy recording type absent, recording factory refused | platform-probe.json |
| Display provider | DllNotFoundException for macOS CoreGraphics on Ubuntu | platform-probe.json |
| Real monitor enumeration | NOT VALIDATED; displayed monitor is a UI fallback, not a detected Linux monitor | source audit, normal-startup.png |
| Editing, preflight, media export | NOT VALIDATED | Linux project/media paths are not implemented/validated |
| Physical screen/system/microphone recording, sync | NOT SUPPORTED / REQUIRES MANUAL VALIDATION AFTER IMPLEMENTATION | No physical recording claim |

[Download diagnostic artifact](https://github.com/kaoshou/OpenCam/actions/runs/40912458217/artifacts/12914232385) (GitHub sign-in required; configured retention 14 days). It contains the tarball and evidence. The archive includes .NET runtime notices, project notices and SOURCE.txt for the exact tested commit. The published application's src/ and native/ trees are unchanged from the release baseline. No master merge, version change or Release publication was performed for this experiment.

Archive: `OpenCam_v1.2.0_Ubuntu24.04_x64_PackagingSpike_680469c.tar.gz`.
SHA-256: `b562642b7beb256024c91f8c0ad1740471952e9f96a67bd405d89cb0ce382cf1`.

Local evidence copy: `/Users/yhcheng/Documents/Codex/outputs/opencam-ubuntu-spike-20261030/`.

Conclusion: packaging and basic GUI startup are feasible, but packaging alone does not add Linux recording. Next work requires an approved Linux adapter/OBS-runtime design, X11/Wayland capture, Linux audio and preview/export ownership paths, followed by physical recording acceptance. This spike must not be advertised as Linux recorder support.
