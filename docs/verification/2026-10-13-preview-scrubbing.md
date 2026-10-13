# Preview scrubbing and compact controls

Scope: retain and progressively replace the preview during timeline/fullscreen scrubbing; compact preview controls. No recording/export, project schema, version or release changes.

Evidence:

- Before correction: `/tmp/opencam-scrub-red-run.log`, 3 failures / 1 pass. Pointer dragging cleared the displayed frame, playback restarted instead of pausing for scrubbing, and wide-window controls measured 109 px against an 88 px maximum.
- After correction: `/tmp/opencam-scrub-related-final.log`, 55 passed / 2 platform-specific skipped / 0 failed, SDK 8.0.425, FFmpeg 7.1.2. Covers pointer drag/release, progressive frame publication, delayed stop, explicit stop canceling resume, stale final-position/project/revision/quality protection, existing playback, fullscreen, timeline and layout behavior.
- Headless rendering of the actual updated UI using original demonstration media: `/tmp/opencam-scrub-preview/preview_editor_zhtw.png` and `preview_editor_enus.png`. Visual inspection of the Chinese editor confirmed the single-row wide-layout controls and increased preview space. This is not native device acceptance.
- Initial sandbox test execution was blocked by testhost socket permissions. Reused the built tests with permitted execution; no test assertions were bypassed. NuGet online audit produced NU1900 network warnings; no online vulnerability-audit pass is claimed.

Behavior: at most one frame decode is in flight. Same-project/revision/quality frames completed during a drag may replace the displayed image even when the pointer has moved; the requested playhead remains unchanged. After release, intermediate frames may not overwrite the final position. Playing preview stops once and resumes at the release position; an explicit stop prevents resumption. Quality/project/revision changes still clear incompatible images.

Controls: play/time stay together; quality, zoom, sound, fullscreen and subtitle menu share a compact wrapping toolbar. Caption language and appearance remain in the subtitle menu. No empty caption-warning row.

Remaining manual check: native Windows/macOS dragging with real high-resolution media, fast forward/backward seeks, clip boundaries, capture loss and fullscreen. Decode refresh speed remains dependent on media, storage and CPU; no 30/60 FPS scrubbing or native audio/device acceptance is claimed.

## Range markers

- Mark in renders a fixed teal flag/vertical line immediately, independently of the playhead. Mark out renders an orange flag and highlights the selected interval above both opaque video cards and audio lanes.
- A new start clears the previous end. Missing-start, equal-end and reversed-end selections show localized guidance and cannot enable Delete range. Marking alone changes neither project duration nor dirty state.
- `/tmp/opencam-range-red.log`: both new regression cases failed against the previous implementation (retained old end / no visible start line).
- `/tmp/opencam-range-green.log`: Release build and 22 relevant tests passed, 0 failed, 0 skipped. Includes actual timeline drawing commands and existing IPC range-delete/undo, pointer scrubbing and editor layout tests. Existing NU1900 network warnings remain, no online audit pass claimed.
- Native-device visual/manual acceptance remains required; no version change, commit, push or Release performed for these changes.
