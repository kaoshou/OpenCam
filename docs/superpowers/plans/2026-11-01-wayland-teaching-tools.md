# GNOME Wayland Teaching Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in GNOME Wayland teaching pointer and desktop annotations without compromising NativeOnly recording.

**Architecture:** A GNOME 46 extension owns compositor actors, local pointer tracking and drawing input. A bounded same-user bridge coordinates one OpenCam recording lease. Existing X11, Windows and macOS implementations remain independent; product integration follows a real compositor/capture feasibility gate.

**Tech Stack:** GNOME 46 GJS/Clutter/St/Cairo, existing original pointer PNGs, C#/.NET 8, Linux GLib/GIO, Node test runner, pinned OBS/libobs and existing portal/PipeWire capture.

**Spec:** `docs/superpowers/specs/2026-11-01-wayland-teaching-tools-design.md` (user approved 2026-11-01).

## Global Constraints

- Initially support GNOME 46 on Ubuntu 24.04 Wayland only.
- No automatic installation, enabling, root input access, arbitrary input injection, or unrelated desktop shortcut/theme changes.
- No postproduction pointer substitution; no text or area eraser.
- F10 toggles drawing without deleting strokes, F11 toggles toolbar, Esc exits drawing and clears; existing user-configurable bindings take precedence.
- Idle/preflight never hides the cursor, intercepts input or opens a drawing layer.
- No merge, version bump, tag or Release; version remains 1.2.0 Beta.
- Do not patch Mutter/OBS, synthesize portal authorization, or switch to X11 to pass a Wayland test.
- Preserve Windows/macOS native builds and X11 behavior. Never add a recording fallback or modify original media.

## Review Focus

1. GNOME 46 repeatedly makes the native cursor visible: detect duplicate cursor in actual moving capture; do not enable an unreliable replacement.
2. Two identical monitors or mixed scale: never identify the selected source by dimensions alone; reject ambiguous mapping before input interception.
3. Malicious/malformed local messages or wrong owner: deny without altering the cursor/input or leaking credentials.
4. App/extension loss while mouse is held or desktop locks: release input, restore cursor and keep video/audio output growing.
5. Toolbar hide/show, shortcut conflict and bounded drawing memory: remain recoverable and responsive without overriding desktop bindings.

## File Responsibilities

- `linux/gnome-extension/metadata.json`, `extension.js`: GNOME version declaration and strict lifecycle entry point.
- `linux/gnome-extension/pointer.js`: actor, original artwork, hotspot, visibility ownership and cleanup.
- `linux/gnome-extension/annotations.js`, `toolbar.js`: bounded drawing/history and accessible draggable controls; no recording logic.
- `linux/gnome-extension/session.js`, `protocol.js`: session authorization, identity, lease, validated command routing; protocol module independently testable without GJS.
- `scripts/linux/package-gnome-extension.mjs`: deterministic optional extension packaging, artwork inventory and licenses.
- `scripts/linux/gnome-teaching.test.mjs`: pure protocol/state/geometry and package tests.
- `scripts/linux/test-gnome-teaching.sh`, `gnome-teaching-fixture.mjs`: disposable real GNOME/portal tests and sanitized evidence.
- `.github/workflows/linux-gnome-teaching.yml`: isolated opt-in feasibility/acceptance runner; does not change release workflows.
- `src/ScreenRecorder.Platform.Linux/Native/teaching-bridge.c`, `LinuxWaylandTeachingSession.cs`: Linux-only GIO transport and managed lease; no extra application process.
- `src/ScreenRecorder.Platform.Linux/LinuxWaylandPointerOverlay.cs`: existing optional-pointer interface adapter.
- `src/ScreenRecorder.UI/ViewModels/MainViewModel.LinuxTeaching.cs`: status, activation consent and annotation actions.
- Existing Linux composition, annotations dispatch and package scripts: only capability-specific integration after the gate passes.
- `tests/ScreenRecorder.Media.Tests/LinuxWaylandTeachingTests.cs`: managed transport/lifecycle and platform isolation contracts.
- `docs/testing/wayland-teaching-tools-20261101.md`: exact results, installation/removal, limitations and manual gates.

## Task 1: Disposable GNOME Feasibility Gate

**Consumes:** Existing `scripts/linux/test-gnome-portal.sh`, real authorized portal selection/restore, original pointer artwork resources.
**Produces:** Optional extension skeleton; evidence proving or rejecting capture, cursor replacement, cleanup and selected-monitor mapping. This task does not expose product capability.

