# Editor Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve editor preview responsiveness, precise editing, keyboard control, range listening and honest save/export feedback without changing recording behavior.

**Architecture:** Extend the existing Avalonia view model, recorder-owned preview coordinator and bounded streaming decoder. Reuse authenticated IPC, source verification, render-plan effects, cancellation, draft persistence and edit commands; no second playback engine.

**Tech Stack:** C#/.NET 8, Avalonia, xUnit/Avalonia.Headless, existing FFmpeg preview/export; no new dependency.

**Spec:** `docs/superpowers/specs/2026-11-04-editor-workflow-design.md` (approved 2026-11-04).

## Global Constraints

- Recording reliability and original media safety take priority. No edits to native capture, fallback, recording format or project schema.
- No version change, push, merge, Release or public asset replacement.
- Preserve the four pre-existing Linux/GNOME dirty paths recorded in `git status` at baseline `3df676f`.
- zh-TW/en-US/ja-JP for user-facing strings; use existing responsive layout and light/dark themes.
- At most one running static preview and one pending latest position. No default parallel decoder/pre-generated proxy scheme.
- Snap threshold 8 DIP, enabled initially, optional off and Alt bypass. Frame positioning uses canvas rational FPS.
- Audio meter uses actual effects-applied PCM before preview mute, at most 10Hz, fixed-size numeric metadata associated with playback generation.
- Existing mixed mic/system audio is not separable; no independent-source controls are added.
- Do not claim physical playback, hardware or another OS passed based on synthetic/headless tests.

## Review Focus

1. Rapid seek/quality changes while a prior request owns the media slot: latest request must win without parallel work or stale pixels (Task 1).
2. Fractional FPS and sub-frame/near-end positions: no cumulative rounding, overflow or invalid final frame (Task 2).
3. IME and nested editable controls/popups: their keys must never trigger editor transport or destructive commands (Task 3).
4. Range endpoints within clips and compound clips: exact audio bound, fades retained, no leaked samples or overlapping output after repeat (Task 4).
5. Export completion from an older revision/server: do not label old media as the current edit; failures preserve selection/drafts (Task 5).

## Preparation and verification commands

Execution record: user approved native/inline execution in an isolated worktree. All five implementation areas are wired and independently reviewed. Detailed evidence, deviations and unverified physical/performance acceptance are tracked in `docs/testing/editor-workflow-validation-20261104.md`; the task checklists below remain the original acceptance checklist, not a claim that hardware benchmarks passed.

Inspect `git status`, branch, worktree metadata and current sources. The current checkout is a normal checkout on a development branch, not a linked worktree. Ask for worktree consent before creating one, per worktree skill; do not silently relocate/copy dirty files. Use a scoped new branch/worktree from `3df676f` if consent is given; carry only the approved spec/plan, not GNOME prototypes.

Use installed .NET SDK with `DOTNET_ROLL_FORWARD=Major` for net8 tests on this Mac. Do not run builds concurrently in the same bin/obj. Test runner needs a local socket; use approved sandbox escalation if denied, not a test bypass.

Targeted command pattern (replace filter):

`DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --no-restore -m:1 /nodeReuse:false --filter '<filter>'`

Full completion: solution Release build, solution tests with TRX, website tests/build, version consistency and `git diff --check`. Record skips and pre-existing warnings separately. Create plan-specific ledger via executing-plans skill, including decisions, RED/GREEN evidence, commit IDs and pending manual acceptance.

---

### Task 1: Single-flight immediate preview scheduling

**Files:**
- Create `src/ScreenRecorder.UI/Projects/ProjectWorkspacePreviewScheduling.cs`.
- Modify `ProjectWorkspacePreview.cs`, `ProjectWorkspaceTimeline.cs`, `ProjectWorkspacePreviewOptions.cs`, `ProjectWorkspaceView.axaml.cs`, `ProjectWorkspaceThumbnails.cs` in the same directory.
- Test `tests/ScreenRecorder.Media.Tests/ProjectScrubbingTests.cs`, `ProjectPreviewPresenterTests.cs`; create `ProjectPreviewSchedulingTests.cs`.

**Interfaces:**
- Consumes `PollPreviewAsync()`, `Seek(long)`, `BeginPreviewScrub()`, `EndPreviewScrubAsync()` and existing state/epoch/quality guards.
- Produces `event EventHandler? PreviewRequested`, `RequestPreviewRefresh()` and `DrainPreviewRequestsAsync()` on the workspace VM. View attaches/detaches the event only during its lifetime; dispatcher schedules the drain without an async-void unobserved exception.
- Effective request quality is P360 while scrubbing unless selected quality is already P360; otherwise the chosen quality. Settled refresh tracks quality as well as ticks.

