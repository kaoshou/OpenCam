# Screen annotations — development verification

## Scope and status

Version remains 1.0.0. Branch: `codex/screen-annotations-20261027`, based on
`40bd5f5`. No merge, push, release or package replacement is part of this change.
This is a development implementation, **not physical dual-platform acceptance**.

The component runs inside the existing UI process. No new package or process is
introduced. Recording, audio, native OBS helpers, project storage and export code
are unchanged. Optional annotation-window failures close the annotation component;
this does not promise isolation from a crash of the entire UI process.

## Usage

Follow-up: the toolbar header has a larger drag area, a main-window minimize
control, and a hide control when `Ctrl + Shift + F8` was successfully registered.
The shortcut opens the tools and toggles toolbar visibility; it yields to existing
user-selected shortcuts. Hidden controls are absent from subsequent captured frames,
but frames recorded before hiding still contain them. Window capture exclusion is
not requested. In eraser mode a left-button drag removes whole touched marks and
one undo restores the entire gesture. Real Windows/macOS desktop interaction and
recorded-video validation of these follow-up changes remain required.
The full-screen drawing surface is hidden when tools are first opened and no
marks exist; choosing a drawing tool shows it. Returning to operation hides the
surface again when empty, but completed marks stay visible for recording. The
toolbar has an independent window lifetime managed by the annotation host.

1. Settings → Screen annotations is enabled by default. Disable it to remove the
   home entry; ordinary recording does not require annotation windows.
2. Start recording, then choose Screen annotations in the supplemental action row.
   Initially the overlay is click-through and normal application operation remains.
3. Choose pen, line, arrow, rectangle or ellipse. Hover only points; hold the left
   mouse button to draw. The existing teaching pointer is not replaced by a pen cursor.
4. Choose one of seven colors and three widths. Remove a mark deletes an entire
   mark at its border; undo, redo and clear-all are available. There is no text,
   partial eraser or region eraser.
5. Esc or Operate restores click-through, retaining completed marks. Pause hides
   the overlay and cancels an unfinished gesture; resume starts in Operate mode.
6. Closing the tools, stopping, disabling or changing the capture region closes
   the component. Marks are ephemeral, not saved to a project.

Annotations remain at screen coordinates, not anchored to scrolling content.
They are baked into the captured picture and cannot be removed afterward. The
toolbar may also appear in recordings; move or collapse it as needed. Its position
is remembered only during the current application lifetime. A capture rectangle
spanning multiple displays is rejected for annotation use, without changing recording.

## Automated evidence

Environment: macOS arm64, .NET SDK 8.0.425, existing Avalonia 11.2.5; no dependency
version changes. Evidence logs are retained outside the source checkout.

- Before feature: Core 358 passed; Media 1387 passed, 23 skipped, zero failures.
- Model TDD: missing types reproduced before implementation; Core then 371 passed.
- Independent review found elongated ellipse removal falsely matched distant
  empty space. Horizontal/vertical regressions failed before the fix (2 failures).
  After perimeter-distance fix and underlying-mark regression: Core 374 passed.
- Focused canvas/settings and existing home-layout checks: initially 19 passed. Initial
  full run caught two English toolbar wrapping regressions; moved annotation
  entry out of primary Start/Pause/Stop/Edit row. Existing assertions retained.
- Actual-component Skia raster preview exposed open-path icons incorrectly using
  filled PathIcon geometry (blank line/close icons, solid shapes). A regression
  failed before changing these icons to stroked paths. Final focused suite: 20
  passed. Re-rendered canvas/toolbar previews are synthetic, not desktop screenshots.
- Full solution rerun after the review fix: Core 374 passed; Media 1402 passed,
  23 skipped, zero failures (6m7s). The subsequent icon-only change was rebuilt,
  tested by the 20-case focused suite and visually re-rendered separately.
- Website: 44 passed. First attempt was blocked by Swift module-cache sandbox
  access; rerun with access passed.
- Player/CI/performance-summary scripts: 48 passed. Actionlint and diff check passed.
- Broad native/script invocation was **not all green**: 12 native capture tests
  lacked `OPENCAM_OBS_HELPER`/`OPENCAM_OBS_RUNTIME`; one AppKit bundle probe aborted
  in the current desktop environment. Neither is counted as a physical pass.

Headless tests check actual canvas pointer events, hover/right-button behavior,
lost-left-button cancellation, unchanged Cursor property, stable toolbar controls
while drawing, localized accessible labels, settings persistence, lazy creation,
pause preservation, disposal and optional-host failures. They do not prove OS
input routing, pointer stacking, display scaling or captured-video appearance.

## Native desktop evidence and limitations

A separate, bundled native harness over synthetic teaching content verified a real
`NSWindow` handle and successful native click-through/drawing-policy property
toggles. No private screen/audio was captured. Subsequent computer interaction
reported that the Mac was locked. A follow-up transparency probe could not start
Avalonia.Native's RenderTimer (`-6661`); native opacity/visual appearance was not
verified. The adapter defensively reuses the existing region selector's explicit
native transparency policy, and closes on failure. This is not a claimed diagnosis
of a confirmed opacity defect.

**REQUIRES MANUAL VALIDATION:** Windows input routing; macOS cross-application
clicking and focus; actual teaching-pointer appearance/stacking; Retina/mixed DPI;
fullscreen/Spaces; recorded picture, audio continuity, MP4 output and performance.

## Physical acceptance checklist

Prerequisites: unlocked interactive Windows/macOS desktop; a build from this branch;
known synthetic teaching material; recording permission and a known audio source.
Keep original MKV and logs. Repeat at 20 and 30 FPS on both systems.

1. With tools closed, record and operate normally. Open tools, click the underlying
   application in Operate mode. Expected: application receives clicks, no new marks.
2. Select each drawing tool; hover and right-click, then left-drag in all directions.
   Expected: only left-drag draws, existing teaching pointer/hotspot remains, no second
   cursor, no covered/opaque desktop. Test white and dark backgrounds.
3. Use colors/widths, remove an overlapping mark, undo/redo/clear. Empty space must
   not delete distant shapes. Move and collapse toolbar; verify controls stay reachable.
4. During a held drag press pause; resume, repeat, stop, reopen tools on a later
   recording, change display configuration, and close tools. Expected: no unfinished
   strokes or invisible input blockers, no inherited stale document, recorder unaffected.
5. Press Esc in canvas and toolbar. Expected: normal application clicking resumes,
   completed marks remain. Existing F9/F10 recording shortcuts still operate.
6. Repeat on negative-coordinate secondary display, Windows 100/125/150/200% and
   Retina/mixed DPI, supported fullscreen/Spaces, plus custom region. Expected:
   pointer tip and stroke start agree and drawing is clipped to capture bounds.
7. Record five minutes with moving teaching content, annotations, audio, pause/resume.
   Play MKV and exported MP4; inspect actual frames and sound, decode both, compare
   packet timelines and observed synchronization. Compare with tools disabled.

Failure conditions: invisible click blocker, pointer replacement/doubling, opaque
overlay, misplaced/missing marks, missing content/audio, new timing drift, loss of
recording controls, exception or growing handles/memory. Preserve app/native logs,
exact commit, settings/display scaling, MKV/MP4 probe and decode results. Do not
mark physical acceptance passed solely because automated decoding succeeds.

## Review

Independent read-only whole-branch review found no critical issues and one important
ellipse hit-test defect, fixed and re-reviewed. No remaining critical/important
static findings were reported. Native acceptance limitations remain open.
