# OpenCam 0.3.8 release preparation

User authorized version +0.0.1, push and Release after testing the Windows build. This release gathers the local preflight initialization/intentional-stop fix, clearer and more compact preflight UI, countdown removal, selected-input presentation and live trial levels, editor bookmark creation/controls, and website screenshot lightbox.

The existing worktree is retained. The main checkout's pre-existing modified editor specification and five untracked plans are excluded. VERSION is canonical at 0.3.8; subtitle schema4 remains unchanged. Existing demonstration screenshots retain their actual version labels. No private desktop captures or workaround for file-browser screenshot restrictions is included.

## Verification

Environment: macOS arm64, .NET SDK8.0.425, FFmpeg7.1.2. The complete Release solution test run includes restore and build; local package auditing is disabled for cached dependencies, not claimed successful. CI retains its vulnerability gate.

- Trial live-level and actual preflight window regression: 29 passed, 0 skipped, 0 failed, `/tmp/opencam-trial-live-audio.log`. Four input combinations, source-specific current/stale/missing samples, cancellation/disposal and bilingual trial/review presentation. Real synthetic FFmpeg capture is used; test-controlled samples do not establish native microphone/system-audio acceptance.
- Website: 39 passed, 0 failed; website build passed. `/tmp/opencam-v038-website.log`, `/tmp/opencam-v038-website-build.log`. Includes executed lightbox behavior and guide/home links.
- Web player: 23 passed, 0 failed, `/tmp/opencam-v038-player.log`.
- Full Release restore/build/tests: Core248 and Media821 passed, Media20 skipped, zero failures; `/tmp/opencam-v038-full.log`, exit0. A quiet interval in the media tests was checked before completion; the process completed normally in 4m18s, with the final summary present. This is not native Windows acceptance.
- Version consistency and diff checks passed. Initial use of the unsupported `dotnet test --ignore-failed-sources` option failed before testing; the actual successful command uses `-p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false`. No test was skipped to obtain the result.

## Publication procedure

Only publish the exact successful master Actions run: portable/Windows/macOS verification and Windows/macOS builds must all succeed, automatic publication skipped, run and nonexpired nonempty artifact workflow SHA correct, and website deployment successful for the same SHA. Download the raw Actions ZIPs and compare API SHA256 digests before unpacking. Validate Portable ZIP contents, x64 PE installer/executables, DMG integrity/Info.plist and packaged source/license notices. Never substitute a local test package.

Recheck tag/Release absence or matching identity, create a draft with the complete same-commit bilingual notes plus build/website provenance, upload exactly three named assets, and compare API names, sizes and SHA256 digests before publishing a non-prerelease. Recheck the public Release, target tag and all assets afterward. Do not overwrite another commit or expose a Release with missing assets.

User reports that the updated Windows preflight now shows the screen. Dedicated low-latency preview is not implemented. Complete native recording/audio combinations, long A/V, macOS visuals, physical disk exhaustion, browser offline/touch/fullscreen, warning exclusion, background shortcuts/multi-display DPI, complete physical Release Gate, trusted signing/notarization and full Windows FFmpeg corresponding-source verification remain incomplete. These limits are preserved in the bilingual release notes. Historical intermittent Windows heartbeat/fast-export issues are not claimed fully resolved.
