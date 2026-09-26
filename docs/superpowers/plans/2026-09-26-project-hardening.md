# OpenCam 0.2.x Project Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make OpenCam's test gates, recorder health reporting, dependencies, packaging, versioning, and documentation match the reliability claims while explicitly excluding paid code signing.

**Architecture:** Keep the current UI/Recorder process split and safe-stop-on-parent-exit behavior. Add a focused health tracker between FFmpeg progress and IPC telemetry, make CI verification a prerequisite of packaging, and use repository-level inputs for product version and third-party binary identity.

**Tech Stack:** .NET 8, C#, xUnit, Avalonia 11, FFmpeg/ffprobe, Swift helper tests, Node.js 22, Bash/PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-26-project-hardening-design.md`

## Global Constraints

- Do not add Apple Developer ID, notarization, Windows Authenticode, or paid signing work.
- Do not add Linux product support or UI-to-orphaned-Recorder reattachment.
- Normal close remains blocked while preparing, recording, paused, stopping, or finalizing; abnormal UI exit makes Recorder safely stop and preserve data.
- Stay on Avalonia 11.x unless the patched transitive dependency cannot be resolved compatibly.
- Never mark hardware, long-duration, sleep, hot-plug, or multi-monitor validation PASS without actual saved evidence.
- Preserve the two existing untracked 2026-09-22 plan files unless the user separately decides their disposition.
- Use TDD for behavior changes and keep each commit limited to the task being verified.

## Review Focus

- A recording that has started but has not emitted its first FFmpeg progress line must report health as unknown, not healthy or failed; Task 2 tests this startup state and Task 3 tests the integration.
- A deliberately silent microphone or system-audio stream must not be mistaken for device loss merely because its level is zero; Task 2 tests fresh zero-level samples separately from missing samples.
- Pause/resume changes the active MKV segment; the watchdog must evaluate the current segment without carrying a false stall from the previous segment; Task 2 tests segment reset behavior and Task 3 tests orchestration.
- A checksum-correct Windows FFmpeg archive with a changed top-level directory must still fail with a clear packaging error rather than silently omit binaries; Task 5 tests archive layout validation.
- Product version text containing trailing spaces, more than one final newline, or invalid SemVer must fail the consistency check before packaging; Task 6 tests these cases while allowing exactly one final newline.

---

### Task 1: Restore Test Coverage and Modernize the Test Toolchain

**Files:**
- Modify: `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs:351-464`
- Modify: `tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj`
- Modify: `tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj`
- Modify: `src/ScreenRecorder.UI/ScreenRecorder.UI.csproj`
- Test: `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs`

**Interfaces:**
- Consumes: existing `MainViewModel` public/internal test surface.
- Produces: six discoverable `[Fact]` tests; patched `Tmds.DBus.Protocol` resolution; current test host packages.

- [ ] **Step 1: Re-enable the six disabled tests and verify discovery fails or the assertions expose current behavior**

Restore `[Fact]` on `CustomRegion_CanConfigureOnlyWhenSelectedAndNotRecording`, `MonitorSelection_CanSelectOnlyWhenOptionCheckedAndNotRecording`, `MicrophoneSelection_CanSelectOnlyWhenEnabledAndNotRecording`, `UpdateCustomRegion_ShouldSanitizeDimensionsToEvenAndAutoSelectRegion`, `CursorEffect_ShouldInitializeWithAllOptionsAndLocalize`, and `QueryTelemetry_OnConsecutiveFailures_ShouldStopAndPromptRecovery`.

Run: `DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~FoolproofUiLogicTests'`

Expected: all six are discovered; record any assertion failure before changing product code.

- [ ] **Step 2: Make only the minimum product correction required by a failing restored test**

Do not weaken assertions. If all restored tests already pass, make no product-code change for this step.

- [ ] **Step 3: Update test packages and constrain the vulnerable transitive dependency**

Use `Microsoft.NET.Test.Sdk 18.10.1`, `xunit 2.9.3`, `xunit.runner.visualstudio 4.0.0`, and `coverlet.collector 10.0.1` in both test projects. Add a direct `Tmds.DBus.Protocol 0.21.3` reference to `ScreenRecorder.UI.csproj` so Avalonia 11.2.5 resolves the patched compatible line.

