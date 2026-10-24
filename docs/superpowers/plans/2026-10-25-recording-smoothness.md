# Recording Smoothness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline, with one fresh final review. User opted out of further planning questions.

**Goal:** Withdraw the confirmed slower Windows WGC entry, keep cross-platform capture controls consistent and make live teaching-pointer pacing measurable and deadline-driven.
**Architecture:** Existing capture/audio pipelines remain intact. Settings normalize stale Windows flags; Windows plan provider delegates to compatible selection. A small overlay clock owns only optional pointer timing. Existing macOS repeating timer remains.
**Tech Stack:** .NET8/Avalonia/Win32 scoped waitable timers; existing AppKit helper, no new dependencies.
**Spec:** docs/superpowers/specs/2026-10-25-recording-smoothness-design.md

## Global Constraints

- Version0.4.1; no merge/push/Release, preserve prior dirty changes and old packages.
- Never weaken audio, startup or capture assertions. No global timeBeginPeriod, busy spin, frame interpolation/duplication, automatic FPS/quality changes.
- Keep macOS capture/audio unchanged; same localized setting location and teaching-pointer behavior.

## Review Focus

- Stale Windows true flags must not activate WGC via settings or direct plan entry (Task2).
- Locked settings and language changes must not switch macOS capture mode (Task2).
- Timer failure/dispose must not leak a handle, block overlay cleanup or affect A/V (Task3).
- Slow work/clock jumps must skip missed deadlines without bursts (Task3).
- Removed editor entry must not delete legacy pointer data or bypass corrupt-track export rejection (Task1).

### Task 1: Remove editor pointer entry

**Files:** ProjectWorkspaceView.axaml/.cs; ProjectEditorChromeTests.cs.
**Interfaces:** No backend contract changes; remove PointerSettingsButton/flyout handler only.
- [ ] RED3: real zh/en/ja editor still contains unwanted action; observed /tmp/opencam-remove-pointer-button-red.log.
- [ ] Remove action and handler, keep project metadata/readers.
- [ ] GREEN relevant editor/legacy tests: observed27pass /tmp/opencam-remove-pointer-button-green.log; rerun at task-done.

### Task 2: Unified capture controls and Windows WGC withdrawal

**Files:** SettingsViewModel.cs, SettingsWindow.axaml, MainViewModel.cs; WindowsGraphicsCapturePlan.cs; localization zh/en/ja; NativeCaptureTests/SettingsViewModelTests/WindowsCaptureArgumentsTests.
**Interfaces:** CaptureModeOption adds bool Native=false; SupportsMacOsCapture and SupportsCaptureSelection identify capability; SelectedCaptureMode sets NativeCaptureEnabled only on Mac. Windows PrepareAsync clears stale NativeCaptureEnabled and delegates to compatible provider; rejects pinned WGC rather than switches silently.
- [ ] RED tests real Windows settings ignore legacy flag/save; direct Windows plan does not probe WGC and preserves audio/FPS/bounds; cancelled requests/pinned WGC fail before capture.
- [ ] RED Mac standard/SCK dropdown selection saves bool, language refresh retains selected backend, locked save cannot switch it; both platforms share a named capture picker and no native checkbox.
- [ ] Implement minimal normalization, dropdown and localized platform notices. Main Windows configuration/load/save cannot reintroduce stale flag; keep Mac SCK flags and pipelines.
- [ ] Run capture/settings/localization tests GREEN; update current bilingual guides/website text only, not historical release notes.

### Task 3: Deadline-driven pointer pacing, bounded diagnostics and final verification

**Files:** Create Windows Cursor/OverlayFrameClock.cs; modify WindowsCursorHighlightService.cs; RecordingPerformance.cs; add focused cadence tests; update internal verification.
**Interfaces:** OverlayFrameClock with monotonic timestamp and bounded wait delegates supports WaitNext(), NoteUpdate(), Snapshot(); owns scoped Win32 waitable timer in production. Internal frame-pacer math accepts injected time/wait for deterministic tests, not real sleeps. RecordingPerformanceSnapshot adds recent output FPS/seconds fields without fabricating capture drops.
- [ ] RED cadence tests: processing5ms waits only remaining~11.67ms, late work skips deadlines, cancellation/fallback bounded, metrics counts/max interval correct and no positions; recent output rates exclude startup/cumulative averaging.
- [ ] Implement60Hz monotonic schedule and scoped high-resolution wait, single static stick upload followed by position-only movement; halos/ripples retain animated redraw. Log one bounded update summary on stop.
- [ ] GREEN targeted overlay/renderer/lifecycle/performance tests. Compile Swift unchanged to verify Mac boundary.
- [ ] Full solution tests, Windows cross-publish, website41tests/build, version/diffcheck; fresh read-only whole-patch review and one TDD fix pass for important findings. Manual Windows/Mac smoothness still pending; no speed guarantees.

## Execution

Use existing focused-editor-revision worktree, base313d280. No partial commits; combined prior uncommitted work is preserved. Reuse latest pre-change full baseline1587pass/21skip/0fail; final full suite required. Retain evidence workspace (not staged or packaged) because no commits are authorized for this combined patch.
