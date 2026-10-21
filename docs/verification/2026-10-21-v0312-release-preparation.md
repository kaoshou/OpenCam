# OpenCam 0.3.12 release preparation

## Scope and authorization

The user explicitly requested finalization, a +0.0.1 version bump, merge, push, build and Release. VERSION changes from 0.3.11 to 0.3.12. At preparation, GitHub's latest public release is v0.3.10 (`c25680b4069fa4132ce3b5c2740de390f1a6666f`); v0.3.12 tag and Release both return 404. This release also carries the audio-only preflight developed for 0.3.11. No scheduler is re-enabled.

Feature implementation and regression evidence: [GIF and clip export](2026-10-21-gif-and-clip-export.md). Release notes and both guides describe whole-video GIF, native nested clip MP4/GIF menus, silent GIF settings and the five-minute warning without a duration cap. No new product behavior is added during release preparation.

## Local verification

- SDK 8.0.425 at `/tmp/opencam-sdk8-20261020`, FFmpeg 7.1.2 from `/tmp/opencam-task2-ffmpeg.GOzyFj`.
- Fresh restore and Release build: `/tmp/opencam-v0312-restore.log`, `/tmp/opencam-v0312-build.log`; build has zero errors and four existing nullable/xUnit/Avalonia warnings.
- Full release test log: `/tmp/opencam-v0312-full.log`, exit 0: Core 255 and Media 975 passed (1230 total), 20 skipped, zero failures; Media took 5m04s. This run tested the freshly built 0.3.12 binaries.
- Player/CI tooling: 29 passed, `/tmp/opencam-v0312-node.log`.
- Website: 39 passed and build exit 0, `/tmp/opencam-v0312-website-final.log`, `/tmp/opencam-v0312-website-build.log`. The first run detected stale guide version labels and a sandbox-denied Swift cache write; labels were corrected. A temporary module cache alone then exposed iconutil sandbox failure (Invalid Iconset); allowing native tooling completed the unchanged test. No assertion was weakened.
- Version consistency and whitespace checks run before commit.

## Publication gates and limitations

Use only exact 0.3.12 Windows/macOS package artifacts from the same successful build SHA; extra test-evidence artifacts are expected. All three verify jobs and both package jobs must succeed, automatic publish must be skipped, and same-SHA Pages must succeed. Check raw ZIP API digests before extraction, complete Windows portable extraction and PE installer headers, readonly DMG integrity/version/arm64, and SOURCE/license/notices. Create a draft first; all three uploaded assets must match local names/sizes/SHA256 before public non-prerelease publication and final tag/asset checks.

This is author self-review, not independent review or completed physical Release Gate. Native device/UI/GIF-viewer/high-DPI/long-duration/resource testing and the prior platform limits remain explicitly pending in bilingual release notes. Unsigned/not-notarized packages and pending complete Windows FFmpeg corresponding-source verification are disclosed. Source project format remains unchanged.

Preserve the development worktree. Main checkout's unrelated modified design spec has hash `25780034e07095597e0d8b6af9d8f3280b88d34e`; its five untracked plans are not part of this release and must not be staged.