- [ ] **Step 4: Verify test discovery, execution, build warnings, and NuGet advisories**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet restore ScreenRecorder.sln
DOTNET_ROLL_FORWARD=Major dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false
DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-build --no-restore -m:1 /nodeReuse:false
DOTNET_ROLL_FORWARD=Major dotnet list ScreenRecorder.sln package --vulnerable --include-transitive
```

Expected: no `xUnit1013`; Core and Media tests have zero failures; the six restored tests appear; no High/Critical advisory remains. If runner 4.0.0 is incompatible with xUnit 2.9.3, use the newest compatible 3.x runner reported by NuGet and document that deviation in the commit.

- [ ] **Step 5: Perform the required security-boundary investigation and candidate review**

Before the package edit, use the security-fix workflow's independent read-only investigation to confirm the dependency path and supported-platform impact. After the candidate patch, use one independent read-only bypass/regression review, then rerun the Step 4 commands.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.UI/ScreenRecorder.UI.csproj tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs
git commit -m "test: restore coverage and patch test dependencies"
```

### Task 2: Add a Testable Recorder Health Tracker

**Files:**
- Create: `src/ScreenRecorder.Recorder/Services/RecorderHealthTracker.cs`
- Modify: `src/ScreenRecorder.Core/Models/RecorderTelemetry.cs`
- Modify: `src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs`
- Test: `tests/ScreenRecorder.Media.Tests/RecorderHealthTrackerTests.cs`

**Interfaces:**
- Consumes: `AudioSourceType`, `AudioLevelsSnapshot`, engine `IsRunning`, `CurrentFramesRecorded`, `CurrentRecordedTime`, and current segment file size.
- Produces: `RecorderHealthTracker.Reset(RecordingConfiguration)`, `BeginSegment()`, `ObserveProgress(RecorderProgressObservation)`, `RecordEngineError(string)`, `RecordAudioDeviceLost()`, and `CreateTelemetryHealth(AudioLevelsSnapshot)`; `RecorderTelemetry.DroppedFrames` and health booleans become nullable; `RecorderTelemetry.HealthWarning` is added.

- [ ] **Step 1: Write failing tracker tests**

Create tests named:

- `BeforeFirstProgress_HealthIsUnknown`
- `RunningProgress_HealthBecomesTrue`
- `ThreeUnchangedObservations_MarksVideoAndEncoderUnhealthy`
- `BeginSegment_ClearsPreviousStallHistory`
- `EngineExitOrError_MarksEncoderUnhealthyAndPreservesMessage`
- `FreshSilentAudioSample_RemainsHealthy`
- `SelectedAudioWithoutFreshSample_IsUnavailable`
- `UnselectedAudioSource_HealthIsNull`

Assert nullable values explicitly: `null` before confirmation or when unselected, `true` after valid progress/fresh samples, and `false` after confirmed stall/error/unavailable samples.

- [ ] **Step 2: Run the new tests and verify they fail because the tracker types do not exist**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~RecorderHealthTrackerTests'`

Expected: compile failure for missing `RecorderHealthTracker` or its records.

- [ ] **Step 3: Implement the minimal tracker and nullable telemetry contract**

Define immutable `RecorderProgressObservation(bool EngineRunning, long Frames, TimeSpan RecordedTime, long FileSizeBytes)` and `RecorderTelemetryHealth(long? DroppedFrames, bool? VideoHealthy, bool? SystemAudioHealthy, bool? MicrophoneHealthy, bool? EncoderHealthy, string? Warning)` in `RecorderHealthTracker.cs`. Count consecutive identical positive file sizes only while engine state is Recording; mark a stall on the third observation. `BeginSegment()` resets size/progress history without clearing an existing fatal engine error.

Change `RecorderTelemetry` health fields and `DroppedFrames` to nullable and add `HealthWarning`. Keep JSON property names unchanged for existing fields.

- [ ] **Step 4: Expose FFmpeg progress without inventing dropped frames**

Keep `CurrentFramesRecorded` and `CurrentRecordedTime` as the progress source. Do not derive dropped frames unless FFmpeg exposes a parsed counter; return `null` for unknown. Replace empty audio-level catches in `ReadInputLevels` with structured Debug/Warning logs while preserving best-effort waveform behavior.

- [ ] **Step 5: Run focused tests**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~RecorderHealthTrackerTests|FullyQualifiedName~AudioLevelsLifecycleTests'`

Expected: zero failures.

- [ ] **Step 6: Commit**