- [ ] Write failing tests: seek requests immediate work without timer; latest of many queued seeks wins; no overlapping static or thumbnail worker; released scrub same ticks refetches selected quality; newer project/revision/server/quality rejects old results; close detaches pending view work.
- [ ] Run the tests and retain expected failures. Existing scrub fixtures may need to distinguish low/high quality acknowledgements without weakening generation assertions.
- [ ] Implement bounded drain with one pending flag, retaining the old picture only while it remains valid for the current project/revision. Kick once after completion if a newer request arrived. Dispatch only from a live view; no background work started just by constructing a VM in tests.
- [ ] Add expected-error logging/status for scheduling faults without overwriting first project failure; retain polling as fallback. Instrument request/display latency separately from decoder timings and aggregate only bounded numeric data, not media/path content.
- [ ] Run all preview/presenter/scrub/background-media/bitmap tests and Release build. Capture baseline and changed repeated-seek request counts; no claimed speedup from fake latency.
- [ ] Commit scoped files: `Improve editor preview request scheduling`.

### Task 2: Frame stepping, snapping and trim gestures

**Files:**
- Create `src/ScreenRecorder.UI/Projects/Editor/EditorTimelineMath.cs`, `ProjectWorkspacePrecision.cs`.
- Modify `Editor/ProjectTimelineControl.cs`, `ProjectWorkspaceTimeline.cs`, `ProjectWorkspaceView.axaml`, `ProjectWorkspaceView.axaml.cs`.
- Create `tests/ScreenRecorder.Media.Tests/EditorTimelineMathTests.cs`, `ProjectPrecisionEditingTests.cs`; extend `ProjectTimelineContextMenuTests.cs` and scrub tests.

**Interfaces:**
- `EditorTimelineMath.StepFrame(long ticks, int direction, long duration, ProjectRational fps) -> long` computes target frame from absolute rational arithmetic, bounded to last valid output frame. Use wide integer math for intermediate products.
- `EditorTimelineMath.Snap(long ticks, IEnumerable<long> targets, long tolerance, long duration) -> long`: closest target in tolerance; tie chooses earlier; out-of-range targets ignored.
- VM produces `Task StepPreviewFrameAsync(int direction)`, `Task SeekRelativeAsync(long deltaTicks)`, `bool TimelineSnapping`, `string SelectionRangeText`.
- Trim UI continues to consume `TrimEdgeAsync(Guid clipId, bool start, long deltaTicks)`; no new project edit kind.

- [ ] Write failing tests for 20/30/30000÷1001 FPS, hundreds of forward/back steps, off-grid seek, zero/tiny duration, last frame and wide integer math. Define frame crossing behavior: forward selects the first frame boundary strictly greater than ticks, backward the last strictly smaller, both bounded.
- [ ] Write failing headless gestures: 8-DIP snap scales with zoom; toggle and Alt disable; nearest/tie rule; handles win over move hit test; drag previews once, release commits once, Esc/capture loss commits zero; changed revision/project cancels proposal; groups/compound invalid edges are rejected by existing edit validation.
- [ ] Run RED, implement math and asynchronous step that confirms Stop before seeking, discards intent if project/revision changed, and requests immediate preview via Task 1.
- [ ] Implement trim proposal overlay and target/delta text. Do not alter retained-media model on pointer move. Reject zero/negative retained interval; original source range remains untouched. SetRangeStart/End and range summary stay current when project timeline changes.
- [ ] Add accessible previous/next frame buttons and snap toggle in a wrapping secondary control area, not an overflowing single toolbar row. Add shared three-language strings in Task 3 localization file now if needed.
- [ ] Run timeline/edit/undo/scrub/layout tests and build. Record actual mixed-FPS source exercise separately from math tests.
- [ ] Commit scoped files: `Add precise editor timeline controls`.

### Task 3: Scoped keyboard commands and clear hints

**Files:**
- Create `src/ScreenRecorder.UI/Projects/Editor/EditorKeyboardCommands.cs`, `src/ScreenRecorder.Core/Localization/LocalizationService.EditorWorkflow.cs`.
- Modify `ProjectWorkspaceView.axaml.cs`, `ProjectWorkspaceFullscreenSeek.cs`, `ProjectWorkspaceView.axaml`, and the existing localization static initializer in `LocalizationService.Annotations.cs`.
- Create `tests/ScreenRecorder.Media.Tests/EditorKeyboardCommandsTests.cs`; extend `ProjectWorkspaceLayoutTests.cs`, `ProjectPreviewControlsTests.cs`.

