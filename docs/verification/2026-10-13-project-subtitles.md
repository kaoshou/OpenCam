# Project subtitles — development verification

Scope: naming-dialog centering, export chevron, project-owned multilingual SRT/VTT import, explicit save/drafts/history, editor preview and Web captions. VERSION remains0.3.3; this feature is not published or merged to master.

User explicitly requested focused important checks and no complex repeated review. Existing original worktree documents are untouched. No new third-party application dependency.

## Executed checks

- Core:230 passed,0 failed (parser, language inference, bounds, unique asset paths, explicit persistence/history and existing core contracts).
- Focused Media/UI:41 passed,0 failed, including an actual authenticated-pipe subtitle roundtrip. Covers import/apply/read/save, tamper detection, preview seek/off/default, timing-change protection, Web data/export, naming dialogs/chevrons and existing layout/frame/save contracts.
- Node player:16 passed,0 failed; plain text, overlaps, language/off, appearance/reset, fullscreen menu and original controls.
- Release solution build:0 errors,0 warnings in the final incremental build. Earlier Core compilation emitted the pre-existing xUnit2031 warning in BoundDirectoryTests.
- Logs: `/tmp/opencam-subtitles-final-core.log` (successful invocation exit0; initial sandbox invocation also wrote to this filename), `/tmp/opencam-subtitles-final-media2.log`, `/tmp/opencam-subtitles-final-node.log`, `/tmp/opencam-subtitles-final-build.log`. Version consistency and diff whitespace checks passed.

The initial sandbox Core run could not open MSBuild's local pipe; the same command executed successfully with host permission. No tests or assertions were relaxed.

## Constraints

Saving attached subtitles upgrades the project to schema4, unsupported by released0.3.3; back up first. No-subtitle projects retain their schema. Raw imported assets are GUID-named, checked against recorded length/hash, and never overwrite same-name imports. Cancel/removal may retain unreferenced assets for undo/recovery safety.

Subtitle time is final-timeline time. Editing order/trim or adding recordings invalidates the timing signature; Web export requires confirmation, omission or cancellation. MP4 is unaffected and captions are not burned in. A confirmation does not retime cues. Parsed cues use authenticated binary media IPC, not oversized control metadata. Web packages retain index.html/media.mp4/opencam-player.js; cue text is never evaluated as HTML/JavaScript. Parsed Web cue data has a combined64 MiB limit.

## REQUIRES MANUAL VALIDATION

1. On native Windows/macOS, check all three naming dialogs and both export chevrons.
2. Select and drop SRT/VTT of different languages with identical filenames; confirm both assets survive save, closing, removal of outside originals and reopening. Test replacement, keep-both, removal, default fallback and cancel.
3. Play, pause, seek and enter fullscreen; compare subtitle timing, language/off and size/theme/opacity/position. Preview language must not change the saved default.
4. Trim/reorder/continue recording; confirm the sync prompt and each export choice. Undo/save/reopen should preserve tracks and timing-warning state.
5. Open exported index.html offline in target browsers. Test CC choices, default-on, reset, blocked storage, touch and fullscreen. MP4 must remain caption-free; original media must remain intact.

These are not marked PASS by headless/unit tests. Prior recording/long-A/V, physical disk-exhaustion, signing/notarization, complete Release Gate and Windows FFmpeg corresponding-source limitations remain unchanged.