- [ ] Write `gnome-teaching.test.mjs` assertions for GNOME version 46 only, inert initialization, no Shell eval interface, no auto-install in production scripts, exact checked-in pointer-artwork hashes and hotspot metadata. Run `node --test scripts/linux/gnome-teaching.test.mjs`; verify missing implementation fails.
- [ ] Implement package metadata and lifecycle-only entry point. Package original PNG assets without image regeneration; explicit `enable()`/`disable()` ownership, no operation at import time. Add `createPointer({style,size,hotspot})` returning `{showAt(x,y),destroy()}` in `pointer.js`; no external event forwarding for movement.
- [ ] Add isolated GNOME test runner/workflow. Only a fresh disposable CI user/session may install/enable the test extension. Use actual portal chooser, a generated test application surface, a moving pointer and a bounded known stroke. Keep all tokens private and report only safe codes/geometry/pixel counts.
- [ ] Test pointer replacement using GNOME 46 APIs without global monkey-patching of unrelated Shell methods. Verify actual captured moving frames have the original pointer at the expected hotspot and no extra native arrow, including pointer crossing application surfaces and scale changes.
- [ ] Establish authoritative monitor mapping. Investigate real GNOME/portal identity APIs first. If no direct mapping exists, test short-lived per-monitor visual nonce markers observed only through the already-authorized source preview; require a unique nonce match, tie it to the acknowledged source hash and compositor geometry generation, then remove markers before recording. Never infer identity solely from size or ordinal. Reject missing/ambiguous/stale results. Do not log screen pixels, raw identities or nonce secrets.
- [ ] Exercise `disable()`, client loss and pointer outside mapped source; inspect desktop input/cursor restoration and continuing OBS bytes. Run real video full decode and expected-pixel/hotspot checks.
- [ ] Record PASS/FAIL with exact GNOME/OBS versions. **If cursor replacement, actual capture or identity cannot be made reliable, stop before Task 2, preserve evidence, and request a scope decision.** Do not enable a partial halo/postproduction substitute.
- [ ] Run pure tests and `git diff --check`; commit only the scoped feasibility artifacts.

## Task 2: Authenticated Session and Fail-Safe Cleanup

**Consumes:** Task 1 validated compositor lifecycle and source mapping.
**Produces:** `TeachingSession.begin(owner,nonce,source,options)`, `.renew(owner,nonce)`, `.command(owner,nonce,command)`, `.end(owner,nonce)` with deterministic cleanup. Local protocol version 1.

- [ ] Add failing pure tests: wrong sender/nonce, duplicate owner, replay after end, unsupported version, unknown keys/commands, nonfinite/out-of-region coordinates, >64 KiB messages, >128 pending commands; none may activate actors/input. Secret comparisons must not be logged.
- [ ] Implement strict protocol parsing and unique session-bus owner pinning. Extension-side explicit consent binds the actual requesting peer credentials/identity to a fresh nonce; do not trust a peer-provided application label. Enable consent cannot grant arbitrary file/exec/eval/input injection access.
- [ ] Implement renewable lease: renew every 1 second, expire after 3 seconds without renewal. Monotonic clock, no blocking Shell operations. Owner disappearance, extension disable, lock, monitor generation change and exceptions all share idempotent cleanup.
- [ ] Add tests for held drawing during expiry, stale renew after owner change, double cleanup and rejected late commands. Cap drawing work/history and always restore cursor/input even if a secondary cleanup operation throws.
- [ ] Run pure and actual GNOME lifecycle tests; verify existing portal recording continues across expiry/disable. Commit.

## Task 3: Linux Client, Pointer Adapter and Capability Integration

**Consumes:** Task 2 protocol and Task 1 source mapping.
**Produces:** `LinuxWaylandTeachingSession` with `GetCapabilitiesAsync`, `BeginAsync`, `RenewAsync`, `SendCommandAsync`, `EndAsync`, `SessionLost`; implements `IAsyncDisposable`. `LinuxWaylandPointerOverlay` implements existing `ICursorHighlightService`/`IPresentationPointerOverlay`.