**Interfaces:**
- `EditorKeyboardCommands.Resolve(Key key, KeyModifiers modifiers, bool ownsInput, bool mac) -> string?` returns one of save/undo/redo/playPause/framePrevious/frameNext/secondPrevious/secondNext/markIn/markOut.
- `EditorKeyboardCommands.Hint(string command, bool mac) -> string` is the single label source for default bindings; no false claim of configurable editor settings.
- Existing view `ResolveShortcut` delegates to the shared resolver for compatibility with current tests. UI dispatch consumes Task 2 methods and existing commands.

- [ ] Write RED tests for exact modifiers and defaults from spec, Mac Cmd versus Windows/Linux Ctrl, inputs nested inside numeric controls, ComboBox, sliders, editable drop-downs, menus and IME; focused button Space produces exactly its native action, not transport twice. Undo inside text remains native; explicit save remains available unless modal owns input.
- [ ] Implement focus ownership using control ancestry and handled/composition state, not TextBox-only checks. Keep Escape precedence for drag/fullscreen/dialog; no new global registration.
- [ ] Connect toolbar hints, automation names and expandable keyboard help using the same mapping. Add complete zh-TW/en-US/ja-JP strings for Tasks 2–5 through `AddEditorWorkflowStrings()`.
- [ ] Run keyboard/layout/preview/fullscreen tests, assert all new keys resolve in three languages without fallback key text. Render normal and minimum-size light/dark editor screenshot for visual inspection.
- [ ] Commit: `Improve editor keyboard controls and hints`.

### Task 4: Bounded range preview, repeat and real mix meter

**Files:**
- Modify `src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs`.
- Modify `src/ScreenRecorder.Recorder/Services/ProjectIpcDispatcher.cs`, `ProjectPreviewCoordinator.cs`.
- Modify `src/ScreenRecorder.Media/Projects/ProjectPreviewDecoder.cs`; create `PreviewAudioMeter.cs` there.
- Create `src/ScreenRecorder.UI/Projects/ProjectWorkspaceRangePreview.cs`.
- Modify `ProjectWorkspacePlayback.cs`, `ProjectWorkspaceTimeline.cs`, `ProjectWorkspaceView.axaml`, `ProjectWorkspacePreviewOptions.cs` in UI Projects.
- Create `tests/ScreenRecorder.Media.Tests/ProjectRangePreviewTests.cs`, `PreviewAudioMeterTests.cs`; extend `ProjectPreviewSoundTests.cs`, `ProjectPlaybackLifecycleTests.cs`, `ProjectPreviewDecoderTests.cs`.

**Interfaces:**
- Append nullable `PreviewEndTicks` to `ProjectRequest`, pass as optional final `long? endTicks = null` to coordinator `PlayAsync`; null retains current whole-project behavior.
- `ProjectPreviewSettings.EndTicks` init property is nullable; decoder validates `start < end <= duration` and uses absolute original timeline for effect/fade calculations. Do not slice/rebase the recipe and inadvertently move fades.
- Add fixed record `ProjectPreviewAudioLevel(double Peak, double Rms, bool Overload)` in IPC reply metadata; append nullable level to `ProjectPlaybackState`. Generation already identifies owner. Numeric values must be finite/bounded; no PCM IPC.
- Media `PreviewAudioMeter.Observe(ReadOnlySpan<byte> floatStereoPcm)` consumes complete float32 stereo samples; bounded accumulator with locked snapshot and reset, no growing buffer. `ProjectPreviewSettings` owns optional numeric level callback; coordinator throttles publication to 100ms with monotonic clock.
- VM produces `CanPreviewRange`, `PreviewRangeAsync()`, `RepeatPreviewRange`, range-aware pending intent and localized meter/status properties.

