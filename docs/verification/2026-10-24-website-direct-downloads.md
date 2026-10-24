# Website direct download buttons

User approved the three-button design. Implemented locally in the existing
focused-editor-revision worktree based on56ea538. No version, application code,
Release notes, workflow, merge, push, deployment or automation changes.

## Behavior

Both languages have direct Windows x64 portable ZIP, Windows x64 installer EXE
and macOS arm64 DMG links. The portable button is green and the others outlined;
desktop/tablet use two columns with macOS below left, phones use full-width
stacked buttons. Grey notes explain x64, full extraction and Apple Silicon.
Original Releases listing and localized guide remain text links underneath.

Ruling: pin website/downloads.json independently of development VERSION — a
pre-release VERSION bump must not move public download links to absent assets.
The homepage's current-version label uses that same published version. There
is no client-side API call or OS detection. Maintainers must update the manifest
after confirming public assets, as described in website/DOWNLOADS.md; otherwise
downloads intentionally remain on the previous published version.

Read-only GitHub API inspection confirmed public/non-prereleasev0.4.1 and all
three exact uploaded, non-empty attachment URLs. No large download was repeated.

## Verification

- RED: rendered actions had2 links instead of3, in
  /private/tmp/opencam-downloads-red.log.
- Initial full website run:39 passed/1 failed because the sandbox denied Swift's
  compiler module-cache write in the existing macOS icon test. No assertion or
  source change for that failure. Rerun with compiler permissions passed.
- Final website suite41 passed,0 failed and build exit0:
  /private/tmp/opencam-downloads-website-complete.log,
  /private/tmp/opencam-downloads-build-final.log. Includes a temporary source
  fixture with development VERSION9.9.9 proving published links/label stay0.4.1.
- Player/CI Node regression36 passed,0 failed:
  /private/tmp/opencam-downloads-node.log.
- Final17 Chromium groups passed:
  /private/tmp/opencam-downloads-final.log. Download layout tested in both
  languages at1440/1024/768/390/320px, minimum52px button height, equal widths,
  desktop row order, phone stacking, keyboard Tab order and200% text/no overflow.
  Existing player/touch/axe/offline/website checks retained.
- Screenshots under/private/tmp/opencam-downloads-final/screens/:
  downloads-{zh-TW,en-US}-{1440,768,390}.png. Desktop Chinese and phone English
  screenshots inspected. They render existing project preview assets, not the
  private desktop.
- Independent read-only review reported no Critical/Important or concrete minor
  defects. Actual Safari/iOS/Android/native download prompts are not certified
  by Chromium emulation or GitHub metadata inspection.
- Version consistency0.4.1 and git diff --check passed. No .NET suite rerun for
  this website-only change; the prior4 Windows preflight failures are not fixed
  or relabeled by this task.

## Website integration authorization

The user subsequently authorized merge, push and website deployment, keeping
version0.4.1. Before integration main and remote master were56ea538 and the
development worktree contained only this website follow-up. Main's unrelated
dirty spec blob25780034e07095597e0d8b6af9d8f3280b88d34e and five untracked plans
are excluded. A fresh complete website suite41 passed and website build exited0
in /private/tmp/opencam-downloads-merge-{test,build}.log. Version consistency
and git diff --check passed. The existing publicv0.4.1 Release421664814 and its
three attachment IDs/digests were read-only verified; no tag, Release or asset
changes are part of this website deployment. Preserve the development worktree.