- [ ] Write managed tests for unavailable extension, unsupported GNOME, denied consent, cancellation, mismatched source/lease, recorder instance restart and ignored stale completion. Confirm no session for Windows/macOS/X11 and no source token serialization into logs/projects.
- [ ] Add Linux-only GIO bridge shared library with an owned event loop; expose bounded connect/call/cancel/close functions consumed via P/Invoke. Reuse installed GLib/GIO; no new NuGet dependency or subprocess. Keep native media-spawn library behavior unchanged.
- [ ] Implement managed session and pointer adapter. There must be one recorder-owned authoritative teaching session; UI uses existing authenticated recorder IPC for low-rate commands, not a competing cursor owner.
- [ ] Add Linux capability/status UI with explicit extension setup and enable instructions. Only advertise tools after compatible extension handshake and source validation. Missing extension never prevents ordinary recording. Avoid synchronous UI startup probes.
- [ ] Add narrowly scoped Linux composition/engine capability dispatch after gate success. Use existing optional-pointer startup fallback; on live failure warn and preserve capture. Test native cursor capture recovery explicitly rather than equating visible desktop cursor with recorded cursor recovery.
- [ ] Run focused .NET tests, existing Linux cursor/source tests, actual extension disconnect during capture and Windows/macOS unchanged-path tests. Commit.

## Task 4: Drawing, Toolbar and Configurable Shortcuts

**Consumes:** Recorder-owned session and existing annotation settings/command semantics.
**Produces:** `AnnotationModel.apply(command)` and GNOME drawing/toolbar actors, routed through existing settings and recording lifecycle.

- [ ] Write failing geometry/state tests for all six drawing tools including whole-stroke eraser drag, undo/redo, clear, finite clipped coordinates, F10 stroke retention and Esc clear/exit. Compare representative vectors against existing `AnnotationDocumentTests` expectations.
- [ ] Implement bounded model: at most 1,000 strokes and 100,000 total points, coalesce input/redraw per compositor frame, refuse excess work with a visible recoverable notice. Keep pointer rendering independent of active tool.
- [ ] Implement draggable accessible toolbar with tooltips, current color/width, undo/redo and hide/finish. Never draw through toolbar controls; hide/show cannot repeatedly shrink dimensions. Clamp to actual logical monitor bounds after topology changes.
- [ ] Register user-configured shortcuts transactionally only for active recording. Detect conflicts and restore only owned registrations. F10-off keeps strokes; Esc clears and releases input; pause/stop clears transient layer and requires fresh validated activation on resume.
- [ ] Test actual mouse drag into/out of crop/toolbar, repeated hide/show and tool changes, conflicting shortcut, owner loss midstroke, monitor change and lock. Record pointer/strokes in real portal media and verify cleanup.
- [ ] Run pure/managed/GNOME tests and X11 annotation regression; commit.

## Task 5: Optional Packaging, Licenses and Cross-Platform Regression

**Consumes:** Verified extension/client integration.
**Produces:** Development Ubuntu app + optional extension ZIP, matched commit/version, reproducible evidence and honest support matrix.

- [ ] Add package tests requiring version/commit manifest, exact original-artwork inventory, AGPL source/license inclusion, GNOME 46 metadata, and no automatic extension activation or writes to a real user's extension directory.
- [ ] Package optional extension separately alongside Ubuntu Beta. Document the explicit installation/activation/removal steps and that disabling it restores ordinary recording. Test clean installation/removal in disposable GNOME user without modifying Windows/macOS packaging.
- [ ] Run Linux Core/Media tests and full app/media/package/X11/GNOME workflows. Record >=5 minutes at 30 FPS with pointer/drawing/pause/resume and a 20 FPS comparison. Decode MKV/MP4, inspect monotonic timestamps, dynamic content, expected pointer/stroke pixels and output growth through optional-feature loss.
- [ ] Run Windows/macOS managed/build/package regressions using current product commit. Do not describe virtual display/audio tests or compilation as physical desktop/microphone acceptance.
- [ ] Request a fresh whole-branch review; fix important findings and re-run affected tests. Keep review and test evidence associated with immutable commits.
- [ ] Complete `docs/testing/wayland-teaching-tools-20261101.md` with verified results and physical mixed-DPI/hotplug/subjective A/V checks still pending. Deliver test package links, not a Release.

## Plan Review

The first task is a hard technical gate; later tasks depend on its actual
results. Public APIs remain unavailable until capability and lifecycle checks
pass. Native inline execution is recommended: these tasks share one Shell
session/recording owner, so parallel implementation would increase contract
and ownership risk. An independent whole-branch review follows implementation.
