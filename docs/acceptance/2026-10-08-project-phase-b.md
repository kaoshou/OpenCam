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
| Selected project/UI/media regression | 90 passed | IPC authorization, repeated commands, lost responses, close protection, source retention, undo persistence, existing UI logic |
| Cross-process FFmpeg integration | 1 passed | Real FFmpeg synthetic media: 3 sources, process exit, new process adds source 4, move project, probe all sources, original hashes unchanged, no MP4 |
| Full solution restore/build/test | PASS; 482 tests passed, 13 skipped | .NET8 Release build: 0 warnings/errors; Core126 + Media356; skipped cases remain unverified |
| Main application launch | BLOCKED | Avalonia.Native cannot start RenderTimer: native error `-6661`; no window appeared |
| macOS GUI / screen / microphone | BLOCKED | Not demonstrated; synthetic video does not establish device permissions, actual audio or A/V synchronization |
| Windows native recording/UI | BLOCKED | No Windows host in this session |

Logs are in the branch-local `.superpowers/sdd/2026-10-08-recording-project-persistence/` workspace while acceptance remains open. `native-app.log` records the actual launch failure. A broader desktop inspection was rejected by the privacy approval system; no indirect desktop capture or security workaround was attempted. The error's underlying OS/session cause has not been established.

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
