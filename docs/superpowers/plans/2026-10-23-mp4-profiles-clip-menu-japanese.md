# MP4 profiles, timeline clip actions and Japanese UI

Spec: user-approved in-chat export design plus the request to implement it,
add timeline rename/delete and Japanese application UI. Version stays 0.3.12.

## Constraints

Preserve prior dirty work, original media, schema and existing release notes.
No merge/push/release, automatic schedule or application dependency additions.
Keep all export validation, bound handles, cancellation and atomic publication.
Default export retains the existing strategy; compression explicitly renders.
Japanese covers the application UI; website remains Chinese/English with updated
feature text. Independent implementation tasks may run in parallel, with
serialized builds and one parent self-review (no extra reviewer agents).

## Tasks and verification

- [x] MP4 options: Core/Projects/Mp4ExportOptions.cs + ProjectExportOptions,
  Selection/Fingerprint; Media/ProjectMediaExportJobs; Recorder exporters and
  coordinator route. Original/Standard/Compact => existing CRF18/23/28;
  Original/1080p/720p bounds without upscaling, preserve aspect and even H.264
  dimensions. Custom settings bypass stream copy. Test receipt identity,
  malformed options, real decoded dimensions/audio/duration and source safety.
- [x] MP4 dialog/preferences: new ProjectMp4ExportDialog, dedicated atomic
  mp4-export.json preferences store, existing file/web chooser routes. Dialog
  runs before destination, remembers accepted choice, cancellation dispatches
  nothing. Test full/clip/web routes and state changes while dialogs are open.
- [x] Timeline context: ProjectTimelineControl + ProjectWorkspaceClipContext,
  preserve export submenu, target clicked logical clip, guard captured project,
  server and revision after dialogs/preview shutdown. Existing undo/edit routes.
  Test video/audio click and stale target cases; do not seek on right-click.
- [x] Japanese: append enum2, full JaJp dictionary, settings/CLI/flags, placeholder
  parity. New export dialog and timeline keys translated in all three languages.
  Test selection/persistence, complete key coverage and Japanese headless views.
- [x] Integrate: targeted RED/GREEN logs, complete solution Release build/test,
  Node/player + website tests/build, version/diff checks, self-review. Prepare
  an optional Windows package only after successful validation; do not publish it.

## Progress

Initial RED: /tmp/opencam-export-menu-ja-red.log (Core8 and Media12 expected failures).
Core profile/Ja GREEN and timeline19/Ja selector GREEN in
/tmp/opencam-export-profile-dialog-red.log; that run intentionally still fails
14 dialog/preference tests before their implementation.

Implemented and self-reviewed; complete build/test Core284 + Media1090 passed,
Media20 platform/hardware cases skipped, zero failed. Node31, website39/build,
version0.3.12 and diff checks passed. Final inspection added a regression for
the home screen Web export entry, then wired it to the same quality flow as the
editor; focused106 passed with one platform skip. Final callback lifetime and
export tests passed 28/28 in /tmp/opencam-mp4-main-web-final-green.log.
See docs/verification/2026-10-23-mp4-quality-timeline-japanese.md for evidence
and physical-host limitations. No new Windows package or release was made.
