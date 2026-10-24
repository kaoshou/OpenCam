# Editable Pointer and Player Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compact reading controls and usable keyboard navigation, followed by editable post-recording pointers on Windows/macOS.
**Architecture:** Keep existing player export packaging. Gate pointer persistence/UI on a proven source-PTS/monotonic-clock bridge, then share composition between preview and all exporters.
**Tech Stack:** Embedded HTML/JS/CSS, Playwright, .NET 8, platform capture helpers and FFmpeg.
**Spec:** `docs/superpowers/specs/2026-10-24-player-navigation-and-editable-pointer-design.md` (approved by user).

## Global Constraints

- No version change, merge or release. Development-branch pushes for independent Windows validation are explicitly authorized; no website deployment. Preserve existing work and recording safety checks.
- Both Windows and macOS; Chinese/English/Japanese application strings. macOS first version targets ordinary desktop operation, with user-approved always-visible-in-region pointer semantics rather than following other applications' auto-hidden cursor state; explain this in settings/help.
- No native cursor in editable recordings; normal desktop cursor remains visible.
- No invented clock offset, no interpolation across gaps >100ms, no unbounded in-memory track.
- Schema 5 only for new pointer data; old media cannot remove baked-in cursors.

## Review Focus

- Search/select/range keys must never accidentally seek or toggle fullscreen.
- Keyboard navigation must reach hidden sidebar panels after wide mode, and remain useful without subtitles.
- Short mobile viewports and 200% text must not hide the settings close control or transcript.
- Delayed startup/progress and VFR capture must not shift pointer timestamps.
- Partial sidecars and stale output receipts must never produce an apparently successful wrong-pointer export.

### Task 1: Player controls and navigation

**Files:** Modify `src/ScreenRecorder.Recorder/WebPlayer/player.html`, `tests/browser/accessibility.mjs`, `docs/WEB_PLAYER_EXPORT.md`.
**Interfaces:** DOM links `skip-player`, `skip-chapters`, existing `skip-transcript`; focus stage/chapter-tab/transcript-tab, restore sidebar. Existing dialog IDs unchanged.

- [x] Add browser assertions for three keyboard destinations from wide mode, search/range isolation, compact reading toggle and help reflow at 390px. Missing destinations failed before code edits.
- [x] Run RED browser suite; new navigation and compact size checks failed as expected. Evidence `/private/tmp/opencam-pointer-red.log`.
- [x] Implement focus-aware navigation, native small disclosure button, responsive help groups, and ignore modified/composing playback keys. Preserve existing controls behavior.
- [x] Run browser suite (19 groups) and Node/CI (36 tests), inspect desktop/mobile screenshots, update guide; embedded export tests 6/6. Independent review found no critical/important issues.
- [x] Commit task and record evidence: c660bd2.

### Task 2: Capture timing feasibility and controlled clock prototype

**Files:** Inspect `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`, Windows capture providers, macOS `Native/OpenCamScreenCapture/main.swift`, media progress tests; create focused timing investigation under `docs/verification/2026-10-24-editable-pointer-clock.md`.
**Interfaces:** Required output is source-video PTS mapped to monotonic acquisition time, per segment; progress receipt time is not this interface.

- [x] Trace frame acquisition, PTS normalization, audio buffering, first-frame ACK and segment sealing for each capture route.
- [x] Controlled synthetic test: vary input start delay and progress delivery while keeping known frame timestamps. Receipt-time inference changes with delivery delay; no verified OS acquisition bridge established.
- [x] Document exact missing capture-clock boundary and alternatives in `docs/verification/2026-10-24-editable-pointer-clock.md`. Windows helper/custom FFmpeg choice requires architecture expansion; no guessed mapper implemented.
- [x] Record findings in native foundation commits 72bf428/69cd9c5. User approved owned native helpers; synthetic acquisition-clock/PTS bridge passes, physical alignment remains manual validation.

### Task 3: Bound pointer recording and persistence (after Task 2 gate)

**Files:** New Core pointer models and Media pointer interpolation; Infrastructure bound sidecar reader/writer; Windows/macOS samplers; modify recording orchestrator and schema/draft/validation paths.
**Interfaces:** Per-source bounded samples with anchor from Task 2, source-relative coordinates, visibility/button transitions, immutable SHA256 identity; resolver maps source PTS to optional position.

- [x] Tests first: negative monitor origins, Retina/Y-axis, invisible outside region, pause/new segment, no interpolation over >100ms or visibility transitions, malformed/unowned sidecar, complete-prefix recovery.
- [x] Implement at most 120Hz collection with bounded buffers and explicit incomplete status; cursor-free capture and no overlay only in opt-in mode.
- [x] Validate schema 1/3/4 compatibility, schema 5 rejection by old validators, no downgrade through subtitle/compound edits; add sidecar ownership and recovery tests.
- [x] Integrated Core336/Media1183 passed, 21 platform/manual cases skipped; native synthetic checks passed. Deadline regression added for final suite.

### Task 4: Shared pointer composition and editor

**Files:** Core project settings/fingerprint/history, Media render plan/jobs, Recorder exporters/preview, UI project settings/localization.
**Interfaces:** Project-wide style {stick, arrow, dot, hidden}, size 50–200%; shared resolver from Task 3, transformed through source crop/canvas to output coordinates.

- [x] Tests first: tip anchored on scale, trim/reorder/compound mapping, old mixed sources, output resizing, stale receipt invalidation, cancellation/missing sidecar.
- [x] Implement transparent pointer graphics, default stick length approximately56/shaft3px at1080p; bounded shared compositor; visible overlay disables copy-fast-path.
- [x] Add opt-in recording setting and editor style/size/reset in zh/en/ja with save/undo/redo.
- [x] Shared still/playback/MP4/GIF/web MP4 compositor verified using generated media and unchanged audio regressions; latest export matrix9/9 passed.

### Task 5: Integration and handoff

- [x] Update guide/website descriptions of opt-in/new recordings, editable styles and full-folder backups.
- [x] Browser19 groups, Node36, website41/build and native synthetic tests passed; desktop/mobile screenshots inspected. Local full suite passed; physical platform limits remain documented.
- [ ] Fresh whole-branch review, address important findings with regression tests, preserve evidence and hand off without publishing.
