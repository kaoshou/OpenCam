# Recording project Phase B — local acceptance

Status: **development branch only; GUI/device acceptance BLOCKED, not release-ready**.

Environment: macOS 26.5.2 (25F84), arm64; isolated official .NET SDK 8.0.425. Stable master and installed application are unchanged. No push, merge, version change, tag or Release was requested for this phase.

## Implemented scope

- Native project workspace from the existing OpenCam main window, using the same authenticated Recorder process.
- Create/save/close/reopen; pause commits finished MKV sources; finish does not remux or delete sources; a reopened project records into a new session.
- Clip-name editing with undo/redo. Save and append preserve this session's history. Reopening loads saved edits but starts a new empty undo history.
- Atomic manifest writes, writer lease, previous valid backup, explicit restore with damaged bytes retained. This is **not** cross-launch edit undo.
- Bound source handles, source hashes, packet-PTS timing, idempotent segment recovery and operation deduplication.
- Timeline trimming/reordering/video preview and edited MP4 export remain Phase C, not implemented by this phase. No fake player or export button is exposed.

## Evidence

| Check | Result | What it proves |
|---|---|---|
| Core suite after native integration | 126 passed | Manifest validation, revision receipts, lease, paths, actual second writer/terminated writer, backup and edit history |
| Focused project regression | 44 passed | Linked/moved recording paths, failed Finish retries, background safety stops, lost requests/responses, server-instance protection |
| Cross-process FFmpeg integration | 1 passed | Real FFmpeg synthetic media: 3 sources, process exit, new process adds source 4, move project, probe all sources, original hashes unchanged, no MP4 |
| Final full solution build/test | PASS; 493 tests passed, 13 skipped | .NET8 Release build: 0 warnings/errors; Core126 + Media367; skipped cases remain unverified |
| Main application launch | BLOCKED | Avalonia.Native cannot start RenderTimer: native error `-6661`; no window appeared |
| macOS GUI / screen / microphone | BLOCKED | Not demonstrated; synthetic video does not establish device permissions, actual audio or A/V synchronization |
| Windows native recording/UI | BLOCKED | No Windows host in this session |

Logs are in the branch-local `.superpowers/sdd/2026-10-08-recording-project-persistence/` workspace while acceptance remains open. The user confirmed the desktop was unlocked and displaying normally; `native-app-retry.log` still records RenderTimer `-6661`. A local development-only LaunchServices wrapper (same DLL, not a release package) instead received macOS “Operation not permitted” reading runtimeconfig under Documents. No installed application was replaced; privacy settings were not changed. A broader desktop inspection had been rejected by the privacy approval system; no indirect desktop capture was attempted. The RenderTimer error's underlying OS/session cause has not been established; [Avalonia #18895](https://github.com/AvaloniaUI/Avalonia/issues/18895) reports a similar error but does not prove this machine's cause.

## Final independent review and fix pass

The single independent review found four Important defects. Each received a reproducing failing test followed by a fix and regression run:

- Recording could resolve a replaced project path: retain directory handles and an exclusively-created output stream. FFmpeg writes MKV via stdout into that stream. Whole-project moves rebase session metadata; linked directories cannot create external sessions. Quick recording keeps its existing path output.
- Background safety stops left the workspace saying Recording: serialized status reconciliation commits completed sources, preserves the stop reason, and the workspace polls and refreshes the clip list.
- Failed Finish could not be retried: an ownership-checked project finalization retry stops retained engines first, then retries persistence/probing. Tests cover stop, cleanup, probe and metadata-save faults.
- Startup failure retains project/session ownership until cleanup and Finish succeed. An empty failed attempt can close after cleanup; its diagnostic file is retained and is not presented as a recorded clip.
- A request lost before dispatch locked the workspace: retain the original payload/operation ID and retry only against the same recorder instance. A restarted process cannot silently replay an old request.

Deferred minor: manifest canvas defaults to 1920×1080/30 instead of adopting the first source. Source geometry/timing is separate and correct; Phase C must initialize canvas/FPS before preview/export uses it.

Streamed MKV may omit the format-duration header; source acceptance uses positive packet PTS bounds. Pipe/copy throughput and long recordings require device acceptance. Saving does not export MP4. Undo/redo currently applies to clip names; timeline edits remain Phase C.

## Required manual acceptance

Prerequisites: unlocked desktop, an interactive OpenCam window, explicit OS screen/microphone permissions, a disposable test-project location with adequate space. Do not use personal/production recording files for fault injection.

1. From the normal main window, open **Recording project · Development preview**. Create a project in the test folder. Verify `project.opencam` exists before record becomes available.
2. Select the display and audio using the existing capture settings. Record three 10-second segments, pausing between segments. Verify sources appear only after pause/save completes.
3. Rename clip 1, save with the button and Ctrl/Command+S while editing the name. Undo/redo outside the text field. Record another segment; undoing the rename must not remove that recording.
4. During recording click workspace X and main-window X. Both must leave recording running and explain why closing is blocked. During a delayed save, closing must wait; failed saves retain the workspace and edits.
5. Finish the session. Check there is no automatically generated MP4 and the MKVs remain even if the quick-mode cleanup preference is enabled. Close the app, restart, reopen and add another segment. Compare earlier source SHA-256 hashes.
6. Close and move the entire project folder; reopen. A removed/changed source must block continued recording with an error. Restore its original path and retry by reopening.
7. In a disposable copy, corrupt the main manifest. Opening must remain recovery-only. Confirm restore only after reading the warning; verify a `project.opencam.damaged-*` copy and intact source files remain.
8. Repeat on Windows and macOS, in English/Traditional Chinese and light/dark themes. Verify no clipped labels, usable keyboard focus and readable text at 1280×720/high DPI.
9. Quick mode regression: all four audio combinations, pause changes for the next segment only, final MP4 playback, recovery, no orphan processes. Record settings, logs, screenshots, hashes, ffprobe metadata, actual audio and A/V results.

Failures: retain logs, manifest, journal, damaged backups and MKVs. Do not delete or overwrite sources to make a test pass. No claim that Phase B or the complete editor is accepted until the blocked cases are filled with evidence.
