# Ubuntu readiness inventory — 2026-10-31

Audit baseline: `b1f2970`, branch `codex/linux-app-test-20261030`, OpenCam 1.2.0. This is an implementation/readiness inventory, not a new package acceptance report. The user's downloaded `2e9b213` package predates the folder/streaming-preview fixes.

## Approved delivery direction

- Provide an Ubuntu 24.04 amd64 `.deb` Beta with declared runtime dependencies, application-menu entry, icon and ordinary-user launch. Do not require users to manually execute a shell script after installation.
- APT resolves the declared dependencies; do not install packages silently from application startup, change repositories, replace GLIBC, or run OpenCam as root.
- Preserve current Windows/macOS native implementations and release packages. Do not change version 1.2.0 or publish a Release as part of Linux testing.
- About version suffixes remain fixed English: `(Windows x64 Build)`, `(macOS arm64 Build)`, `(Linux x64 Beta Build)`. Resolve package platform/architecture from build metadata. Keep the surrounding version label localized.

Packaging/version-display changes are now implemented on this development branch. Actual deb installation acceptance and delivery still depend on the new CI run; this document does not declare that acceptance passed.

## Feature inventory

| Area | Source status | Remaining acceptance |
| --- | --- | --- |
| OBS/libobs X11 recording | Implemented; NativeOnly, CPU H.264 | Physical GNOME desktop, actual microphone/system sound, long-duration sync and device changes |
| System/microphone audio selection and preflight | Implemented through Linux Pulse adapters and shared UI | Physical levels, mix audition, silent start and audio stop/restart; selected device consistency |
| Open output folder | Linux `xdg-open` branch fixed; regression tests pass | Actual desktop file-manager launch; integrate fix into new package |
| Editor streaming playback | Pulse buffering/clock deadlock fixed; production preview probe passes | Physical playback/listening, seeking/sync; integrate fix into new package |
| Editing/import/export | Shared project path wired; virtual integration exercises capture, pause/resume, preview and MP4 export | Ubuntu desktop workflow for import, split/trim/merge, subtitles, GIF and web-player export; not all proven by MP4 test |
| Dependencies | Consolidated manifest; deb builder and desktop entry implemented | Real `.deb` clean installation/uninstallation and desktop launch acceptance |
| Wayland capture | Product path explicitly blocked pending authoritative portal-selected source identity | Implement source permission/selection, cancellation/retry, actual pixel geometry, session loss and pause/resume; physical GNOME acceptance |
| Virtual teaching pointer / halo / click ripple | Not enabled; Linux exposes only original/hidden cursor | Platform-specific implementation and actual recording verification; no duplicate cursor, correct tip hotspot and scaling |
| Screen annotation | Explicitly disabled on Linux | Overlay/input routing, capture visibility, eraser/undo/clear, draggable toolbar, mixed-DPI hide/show and pointer interaction |
| Global recording/annotation shortcuts | Linux integration not implemented | Capability-check desktop/portal support; editable bindings/conflict handling; safe guided fallback where unavailable |
| Hardware encoding | Linux CPU-only; no hardware probes | Optional later acceleration work; CPU recording must remain usable without a discrete GPU |
| About and notices | OBS entry added to separate About window; Linux notices/source paths updated; entry-assembly package/Beta suffix implemented | Verify the actual published UI identity and installed package/source coverage |
| Supported systems | Ubuntu 24.04 x64 development target only | No Ubuntu 20.04, other distributions, arm64 or universal Wayland claim |

## Priority and gates

1. Deliverable packaging and accurate notices/version identity: clean Ubuntu install, menu launch, upgrade/uninstall without deleting projects, declared dependencies, source/notices/hash coverage. Installing on a development runner is insufficient.
2. Reliable ordinary recording: authorized Wayland source identity/geometry and fail-safe session behavior; X11 must retain its existing supported path. No FFmpeg/GDI recording fallback.
3. Teaching parity: implement pointer, annotation and shortcuts as capability-checked Linux adapters. Unavailable optional tools must not prevent ordinary recording.
4. Desktop acceptance: multi-monitor/fractional scaling, region persistence, silent/system/microphone combinations, permission cancellation/retry, device changes, repeated pause/resume, recovery and original MKV preservation.
5. New application artifact: repeat 20/30 FPS endurance, MKV/MP4 packet/decode checks and actual playback, then whole-branch review. Identify commit/version and unverified physical scenarios. Do not label virtual CI as physical A/V validation.

## Existing evidence

See [desktop follow-up](linux-desktop-follow-up-20261031.md) and [Linux desktop integration plan](../superpowers/plans/2026-10-31-linux-desktop-integration.md). Final first-batch diagnostics: https://github.com/kaoshou/OpenCam/actions/runs/41203376474 . That run validates fresh prerequisite installation, bounded Pulse playback and virtual X11/Pulse project preview/export; it does not deliver a new application package or prove physical GNOME/Wayland behavior.
