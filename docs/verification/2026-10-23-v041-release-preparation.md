# OpenCam 0.4.1 release preparation

The user explicitly authorized version +0.0.1, merge, push, build and Release.
Version changes from0.4.0 to0.4.1. Release description contains only this version's
new features and changes in Chinese/English. Historical release notes unchanged.
No recurring automation is to be enabled. Existing main dirty spec and five
untracked plans remain outside this change; development worktree is preserved.

## Scope

Web player reading preferences, single non-modal settings overlay, complete
selected-language HTML/TXT downloads, keyboard/focus navigation and responsive
phone/tablet layouts; website/guide navigation, contrast and image layout.
No recording pipeline, project/player schema or application dependencies changed.
Playwright/axe are isolated dev-only dependencies in tests/browser. Details and
RED/GREEN evidence:2026-10-23-web-reading-accessibility.md.

## Local verification

Fresh restore and complete Release solution build/test exited0 in
/private/tmp/opencam-v041-{restore,full}.log, SDK8.0.425 / FFmpeg7.1.2:
Core284 + Media1092 =1376 passed,20 skipped,0 failed; Media5m29s.
Four existing nullable/xUnit/Avalonia warnings remain. No runtime source changed
after this verification. Version consistency and git diff --check passed.
Node player/CI/performance38 passed in /private/tmp/opencam-v041-node.log.
Website39 passed in /private/tmp/opencam-v041-website-final.log; site build passed.
The first website run identified the guides' outdated0.4.0 version reference;
both guides were updated to0.4.1, with the original assertions retained.
Both native helper suites passed in /private/tmp/opencam-v041-native{,-audio}.log.
NuGet vulnerability check found no known vulnerable packages in the solution.
Fresh sixteen Chromium groups passed in /private/tmp/opencam-v041-browser.log,
including phone/tablet touch emulation, portrait/landscape and rotation while
settings are open. Actual Safari/iOS/Android and screen readers are not claimed
validated. Existing physical recording/long-duration/platform acceptance remains
manual as listed in MANUAL_TEST_CHECKLIST.md, not a Stable-acceptance claim.

## Publication checks

Before integration remote master was018aee5e0ddb17228b281cbefe9640560d1fec86.
Both v0.4.1 tag and Release API returned404. Latest public release isv0.4.0.
Use only exact current-SHA run packages, verify original Actions ZIP SHA256,
fully extract Windows portable and validate PE/companion files/notices, verify
and readonly-mount macOS DMG and check arm64/version/source/notices, then detach.
Create a draft, upload all three expected assets, validate API name/size/digest
and uploaded state, only then publish. Never reuse earlier version/run packages.
If standard CI fails, inspect complete exceptions/evidence before any repair or
alternative packaging decision; earlier independent-packaging authorization
covered0.4.0 and is not treated as a new bypass for0.4.1.

License notice verification does not certify complete corresponding-source
coverage for the pre-existing Windows third-party FFmpeg distribution. Preserve
its source-status notices and do not claim that historical audit is complete.
No licensing/package dependency changes are included in this version.