```bash
git add src/ScreenRecorder.Core/Models/RecorderTelemetry.cs src/ScreenRecorder.Media/Capture/FFmpegScreenRecorderEngine.cs src/ScreenRecorder.Recorder/Services/RecorderHealthTracker.cs tests/ScreenRecorder.Media.Tests/RecorderHealthTrackerTests.cs
git commit -m "feat: track real recorder health"
```

### Task 3: Wire Health State Through Watchdog, IPC, and UI

**Files:**
- Modify: `src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs`
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Test: `tests/ScreenRecorder.Media.Tests/ActualRecordingIntegrationTests.cs`
- Test: `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs`

**Interfaces:**
- Consumes: Task 2's `RecorderHealthTracker` and nullable `RecorderTelemetry` fields.
- Produces: `MainViewModel.ResolveRecorderHealthStatus(RecorderTelemetry, string currentStatus) -> string`; telemetry whose values change with engine/watchdog/audio state.

- [ ] **Step 1: Write failing integration and UI-policy tests**

Add:

- `SyntheticRecording_TelemetryTransitionsFromUnknownToHealthy`
- `PausedRecording_TelemetryDoesNotReportFalseStall`
- `HealthWarning_IsIncludedInTelemetryAfterConfirmedStall` using an internal deterministic observation hook rather than a six-second sleep.
- `ResolveRecorderHealthStatus_UsesWarningWithoutOverwritingDiskWarning`
- `ResolveRecorderHealthStatus_LeavesHealthyRecordingStatusUnchanged`

- [ ] **Step 2: Run focused tests and confirm failure against fixed telemetry constants**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'Name~Telemetry|Name~ResolveRecorderHealthStatus'`

Expected: new assertions fail because current telemetry always reports true/zero and has no warning.

- [ ] **Step 3: Wire lifecycle and progress observations**

Reset the tracker on `StartRecordingCoreAsync`, call `BeginSegment()` on resume/audio-recovery segment creation, record engine errors/device loss, and update it from the watchdog using the current `session.WorkingFilePath`. `GetTelemetry()` combines tracker output with `GetAudioLevels(DateTimeOffset.UtcNow)` and never substitutes healthy constants.

- [ ] **Step 4: Surface health warnings in UI without disturbing disk warnings or waveform polling**

Add localized `StatusRecorderHealthWarning` strings. In `QueryTelemetryAsync`, keep disk-critical/warning status highest priority; otherwise show `HealthWarning` when any confirmed health field is false, and restore Recording/Paused text after recovery.

- [ ] **Step 5: Replace health-path silent catches with structured logging**

Only touch catches reached by telemetry, engine-state persistence, and watchdog file observation. Preserve best-effort cleanup behavior but log exception context.

- [ ] **Step 6: Run focused and owning-package tests**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj -c Release --filter 'Name~Telemetry|Name~AudioLevels|Name~SessionHeartbeat|Name~FoolproofUiLogic'
DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release
```

Expected: zero failures; hardware-only cases may skip with their existing explicit reason.

- [ ] **Step 7: Commit**

```bash
git add src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs src/ScreenRecorder.UI/ViewModels/MainViewModel.cs src/ScreenRecorder.Core/Localization/LocalizationService.cs tests/ScreenRecorder.Media.Tests/ActualRecordingIntegrationTests.cs tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs
git commit -m "feat: expose recorder health warnings"
```

### Task 4: Make macOS Icon Generation Work on macOS 15 and 26

**Files:**
- Create: `scripts/build-macos-icon.swift`
- Modify: `scripts/build-macos-icon.sh`
- Modify: `website/tests/icon-assets.test.mjs`
- Test: `website/tests/icon-assets.test.mjs`

**Interfaces:**
- Consumes: one 1024×1024 RGBA PNG and output `.icns` path.
- Produces: `scripts/build-macos-icon.sh <source.png> <output.icns>` with the existing CLI contract; Swift helper generates normalized RGBA iconset PNGs.

- [ ] **Step 1: Strengthen the failing macOS test**

Before invoking the script, assert macOS-only execution. After success, assert the `icns` magic, then use `iconutil -c iconset` on the output and verify all required logical sizes are extractable. Keep the test skipped on non-macOS hosts.

- [ ] **Step 2: Run on macOS 26 and confirm the existing `Invalid Iconset` failure**

Run: `node --test --test-name-pattern='macOS icon builder' website/tests/icon-assets.test.mjs`

