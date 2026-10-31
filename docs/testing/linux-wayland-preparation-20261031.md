# Linux Wayland preparation increment — 2026-10-31

Version remains 1.2.0, Linux Beta. Development branch only; no release, merge or tag update.

## Implemented boundaries

- Pinned OBS portal responses are observed on the helper-owned session-bus connection. Request and session paths must belong to that connection and the pinned OBS request namespace.
- A usable grant requires portal version 4+, exactly one monitor, a bounded opaque stream identity and a renewable restore token. Dimensions and transient PipeWire node IDs are not stable identity.
- Cancellation/denial/session loss are latched. Unrelated session closure does not mark our capture failed. No private payload is emitted in an error message.
- Native `--runtime ROOT --prepare-wayland` creates a monitor source without creating an encoder, recording output or MKV. It waits for the granted stream and a startup-only RGBA render; no texture/transparent output is rejected, but a genuine opaque black screen is accepted.
- Preparation returns SHA256 source identity, actual source pixel geometry, private restore credential and a bounded 480×270 maximum thumbnail. These are not desktop-global logical coordinates.
- Managed preparation validates the runtime inventory, bounds both pipes, checks actual preview scaling, sanitizes errors and cancels/reaps its own helper. Its private result deliberately has no DTO/record serialization or credential-bearing `ToString`.

## Verification so far

- Native policy test was RED before the header existed, then GREEN locally with `-Wall -Wextra -Werror`.
- Actual GLib/GVariant response parser and preview-policy tests passed on Ubuntu: [native policy CI](https://github.com/kaoshou/OpenCam/actions/runs/41215300662).
- Native preparation helper compiled on Ubuntu in [native/application regression run](https://github.com/kaoshou/OpenCam/actions/runs/41215300661). Both `linux-core` and `deb-install` PASS, including 300-second 30-FPS X11/Pulse recording plus pause/resume (304-second MP4), 20-FPS comparison (13.15-second MP4), packet/dynamic-content validation and clean `.deb` install/start/remove/reinstall checks. These are virtual desktop results, not physical listening/synchronization acceptance.
- The explicitly non-gating Sway/portal research fixture still reports `FAIL: portal_identity_unavailable`; it is not Wayland acceptance. Its readiness loop also revealed an undeclared `rg` command dependency. That loop now uses standard `grep -Fq`; this removes a test-harness dependency, **not** the portal identity limitation. The script passed syntax checking; this one-line fixture change has not been rerun on a desktop yet.
- Managed preview scaling test first failed (1 failure, 11 pass), then all local Linux managed tests passed: 33 pass, 3 platform-specific skip. Local host was macOS using the installed newer .NET runtime via process-local roll-forward; not an Ubuntu recording claim.
- Private-pipe, overflow and process-cancellation tests use real controlled Unix child processes. They are process-protocol tests, **not** substitutes for a real GNOME chooser/PipeWire stream.
- Current managed Ubuntu CI: [private protocol and process tests](https://github.com/kaoshou/OpenCam/actions/runs/41215953135), both jobs PASS: 12 managed tests (0 skip), three native test executables with actual GVariant parsing.
- Native grant and preview tests also passed local AddressSanitizer/UndefinedBehaviorSanitizer execution.
- Windows/macOS platform trees, their native capture core/build scripts, formal release workflows and VERSION retain an empty diff against the release baseline.

## Not implemented or accepted yet

Product Wayland recording remains disabled. These pieces must be completed together before enabling it:

1. Recorder-owned in-memory grant registry, capture lease and short-poll/cancel IPC; no token in UI IPC, settings, project files or logs.
2. Source-selection UI and source-local crop mapping using the authorized thumbnail.
3. Formal segment restoration, returned identity/geometry verification, actual first-frame proof and single-use credential rotation across pause/resume.
4. Real GNOME portal acceptance: select/cancel/retry, same-size multiple monitors, scaling, session loss and permission revocation; then recording/media/synchronization acceptance.
5. Capability-specific teaching pointer, annotations and configurable global shortcuts. Screen-capture permission alone does not grant global input/cursor/overlay capabilities.

The already delivered X11 `.deb` remains the usable Ubuntu Beta. A new Wayland-ready user package is not implied by these source changes or by a successful headless policy test.
