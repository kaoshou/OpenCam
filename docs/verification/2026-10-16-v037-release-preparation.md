# OpenCam 0.3.7 release preparation

This release integrates recording preflight, configurable countdown, project bookmarks/global bookmark shortcuts and microphone silence advisories. No new project-format revision is introduced. Subtitle projects still use schema4; retain full backups and bookmark sidecars.

## Local verification

Environment: macOS arm64, .NET SDK 8.0.425, FFmpeg 7.1.2. Complete Release solution test command: `dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false -p:UseSharedCompilation=false --blame-hang-timeout 60s --blame-hang-dump-type none`.

- Release build and full tests: Core 248 passed; Media 792 passed, 20 skipped; zero failures. `/tmp/opencam-v037-full.log`.
- Web player: 23 passed, zero failures. `/tmp/opencam-v037-player.log`.
- Website: 30 passed and build successful. `/tmp/opencam-v037-website.log`, `/tmp/opencam-v037-website-build.log`.
- Native audio helpers: `bash tests/native/OpenCamSystemAudioTests.sh` passed using isolated Swift/Clang caches. `/tmp/opencam-v037-native.log`. These are synthetic/argument tests, not actual microphone or system-audio acceptance.
- Version consistency 0.3.7 and diff check passed. Existing nullable, NU1900 network-audit and preflight constructor warnings remain; no online vulnerability-audit success is claimed locally. CI retains its separate vulnerability gate.

The author performed a scoped integration check without additional review agents, as requested. The main checkout's pre-existing modified specification and five untracked plans are excluded. Existing screenshot version labels remain unchanged; no private desktop image or bypass of file-browser restrictions is included.

## Publication checks

Use only artifacts from the successful master build for the final commit. Require all three verify jobs and both platform builds to succeed, automatic publish job skipped, same-commit website deployment successful, and nonexpired nonempty artifacts with matching workflow SHA. Validate the Portable ZIP, PE installer, DMG and packaged notices/source/version before uploading three assets to a draft. Compare uploaded names, sizes and SHA256 digests before publishing a non-prerelease and verify its tag resolves to the same commit. Do not replace an existing mismatched tag or Release.

The Windows physical recording/long-A/V, microphone behavior, warning capture exclusion per backend, background hotkeys, macOS visuals, real disk exhaustion, browser offline/touch/fullscreen, full physical Release Gate, trusted signing/notarization and Windows FFmpeg complete corresponding-source checks remain incomplete. Publication is user-authorized, not complete acceptance. Detailed manual checks are in the preflight, countdown/bookmark and microphone reminder verification reports dated 2026-10-15.

## CI trial replay test correction

First build 38769627290 at c5e8d706cc76031aadbc872d56d197501860ea28 failed on Linux and Windows only in `FiveSecondSyntheticTrialStopsCheckingAndReplaysWithoutFormalProjectFiles`, with a null frame after replay. Full logs: `/tmp/opencam-v037-portable-ci.log` (Media710 passed/73 skipped/1 failed) and `/tmp/opencam-v037-windows-ci.log` (Media736 passed/69 skipped/1 failed). macOS verification succeeded. The test unconditionally constructed `MacProjectMediaProcess`, whose streaming method explicitly throws `PlatformNotSupportedException` off macOS. Production already chooses its platform backend; it is unchanged by this correction.

The test now chooses Windows/macOS native adapters appropriately and also exercises an actual FFmpeg stdin transport, limited to the test's small synthetic MKV. Linux runs this portable transport rather than skipping replay. The transport passes the same preview job/filter arguments with only `fd:` input changed to `pipe:0`, feeds the bound stream without reopening a media path, bounds diagnostics, rejects excess output and kills/joins children on cancellation. The original five-second, reviewing/waiting state, stopped engines, frame, PCM, output stop and private-file cleanup assertions remain. The existing test deadline is unchanged. Both native and portable cases run on macOS and Windows.

The added transport first reproduced the null-frame failure with a throwing stub (`/tmp/opencam-v037-trial-portable-red.log`), then all five trial tests passed with real FFmpeg (`/tmp/opencam-v037-trial-portable-green.log`). Complete Release regression after correction: Core248 and Media793 passed, 20 skipped, zero failures (`/tmp/opencam-v037-portable-fix-full.log`). Version and diff checks passed. This is a test portability correction, not a claim that Linux native recording is supported or that Windows physical replay has been accepted.