- [ ] RED tests: invalid/partial range rejected before opening output; absent end preserves old behavior; end inside clip or compound content bounds video frames and audio samples; zero-audio clips still use output clock; partial final block has no tail leakage; EOF/exact boundary stable. Use existing render frame/sample rounding conventions and explicitly assert endpoint sample count.
- [ ] RED tests for repeat stop acknowledgement before new Play, rapid range changes, stop during pending Play, quality change, revision/server replacement, lost UI lease, close/cancel; maximum active output count one. UI repeats only after successful clean completion at bound, never decode error or unconfirmed stop.
- [ ] RED meter tests with known float PCM: zero/tone/stereo peak and RMS, non-finite input, overload before clamping, before-mute versus after effects, complete stereo blocks, reset/stale generation/throttle; no meter value from an older generation accepted by UI.
- [ ] Implement endpoint clipping using the existing absolute render plan, trim FFmpeg audio/video job lengths where possible and enforce exact read/write counts at decoder boundary. Do not merely cancel from UI elapsed time; disposal/lease/shutdown guards remain mandatory.
- [ ] Implement meter capture immediately before `settings.Audio.Copy`, numeric callback bounded at 10Hz; query carries only current/fresh level. Mark no data when stale, muted-output status separately; stop clears meter. Keep output-device volume semantics separate.
- [ ] Add preview-range/repeat controls adjacent to range summary, small meter with textual state and accessible name; summarize selected clip gain/mute/fades without rebuilding inspector.
- [ ] Run preview/coordinator/IPC/sound/fullscreen/cancellation suite and build. Perform actual FFmpeg range decode with reference PCM and inspect endpoint/count/PTS; actual listening/sync on hardware remains separate acceptance.
- [ ] Commit: `Add bounded range listening and preview mix levels`.

### Task 5: Save/export clarity, preservation and final acceptance

**Files:**
- Modify `src/ScreenRecorder.UI/Projects/ProjectWorkspaceViewModel.cs`, `ProjectWorkspaceSaveLifecycle.cs`, `ProjectWorkspaceExport.cs`, `ProjectWorkspaceView.axaml`.
- Extend `tests/ScreenRecorder.Media.Tests/ProjectWorkspaceTests.cs`, `ProjectManualExportTests.cs`, `ProjectExistingExportTests.cs`; create `ProjectEditorStatusTests.cs`.
- Update `docs/USER_GUIDE.zh-TW.md`, `docs/USER_GUIDE.en-US.md`; create `docs/testing/editor-workflow-validation-20261104.md` including manual checklist.

**Interfaces:**
- VM `string CurrentExportStatusText` distinguishes none/current successful/older revision/running/failed/canceled, using existing `RecordingExportStatus.Revision`; do not mark selected-clip export as a full-project export. Track display association to project and server identity, reset on replacement.
- Existing `SaveStatus` remains project/draft status; keep operation/preview/export error category separate so preview error alone is not mislabeled save failure. Existing `Error` diagnostics remain observable, not silently suppressed.

- [ ] RED tests: saved without export, export success then edit, undo/revision differences, project/server replacement, clip-only export versus project export, failed lookup/save/export, canceled picker, canceled/failed output retains selected clip/range/playhead/draft. Edits that change timeline must still invalidate now-invalid ranges.
- [ ] Implement status and targeted notifications, avoid replacing clip collections or focused inspector unnecessarily. Ordinary poll must not erase a meaningful pending failure. No automatic export/retry added.
- [ ] Run all targeted tests, solution build and entire solution test suite with TRX. Run `npm test --prefix website`, `npm run build --prefix website`, `node scripts/check-version-consistency.mjs`, `git diff --check`.
- [ ] Exercise reference media in the editor; measure first/seek/cross-clip/range-repeat latency, process peak and memory with actual source verification on. Record methodology, hardware, media FPS and original/changed results; if physical output unavailable, mark BLOCKED rather than presenting mocks as measured speedup.
- [ ] Document per-platform physical acceptance: Windows/macOS/Linux normal/minimum window, dark/light, native audio output, typing/IME, range repeat and close. Preserve older validation evidence; no stable-release claim without physical acceptance.
- [ ] One fresh independent whole-branch code review, scoped to this plan. Address important findings with RED/GREEN and rerun full suite; ledger minor deferred findings honestly. No delegated implementation unless user chooses it.
- [ ] Commit only scoped files, leave unrelated original four dirty paths unchanged, report local commit/test evidence and any incomplete platform acceptance. Do not push/merge/release.

## Self-review

All five design stages map to Tasks 1–5. Shared interfaces: Task 1 refresh scheduling is consumed by frame-step in Task 2 and shortcut dispatch in Task 3; Task 4 adds optional preview-only IPC metadata without project schema changes; Task 5 uses existing revisioned export records and resets display association. Endpoint and meter generation tests explicitly cover the highest-risk new preview behavior. Exact implementation internals, layout grouping and test fixture structure remain builder decisions constrained by the spec.
