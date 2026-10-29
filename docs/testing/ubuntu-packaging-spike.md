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
