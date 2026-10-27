# Screen Annotations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Optional in-process screen annotations with unchanged teaching pointer and direct recording.
**Architecture:** Bounded pure document + event-driven Avalonia canvas/toolbar + native window adapter, owned by a disposable controller. Home observes recording state; recorder receives no new commands.
**Tech Stack:** .NET 8, existing Avalonia 11.2.5, Win32/AppKit adapters; no new packages.
**Spec:** `docs/superpowers/specs/2026-10-27-screen-annotations-design.md`

## Global Constraints

- Windows/macOS; same process, separate classes; default enabled, lazy windows.
- Keep teaching pointer and hotspot; only left drag draws; no text or partial eraser.
- Do not change OBS/recording commands, audio, project files, export, version or release.
- Record real validation separately from synthetic/headless checks.

## Review Focus

- Pausing during a drag cancels unfinished geometry and releases pointer capture.
- Failed native click-through must not leave an invisible input-blocking window.
- Screen scale, negative coordinates and custom capture bounds must not misalign strokes.
- Undo/clear/history cannot grow without bound or expose mutable mark arrays.
- Disabling, changing DataContext, screen configuration or closing must release all windows/subscriptions.

### Task 1: Bounded annotation document

**Files:** Create `src/ScreenRecorder.Core/Annotations/AnnotationDocument.cs`; test `tests/ScreenRecorder.Core.Tests/AnnotationDocumentTests.cs`.
**Interfaces:** Produce `AnnotationDocument` with Begin/Move/End/Cancel, Undo/Redo/Clear/RemoveAt; immutable marks/points and tools. Consume no UI/recording dependencies.
- [ ] Add tests: hover empty; left drag; each shape; reverse drag; cancel; whole-mark hit test; clear undo; redo invalidation; immutable data; limits and invalid coordinates.
- [ ] Run core tests RED, implement, run complete core suite GREEN; commit.

### Task 2: Canvas, toolbar and platform host

**Files:** Create focused files in `src/ScreenRecorder.UI/Annotations/`: canvas, toolbar, native adapter, controller. Test `tests/ScreenRecorder.Media.Tests/ScreenAnnotationUiTests.cs`.
**Interfaces:** Consume document; produce `ScreenAnnotationController.Update(enabled, recording, paused, region)` and `Open(owner, region)`/Dispose. Platform native adapter returns verified input-policy success; failed operation closes overlay.
- [ ] Test left-only gestures, idle hover, pointer style unchanged, cancel/release, tool state, localized accessible controls, lazy creation, pause/stop/disable/failure cleanup.
- [ ] Watch RED; implement geometry rendering, native hit-through and toolbar; run focused GREEN and complete media suite; commit.

### Task 3: Small application integration

**Files:** Modify UserSettings, SettingsViewModel/window, MainViewModel via partial, MainWindow via partial, localization partial resources; add UI/settings persistence tests.
**Interfaces:** Settings `ScreenAnnotationsEnabled=true`; MainViewModel read-only capture bounds/feature flag; MainWindow owns optional controller. No new recorder IPC.
- [ ] Test legacy missing flag defaults true; opt-out survives unrelated save; three languages; UI entry only during recording; no controller when disabled; state/range changes cancel safely.
- [ ] Watch RED; implement minimal hooks; full core/media, script/site, actionlint and diff checks; commit.

### Task 4: Real desktop verification and handoff

**Files:** Add verification evidence document.
- [ ] Render and operate real macOS annotation windows over synthetic teaching content; verify native click-through, Esc, shapes/undo and unchanged pointer behavior where environment permits.
- [ ] Attempt actual screen capture only with approved permission; no private microphone/content; inspect frames/decoding separately from UI test success.
- [ ] Run independent whole-branch review; fix important findings with regression tests; record Windows/physical limitations. No merge/push/release.