Expected before fix: FAIL with `Invalid Iconset`.

- [ ] **Step 3: Normalize resized PNGs with an AppKit Swift helper**

Implement `build-macos-icon.swift <source.png> <output.iconset>` using `NSBitmapImageRep` with 8-bit, four-sample, non-planar device RGB output for all ten standard filenames and dimensions. The shell script compiles/runs the helper in its private temporary directory, validates every generated dimension with `sips`, and calls `iconutil`.

If normalized PNGs remain rejected on macOS 26, use `xcrun actool` as the shell-script fallback with a generated temporary `AppIcon.appiconset`, copying the resulting `AppIcon.icns` to the requested output. Preserve `iconutil` validation on macOS 15.

- [ ] **Step 4: Run icon and bundle verification tests**

Run:

```bash
node --test website/tests/icon-assets.test.mjs
bash scripts/build-macos-icon.sh src/ScreenRecorder.UI/Assets/app_icon.png /tmp/OpenCam-hardening.icns
file /tmp/OpenCam-hardening.icns
```

Expected: website icon tests pass and output reports Apple Icon Image format.

- [ ] **Step 5: Commit**

```bash
git add scripts/build-macos-icon.swift scripts/build-macos-icon.sh website/tests/icon-assets.test.mjs
git commit -m "fix: support macOS 26 icon packaging"
```

### Task 5: Add CI Verification Gates and Pin Windows FFmpeg

**Files:**
- Create: `scripts/check-nuget-vulnerabilities.mjs`
- Modify: `.github/workflows/build-and-release.yml`
- Modify: `website/tests/workflow.test.mjs`

**Interfaces:**
- Consumes: solution/tests from Tasks 1-4; GitHub-hosted Ubuntu, Windows, and macOS runners.
- Produces: verification jobs required by platform packaging; immutable Windows FFmpeg asset `autobuild-2026-09-25-15-37/ffmpeg-n8.1.3-win64-gpl-8.1.zip` with SHA-256 `8efaa4e62db01a71580dc5a7ec0625dea7a4dfc5fac2d680f94804503d18a34c`.

- [ ] **Step 1: Write failing workflow structure tests**

Assert that the workflow contains:

- a .NET test command;
- website tests on Linux and macOS;
- `tests/native/OpenCamSystemAudioTests.sh` on macOS;
- a NuGet vulnerability check;
- `needs` from both package jobs to verification jobs;
- job-scoped `contents: write` only for release publication;
- no BtbN `/latest/` URL;
- the exact pinned URL and SHA-256 above;
- a PowerShell `Get-FileHash` equality check before extraction;
- an explicit assertion that the expected extracted `bin/ffmpeg.exe` and `bin/ffprobe.exe` paths exist.

- [ ] **Step 2: Run the workflow test and verify it fails**

Run: `node --test website/tests/workflow.test.mjs`

Expected: FAIL for missing test gates and mutable FFmpeg URL.

- [ ] **Step 3: Implement cross-platform vulnerability checking**

`scripts/check-nuget-vulnerabilities.mjs` runs `dotnet list <project-or-solution> package --vulnerable --include-transitive --format json`, recursively collects vulnerabilities, prints package/severity/advisory, and exits nonzero on High or Critical. Add a script self-test mode with fixture JSON so CI parsing behavior is tested without depending on a live advisory response.

- [ ] **Step 4: Add verification jobs and package dependencies**

Use least privilege at workflow level (`contents: read`) and grant `contents: write` only to `publish-release`. Run portable .NET tests on Ubuntu, website/icon/native helper tests on macOS, and the Windows-compatible suite on Windows. Keep hardware opt-in tests skipped rather than fabricating devices.

- [ ] **Step 5: Pin and validate the Windows FFmpeg archive**

Download the exact Task 5 asset, compare `Get-FileHash -Algorithm SHA256` case-insensitively to the fixed digest, extract, assert the expected versioned directory and two binaries, then copy them. A mismatch or layout change must terminate the job.

- [ ] **Step 6: Run workflow/site tests and the vulnerability parser self-test**

Run:

```bash
node --test website/tests/workflow.test.mjs
node scripts/check-nuget-vulnerabilities.mjs --self-test
npm test --prefix website
```

Expected: zero failures.

- [ ] **Step 7: Commit**

```bash
git add .github/workflows/build-and-release.yml scripts/check-nuget-vulnerabilities.mjs website/tests/workflow.test.mjs
git commit -m "ci: gate releases and pin Windows FFmpeg"
```

