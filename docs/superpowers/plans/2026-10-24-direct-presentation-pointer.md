# Direct Presentation Pointer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record one live presentation stick on both platforms without editable-pointer recording or changing the working video/audio capture path.

**Architecture:** Existing desktop overlays render the supplied original PNG. A readiness contract controls whether native cursor capture may be disabled. Overlay failures are optional-decoration warnings, never recorder failures. Legacy pointer projects retain their compatibility reader.

**Tech Stack:** .NET 8, existing Win32 overlay, AppKit Swift helper, existing FFmpeg/platform providers; no new dependencies.

**Spec:** docs/superpowers/specs/2026-10-24-direct-presentation-pointer-design.md

## Global Constraints

- Version remains 0.4.1; do not merge/push/publish a Release.
- Keep existing and newly recorded video/audio safe; preserve old projects.
- No global cursor replacement, private API, hooks, pointer-history recording, or new video transport.
- Desktop retains the ordinary cursor; recorded output contains one stick when the overlay is ready.
- Do not automatically record private desktops or claim physical-device PASS from synthetic tests.

## Review Focus

- Missing/corrupt PNG or absent helper must fall back to native cursor recording, not invisible-pointer success.
- Failure/timeout/cancellation during overlay startup must not strand a helper or block capture ownership release.
- Pause/resume and recorder EOF must not create duplicate or orphan overlays.
- Stale editable-pointer preferences must not reactivate the failing native pointer producer on either platform.
- Desktop coordinates, image alpha and tip geometry must stay correct across negative origins/scale; do not mistake simulated rendering for physical capture proof.

### Task 1: Original artwork and single-cursor capture policy

**Files:** CursorEffectTests.cs, NativeCaptureTests.cs, MacOsFFmpegProviderTests.cs; WindowsCursorHighlightService.cs; shared PointerRasterizer/PresentationPointerImage; WindowsFFmpegProvider/WindowsCapturePlanProvider/WindowsGraphicsCapturePlan; MacOsFFmpegProvider/MacOsNativeVideoCapture; OpenCamCursorOverlay/main.swift.

**Interfaces:** Providers consume `RecordingConfiguration.CursorEffect`; only Hidden and a ready PresentationStick omit native cursor capture. The PNG and hotspot are shared with legacy rendering.

- [x] Add failing renderer checks for dark tip, silver body, transparent background and premultiplied BGRA, and provider checks that Stick captures no second cursor.
- [x] Run targeted tests and confirm expected RED against the simplified renderer/current cursor flags.
- [x] Replace the live drawings with the original PNG, correct the capture flags, and compile the Swift helper without launching a desktop overlay.
- [x] Run the renderer/provider suite and inspect a synthetic PNG preview.

### Task 2: Optional overlay readiness and ownership

**Files:** CoreInterfaces.cs, WindowsCursorHighlightService.cs, MacOsCursorHighlightService.cs, OpenCamCursorOverlay/main.swift; platform lifecycle tests.

**Interfaces:** `IPresentationPointerOverlay.StartPresentationAsync(CancellationToken): Task<bool>` and `OverlayFailed` event; implementations also remain `ICursorHighlightService`.

- [x] Add failing tests for ready handshake, missing asset/helper, cancellation, early EOF and stop/restart ownership.
- [x] Add bounded readiness, safe worker exception handling, parent-EOF cleanup and a runtime failure notification without logging pointer coordinates.
- [x] Verify synthetic helper processes are reaped and compile native Swift; do not run a real desktop overlay automatically.

### Task 3: Direct-recording integration and truthful UI

**Files:** RecordingOrchestrator.cs, RecorderTelemetry.cs, MainViewModel.cs, SettingsViewModel.cs, SettingsWindow.axaml, LocalizationService.cs/JaJp.cs, project-recording lifecycle tests.

**Interfaces:** Orchestrator consumes Task 2's readiness, stores an optional cursor-effect health flag, and selects Default before capture if the overlay is unavailable.

- [x] Add failing integration tests: stale editable settings create no sidecar; ready overlay precedes capture; startup failure uses ordinary cursor and preserves audio selection; runtime failure does not stop/restart recording; pause/resume/failed startup clean up.
- [x] Disable editable-pointer recording for new sessions on both platforms, remove its setting from the UI, retain old project readers, and integrate optional overlay lifecycle.
- [x] Add zh-TW/en/ja wording explaining live/baked-in pointer and its optional-effect warning. Test warning priority and old project compatibility.
- [x] Run relevant lifecycle/UI suites and inspect production capture arguments.

### Task 4: Full verification and handoff

**Files:** current user guides/website feature copy as applicable, package verification, internal verification record.

- [x] Update current documentation to describe direct capture, not new editable pointer recording; leave historical release notes unchanged.
- [x] Run full solution tests, version/diff checks, Swift compilation and Windows cross-publish.
- [x] Review the final scoped patch independently, address important findings with RED/GREEN checks, and retain physical-device validation limits.
- [x] Deliver source/build status and, if packaged, verify exact source snapshot, PE architecture, notices, ZIP extraction and hashes. No formal Release.

## Execution rulings

- Continue inline in the existing isolated worktree. The user's acceptance covers the concrete design above; do not ask them to re-approve the same choices.
- Existing uncommitted work belongs to the preceding authorized fixes; preserve it and distinguish this plan's changes. Keep work uncommitted until its checks finish rather than committing unrelated partial work task by task.
