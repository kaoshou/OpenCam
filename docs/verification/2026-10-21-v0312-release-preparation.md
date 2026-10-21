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

## First CI attempt: evidence, not a release

Commit `c1bb284880af45f327092635fdb83093a9d76847` was fast-forward merged and pushed. The merged full Release test also passed Core 255 / Media 975, 20 skipped (`/tmp/opencam-v0312-merged-full.log`). Build run 39575411759 failed; same-SHA Pages 39575411865 and macOS verification succeeded. Both package jobs were skipped. No tag, draft, Release or package download was performed.

- Linux: Core 255 passed; Media 877 passed, 81 skipped, one failure. `ClipListPolishTests.ContextMenuTargetsClickedClipAndGroupRowsConnect` failed before its body, in `HeadlessUnitTestSession.EnsureApplication` → `Dispatcher.ResetForUnitTests` → pending text layout, with missing `IFontManagerImpl`. Full log: `/tmp/opencam-v0312-portable-failed.log`. Artifact 12334606264 raw ZIP SHA256 `5dbcf42a34c99f75e6a62174174629e9abc266f97e929d28249a307af3629c14` matched the API; TRX and environment were inspected.
- Windows: Core 252 passed, 3 skipped; Media 900 passed, 79 skipped, two failures. `ProjectGifExportDialogTests.ChosenSettingsAreUsedForExport(0,1,480,15)` hit the same pre-body headless font-service failure. `DirectPreflightTests.LiveOutputCanBeDisplayedWithoutAContainerOrDecoder(false,None)` retained 425984 trailing bytes: startup 21177.74ms, stop 142.60ms, 15-second caller deadline canceled, 37750784 total bytes, 10 reported frames / 1.8 seconds. This distinguishes delayed startup from a long stop but does not establish the delay's cause. Full failures: `/tmp/opencam-v0312-windows-failed.log`; artifact 12334144788 SHA256 `8cb25d1408c2a928b10a1163a85f03ccb52fcdce7a574fe77aacf885cb6d982b` matched; both full TRX exceptions and environment were read. Hosted SDK was 10.0.401 and pinned Windows FFmpeg n8.1.3-9; no causal inference is made from these versions.

Avalonia 11.2.5 source confirms that headless teardown disposes the locator scope before resetting the dispatcher, and the next setup resets pending jobs before creating services. The existing after-test drain does not identify who posts later work. Local closed-window/language/GIF/bookmark-sequence and idle-gap experiments did not reproduce the failure; they were removed, not presented as a fix or retained as misleading regressions.

The next attempt adds test-only bounded diagnostics: before/body-end/drain-end snapshots of service presence and up to eight queued callback names, with at most 4096 records and 200 characters per name, in the existing seven-day test-evidence artifact. The inspector reads the existing dispatcher through pinned-version reflection and never creates, runs, clears or aborts queued work. It records no user content, pixels, launch arguments or environment dump. The real queued-callback test first failed against an empty inspector, then passed with observation, proving observation does not execute the callback. The original cleanup test still passes. The direct-preview test additionally uses the existing dedicated-thread five-second stack observer while startup remains unacknowledged; this is diagnostic and can perturb timing, not a product fix. Fifteen-second cancellation, six-second engine stop, full raw-frame checks, audio tolerances, safety gates and product code remain unchanged.

### Local trace identified a test-context defect

The instrumented full run passed Core 255 / Media 976, 20 skipped (`/tmp/opencam-v0312-ui-trace-full.log`, 4m58s Media), but its 2930-record trace found a previously invisible defect: `FoolproofUiLogicTests.CursorEffect_ShouldInitializeWithAllOptionsAndLocalize` posted a weak property-change callback with no font/application services; `EncoderStatusTests.RecorderSelection_OverridesUiProbeWithoutChangingPreference` later changed an empty queue into weak-event/InputMarker/MediaContext.Render work while both services were absent (records 2697–2699). No later headless test consumed that final render in this particular ordering, so a green aggregate result alone would miss it.

The four plain Fact tests that explicitly mutate the shared language service (cursor labels, recorder encoder labels, settings version labels, valid settings save) now use AvaloniaFact, as the other shared-language UI tests already do. Each asserts that ProjectLayoutApp exists at its body boundary. All four assertions failed against their original Fact attributes (`/tmp/opencam-v0312-language-context-red.log`); only their execution context changed, not their existing product assertions. Pure formatters with private LocalizationService instances remain plain tests. This fixes the demonstrated missing-context mutation, not a claim that every possible CI lifecycle failure has been eliminated. The trace remains enabled for the next CI attempt.

Direct-preview diagnostic filtering also has a RED test showing the new startup observation would otherwise be discarded. Its allowlist now retains this observation and stack-tool start/end markers within the unchanged 32-event / 10000-character-per-event / 120000-character-total bounds, while still rejecting full launch arguments. No Windows delay root cause is claimed.

Final local validation after the context change and startup diagnostics: 89 related tests passed with no skips (`/tmp/opencam-v0312-language-context-green.log`); fresh complete Release solution test including build exited 0, Core 255 + Media 976 = 1231 passed, 20 skipped, zero failures, Media 5m03s (`/tmp/opencam-v0312-isolated-full.log`). Its lifecycle trace contains no Render callback observed without font services. This is local evidence, not proof of hosted Windows native behavior or elimination of every scheduling race. CI tooling 6 tests, version consistency and diff checks passed. No product source, dependencies, timeout, retry, test skip policy or security gate changed in this follow-up.