### Task 6: Establish a Single Product Version Source

**Files:**
- Create: `VERSION`
- Create: `Directory.Build.props`
- Create: `src/ScreenRecorder.Core/ProductInfo.cs`
- Create: `scripts/check-version-consistency.mjs`
- Modify: `src/ScreenRecorder.UI/ScreenRecorder.UI.csproj`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`
- Modify: `src/ScreenRecorder.UI/Views/SettingsWindow.axaml`
- Modify: `installer/OpenCam.iss`
- Modify: `installer/build_installer.ps1`
- Modify: `scripts/package-macos.sh`
- Modify: `.github/workflows/build-and-release.yml`
- Modify: `website/src/content.mjs`
- Modify: `website/tests/release-notices.test.mjs`
- Test: `tests/ScreenRecorder.Core.Tests/ProductInfoTests.cs`
- Test: `website/tests/release-notices.test.mjs`

**Interfaces:**
- Consumes: root `VERSION` containing exactly `0.2.1` plus one newline.
- Produces: `ProductInfo.Version`, `ProductInfo.DisplayVersion`, and `scripts/check-version-consistency.mjs`; packaging receives version from `VERSION` rather than literals.

- [ ] **Step 1: Write failing version tests**

Add tests that assert:

- `ProductInfo.Version == "0.2.1"` and `DisplayVersion == "v0.2.1"`;
- `VERSION` matches strict `^[0-9]+\.[0-9]+\.[0-9]+\n$`;
- the checker rejects fixture values with trailing spaces, extra blank lines, or nonnumeric components;
- rendered zh-TW/en-US content uses the same version;
- Inno, macOS package, SOURCE notice, assembly, and workflow do not contain independent product-version literals.

- [ ] **Step 2: Run version tests and confirm failure because no canonical source exists**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~ProductInfoTests' && node --test website/tests/release-notices.test.mjs`

Expected: compile/file assertion failure before implementation.

- [ ] **Step 3: Add canonical version and MSBuild integration**

Create `VERSION` with `0.2.1`. `Directory.Build.props` reads and trims it into `OpenCamVersion`, sets `Version`, `AssemblyVersion` to `0.2.1.0`, and `FileVersion` to `0.2.1.0`. Remove the UI-local `<Version>`.

`ProductInfo` derives its public values from assembly informational version and strips build metadata. Replace hard-coded localized version labels with ViewModel properties backed by `ProductInfo`.

- [ ] **Step 4: Feed the canonical value into packaging and website rendering**

PowerShell and Bash read `VERSION` and reject invalid content. Pass `/DMyAppVersion=<value>` to Inno; make `OpenCam.iss` require the define. Make `package-macos.sh` use the value for plist and SOURCE notice. Website rendering reads the canonical version instead of literal `0.2.1` strings.

- [ ] **Step 5: Implement and run consistency validation**

Run:

```bash
node scripts/check-version-consistency.mjs
DOTNET_ROLL_FORWARD=Major dotnet build ScreenRecorder.sln -c Release
DOTNET_ROLL_FORWARD=Major dotnet test tests/ScreenRecorder.Core.Tests/ScreenRecorder.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~ProductInfoTests'
npm test --prefix website
```

Expected: zero failures and displayed/package version `0.2.1` everywhere.

- [ ] **Step 6: Commit**

Stage only the files listed in this task and commit:

```bash
git commit -m "build: centralize OpenCam version metadata"
```

### Task 7: Align Architecture, Acceptance, and User Documentation

**Files:**
- Modify: `ARCHITECTURE.md`
- Modify: `ROADMAP.md`
- Modify: `TESTING.md`
- Modify: `RELIABILITY.md`
- Modify: `ACCEPTANCE_REPORT.md`
- Modify: `MANUAL_TEST_CHECKLIST.md`
- Modify: `docs/USER_GUIDE.zh-TW.md`
- Modify: `docs/USER_GUIDE.en-US.md`
- Modify: `README.md`
- Modify: `website/tests/guide.test.mjs`

**Interfaces:**
- Consumes: verified behavior and command results from Tasks 1-6.
- Produces: evidence-based bilingual documentation and a repeatable manual validation record format.

- [ ] **Step 1: Add failing documentation integrity tests**

