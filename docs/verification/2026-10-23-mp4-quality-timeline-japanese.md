# MP4 quality, timeline clip actions and Japanese UI

Scope: approved manual MP4 quality/resolution settings, timeline clip rename/delete,
and Japanese application UI. Version remains 0.3.12. No merge, push, release,
schedule, dependency or schema change was requested or performed in this task.
Earlier worktree changes remain in place; existing release notes are unchanged.

## Implementation and self-review

- Whole-video MP4, single-clip MP4 and Web player's media.mp4 share the options
  dialog, before the destination picker. The last accepted setting is atomically
  saved separately from the main settings file, avoiding autosave overwrites.
  Cancellation sends no export; captured project/server/revision/clip guards run
  after user dialogs and after preview shutdown. Original settings retain the
  existing strategy; compression/resizing explicitly bypasses stream copy.
- Export-only canvases fit the original aspect into landscape or portrait
  1080p/720p bounds, with even H.264 dimensions and no upscaling. CRF18 remains
  the original rendering value; Standard/Compact use CRF23/28. Frame/audio
  clocks, source binding/hashing, output verification and atomic publication
  remain intact. Profile-aware fingerprints prevent cross-quality reuse.
  Automatic export on stop does not read the remembered manual export profile.
- Timeline video/audio clip context actions capture the clicked logical clip,
  without seeking or changing selection. Rename/delete use existing commands,
  undo and persistence, with stale-target guards. Export remains a nested MP4/GIF
  submenu. Removing a timeline clip does not remove original source media.
- Japanese enum value 2 is appended without reinterpreting existing language
  preferences. The catalog matches all 518 Chinese/English keys and format
  arguments; settings and CLI selection support Japanese. Application UI only:
  website remains Chinese/English, and the exported player's existing bilingual
  markup was not replaced with a new localization subsystem.
- Public README, both guides and website feature descriptions are updated.
  No historical release notes were changed. Compression can soften text/detail
  and takes re-encoding time; smaller output size is not guaranteed for all input.

## Evidence

- Initial RED: `/tmp/opencam-export-menu-ja-red.log`,
  `/tmp/opencam-export-profile-dialog-red.log`,
  `/tmp/opencam-mp4-render-red.log`, `/tmp/opencam-mp4-workflow-red.log`.
- Focused GREEN: `/tmp/opencam-export-profile-green.log` (36 Media cases),
  `/tmp/opencam-mp4-workflow-green.log` (45 cases). Real FFmpeg renders verify
  1920x1080 MP4 and 1280x720 Web media, CRF, decoded frame/audio counts,
  unchanged original source hashes, preserved existing output and cleanup.
- Full Release build/test: `/tmp/opencam-export-quality-ja-full.log`, exit 0:
  Core284 + Media1090 passed, Media20 skipped, zero failed (Media5m26s).
  Existing nullable/xUnit/Avalonia warnings remain. This full run preceded the
  final main-window Web-entry hookup described below.
- Final entry inspection caught the home screen's Web player command still
  using the legacy folder picker directly, unlike the editor. A new regression
  failed in `/tmp/opencam-mp4-main-web-red.log` before adding a shared Web-export
  callback. MainWindow now attaches/detaches this callback, just like manual
  MP4/GIF, routing both screen entry points through the quality dialog.
  `/tmp/opencam-export-quality-ja-final-focused.log`: 106 passed, one
  platform-specific case skipped, zero failed after the hookup. Final targeted
  attach/detach and export regression: `/tmp/opencam-mp4-main-web-final-green.log`,
  exit 0, 28 passed, zero failed/skipped. The preceding `-green.log` had one
  test-fixture cleanup failure: an unshown headless MainWindow had no initialized
  windowing backend when OnClosed accessed Screens. Showing the test window
  before exercising and closing it fixes the fixture; product guards/assertions
  were not relaxed.
- Player/CI/performance Node tests: `/tmp/opencam-export-quality-ja-node.log`,
  31 passed, zero failed. Website: `/tmp/opencam-export-quality-ja-website.log`,
  39 passed, zero failed; build exit 0 in `-website-build.log`.
- `node scripts/check-version-consistency.mjs` and `git diff --check` pass.

This host uses SDK8.0.425 and FFmpeg7.1.2 on macOS. Automated headless checks
cover the new dialog in Chinese, English and Japanese, complete translation key
and placeholder parity, saved Japanese selection, clicked video/audio clip
identity, cancellation and stale project/server/revision cases. These checks do
not constitute physical Windows capture/audio, native font layout or hardware
performance acceptance. No existing Windows package was overwritten.