Assert that generated guides describe abnormal UI exit as safe-stop, identify hardware cases as manual validation, use the canonical current version, and do not contain the stale claims `45 項全數 PASS`, `0 warnings`, or “Recorder continues recording after the UI disappears.”

- [ ] **Step 2: Run documentation tests and confirm stale claims fail**

Run: `node --test website/tests/guide.test.mjs website/tests/release-notices.test.mjs`

Expected: FAIL on current stale documentation.

- [ ] **Step 3: Update technical truth and roadmap status**

Document safe stop on parent exit, real/nullable health telemetry, CI gates, pinned FFmpeg, and recovery behavior. Mark only automated items backed by the final verification commands complete. Leave 2/4/8-hour, sleep/resume, hot-plug, physical multi-monitor, Windows DPI, and real-device audio cases unverified until evidence exists.

- [ ] **Step 4: Replace the acceptance report with dated evidence**

Record date, commit under test, macOS version, exact commands, pass/fail/skip counts, and known limitations. Do not claim Windows hardware validation from the current Mac. Add a reusable evidence block to every manual checklist case: OpenCam version, commit, date, OS/build, hardware, executor, result (`PASS`/`FAIL`/`BLOCKED`), and artifact/log paths.

- [ ] **Step 5: Run website/document tests**

Run: `npm test --prefix website && npm run build --prefix website`

Expected: zero failures and successful build.

- [ ] **Step 6: Commit**

```bash
git add ARCHITECTURE.md ROADMAP.md TESTING.md RELIABILITY.md ACCEPTANCE_REPORT.md MANUAL_TEST_CHECKLIST.md README.md docs/USER_GUIDE.zh-TW.md docs/USER_GUIDE.en-US.md website/tests/guide.test.mjs
git commit -m "docs: align reliability claims with evidence"
```

### Task 8: Final Cross-Platform Verification and Review

**Files:**
- Modify only if a verification failure proves an in-scope regression.

**Interfaces:**
- Consumes: all prior tasks.
- Produces: final evidence report; no new product interface.

- [ ] **Step 1: Run format, diff, and repository-state checks**

Run:

```bash
git diff --check
git status --short
node scripts/check-version-consistency.mjs
node scripts/check-nuget-vulnerabilities.mjs ScreenRecorder.sln
```

Expected: no whitespace errors; only intended tracked changes plus the two preserved pre-existing untracked plans; both checkers exit zero.

- [ ] **Step 2: Run clean restore, Release build, and full .NET tests**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet restore ScreenRecorder.sln --force-evaluate
DOTNET_ROLL_FORWARD=Major dotnet build ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false
DOTNET_ROLL_FORWARD=Major dotnet test ScreenRecorder.sln -c Release --no-build --no-restore -m:1 /nodeReuse:false
```

Expected: build exit 0; zero `xUnit1013`; zero test failures; report exact pass/skip totals.

- [ ] **Step 3: Run native, website, icon, and bundle-script verification**

Run:

```bash
bash tests/native/OpenCamSystemAudioTests.sh
npm ci --prefix website
npm test --prefix website
npm run build --prefix website
bash scripts/build-macos-icon.sh src/ScreenRecorder.UI/Assets/app_icon.png /tmp/OpenCam-hardening-final.icns
```

Expected: all exit zero.

- [ ] **Step 4: Run package security audit**

Run:

```bash
DOTNET_ROLL_FORWARD=Major dotnet list ScreenRecorder.sln package --vulnerable --include-transitive
npm audit --prefix website --omit=dev
```

Expected: no known vulnerable package in the solution; npm reports zero vulnerabilities.

- [ ] **Step 5: Perform one fresh whole-branch review**

Review the final diff for recorder data-loss risk, watchdog false positives, IPC compatibility, package/runtime regressions, packaging reproducibility, documentation overclaims, and accidental inclusion of the two pre-existing untracked plans. Confirm every concrete finding before changing code, then rerun the affected focused and full checks.

- [ ] **Step 6: Report remaining manual validation**

List Windows installer execution, real Windows capture/audio, physical multi-monitor/DPI, macOS microphone/system audio permissions, hot-plug, sleep/resume, and 2/4/8-hour recordings as `REQUIRES MANUAL VALIDATION` unless fresh artifacts were produced during execution.

- [ ] **Step 7: Final integration commit if verification required tracked adjustments**

```bash
git add <only verified in-scope files>
git commit -m "chore: complete project hardening verification"
```
