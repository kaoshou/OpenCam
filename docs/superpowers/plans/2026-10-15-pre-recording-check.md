# Recording Preflight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Implement inline; the user requested minimal confirmation/review and no additional review agents.

**Goal:** Check the selected screen, microphone and system sound before recording, with an optional isolated5-second trial and replay.

**Architecture:** A Recorder-owned preflight coordinator acquires a capture lease also required by formal recording. Fresh platform audio services and the existing recording engine produce actual input levels and an in-memory streaming preview without writing a test file. Only explicit trial recording creates a private MKV; playback and all checking resources stop before formal start.

**Tech Stack:** Existing .NET8, Avalonia, authenticated IPC, platform capture adapters, FFmpeg and project-media playback components. No new package.

**Spec:** `docs/superpowers/specs/2026-10-15-pre-recording-check-design.md`

## Global Constraints

- Do not change VERSION, project schema, source recordings or existing export/recovery policies. No push/release in this implementation task.
- Preflight never creates a formal session, clip or project revision. Capture has one authoritative Recorder-side owner.
- Checking stores no audio/video file. Explicit trial records5 seconds after usable startup, never starting automatically merely because the window opens.
- Samples older than1 second, future-dated or nonfinite are unavailable, not silent or passed. Separate microphone/system sources; disabled inputs need no sound.
- Black pixels do not prove failure. Missing permission, invalid bounds, disappearance or an unreadable enabled input must not silently pass.
- Fresh resource instances per preflight; no sharing active native capture or altering formal RecordingOrchestrator state.
- Close/cancel/disconnect and formal-start transitions stop and join owned processes and source services before releasing the lease. Never force a second capture after an unconfirmed stop.
- Replay stops monitoring before producing sound. Clean only private owned trial data; retain originals and report cleanup failures.
- Windows/macOS device acceptance is manual where unavailable; no fake green checks or acceptance claims.

## Review Focus

1. A timed-out start later produces callbacks: it must not publish frames or start a second engine. Task2 tests operation generation and joined cancellation.
2. Hotkey/double-click/UI disconnect while a check owns capture: formal recording must not race or silently proceed. Tasks1/4 test Recorder-side ownership, not just button disabling.
3. A changed default endpoint or moved display reuses an apparently identical selection: confirmation must be invalidated. Tasks2/5 test resolved source identity and bounds snapshots.
4. A silent microphone with loud system playback: each card must reflect its own input, and replay must not validate the system card. Tasks2/3 test independent levels and replay suppression.
5. Cancel during finalization or a planted file/link in the trial directory: cleanup must not touch unrelated data. Task3 tests owned-path/identity cleanup and failed-stop blocking.

### Task 1: Capture lease and readiness rules

**Files:** Create `src/ScreenRecorder.Core/Preflight/RecordingPreflight.cs`, `src/ScreenRecorder.Recorder/Services/RecordingCaptureLease.cs`; test `tests/ScreenRecorder.Core.Tests/RecordingPreflightTests.cs` and `tests/ScreenRecorder.Media.Tests/PreflightLeaseTests.cs`.

**Interfaces:** Core produces `PreflightPhase`, `PreflightSourceState`, `PreflightStatus` (operation/configuration IDs, separate input states, resolved display/bounds and warning/error codes). `PreflightReadiness.Resolve(bool enabled, AudioLevelSample? sample, DateTimeOffset now)` is independent of the formal recording-state resolver. `RecordingCaptureLease.AcquireAsync(Guid owner, CancellationToken ct)` returns an async-disposable ownership token; concurrent different owners fail without waiting behind active capture.

- [ ] Write tests for disabled, missing, silent, live, stale/future/nonfinite readings and lease acquisition/release/failure. Mutating a missing reading to Silent or allowing a second owner must fail.
- [ ] Run the two named test classes before implementation. Expected: missing-contract failure; do not accept a setup/socket failure as RED.
- [ ] Implement only these models/readiness and exclusive ownership; failed cleanup retains ownership until confirmed shutdown.
- [ ] Run the two classes; expected0 failures. Save evidence and commit task changes locally.

### Task 2: Independent streaming check backend

**Files:** Create `src/ScreenRecorder.Recorder/Services/PreflightCaptureFactory.cs`, `PreflightLiveSession.cs`, `PreflightCoordinator.cs`; create `src/ScreenRecorder.Media/Preflight/PreflightPreviewPipe.cs`; test `tests/ScreenRecorder.Media.Tests/PreflightLiveSessionTests.cs`.

**Interfaces:** `IPreflightLiveSession` exposes `Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct)`, `PreflightStatus ReadStatus(DateTimeOffset now)`, `ProjectFrameReply? ReadFrame()` and joined `Task StopAsync(CancellationToken ct)`, plus async disposal. `PreflightCoordinator.BeginAsync(Guid operation, RecordingConfiguration configuration, CancellationToken ct)` produces a token-scoped `PreflightStatus`; poll/stop reject stale operation IDs. Task1 owns readiness/lease contracts. Freeze configuration by deep copying the caller's mutable configuration before asynchronous work. New frames carry the operation ID and monotonically increasing configuration revision and are discarded after invalidation.

- [ ] Add failing tests for independent audio reads, stale callbacks, no files created while checking, capture identity mismatch and failed stop retaining the lease. Exercise real coordinator behavior with controlled native boundaries, not assertions that a fake was called.
- [ ] Confirm RED for each behavior before implementing it.
- [ ] Factory creates a fresh engine and fresh OS audio adapters, not the singleton formal adapters. Use `IBoundRecordingOutput.OutputStream` to send checking MKV bytes into a bounded streaming preview decoder, never a disk file. Keep only the latest decoded BGRA frame, at most960x540,4 updates/second; drain stderr and all PCM pipes continuously. If this cannot preserve the selected formal capture mode, report unsupported rather than selecting another mode.
- [ ] Cap start/check initialization at40 seconds and stop at10 seconds, using cancellation plus process/source join; failure to confirm exit blocks handoff. Snapshot the resolved device/display identities as well as requested configuration.
- [ ] Run coordinator tests and synthetic streaming integration: real FFmpeg output produces bounded frames while no checking file is created; cancellation joins both encoder/decoder. Native capture/permissions remain manual unless explicitly enabled.
- [ ] Expected0 failures; save evidence and commit locally.

### Task 3: Isolated trial and replay

**Files:** Create `src/ScreenRecorder.Recorder/Services/PreflightTrialSession.cs`, `PreflightTrialPlayback.cs`, `PreflightTrialStorage.cs`; test `tests/ScreenRecorder.Media.Tests/PreflightTrialTests.cs`. Reuse existing closed media-probe/preview jobs and platform audio output, not a project store.

**Interfaces:** `PreflightCoordinator.RecordTrialAsync(Guid operation, CancellationToken ct)` stops live checking, records through a fresh session, validates and enters Reviewing. `PlayTrialAsync`, `StopReplayAsync` and `StopAsync` remain operation scoped; no caller-supplied filesystem paths. Playback returns bounded frames to the same frame channel. Trial storage returns only a bound owned file, never a formal ProjectHandle.

- [ ] Write failing tests for5 seconds counted from startup, early cancellation, no formal project/session mutations, audio-track presence not implying audible input, replay suppression, tampered temporary paths and cleanup failures.
- [ ] Verify RED, then implement private random trial directories under application temporary storage with ownership checks, no symlink traversal and no formal recovery registration. Close checking before trial; close trial writing before probing/replay. Reuse the captured settings, including encoder/capture selection, with a new configuration ID if fallback changes them.
- [ ] Stream a closed trial through existing file-handle-bound video/audio decoding into the preview and `IProjectAudioOutput`; never launch an external player that outlives cleanup. Stop monitoring before replay. Retrying stops replay and invalidates previous signal results.
- [ ] Run focused unit tests and real FFmpeg synthetic trial/replay tests: valid frame/audio output, sample/frame bounds, deterministic cancellation, no formal data changes, no leftover owned processes.
- [ ] Expected0 failures; commit locally. Do not claim native device acceptance from synthetic media.

### Task 4: Recorder IPC and formal-start exclusion

**Files:** Modify `src/ScreenRecorder.Recorder/Program.cs`, `src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs` and `src/ScreenRecorder.Infrastructure/IPC/NamedPipeIpcClient.cs`; create `src/ScreenRecorder.Infrastructure/IPC/PreflightMessages.cs` and `src/ScreenRecorder.Recorder/Services/PreflightIpcDispatcher.cs`; test `tests/ScreenRecorder.Media.Tests/PreflightIpcTests.cs`.

**Interfaces:** Commands `BeginPreflight`, `GetPreflightStatus`, `GetPreflightFrame`, `RecordPreflightTrial`, `PlayPreflightTrial`, `StopPreflightReplay`, `EndPreflight`. `PreflightRequest` carries `Guid OperationId`, `long ConfigurationRevision`, `Guid ServerInstanceId` and an optional deep-copied configuration, never an output path. Status responses include only bounded state and IDs. Frame requests return the existing `ProjectReply.Frame` media envelope with `ProjectSnapshot.Closed` carrying the server instance: `Frame.ClipId` is this operation ID and `Frame.Revision` is the configuration revision, validated by the preflight client and never submitted to project-edit commands. This reuses existing authenticated geometry/size checks without changing wire format or trusting a project ID as preflight authorization. Formal recording acquires Task1's same lease before opening any capture source and releases it only after confirmed stop.

- [ ] Write failing authenticated-dispatch tests for stale IDs, duplicate trial commands, untrusted paths, overlarge payloads, unconfirmed stop and direct formal-start attempts while preflight owns capture.
- [ ] Verify RED; implement dispatcher and lifetime wiring without changing existing control/media deadlines or trusting UI-side readiness as authority.
- [ ] Tests cover both normal and project/insertion/resume starts so none bypasses the lease. A disconnect cancels preflight and retains blocking state until shutdown is confirmed.
- [ ] Run existing IPC/auth/media size-limit and recording-start suites plus new tests. Expected0 failures; commit locally.

### Task 5: Preflight window and start integration

**Files:** Create `src/ScreenRecorder.UI/Preflight/RecordingPreflightWindow.axaml`, its code-behind and `RecordingPreflightViewModel.cs`; modify `MainViewModel.RecordingContent.cs`, `MainViewModel.cs`, `MainWindow.axaml.cs` and `LocalizationService.cs`; test `tests/ScreenRecorder.Media.Tests/RecordingPreflightUiTests.cs`.

**Interfaces:** A main-window dialog callback accepts an immutable configuration snapshot and returns Cancel or an explicitly confirmed snapshot. The dialog uses Task4 commands; formal start waits for `EndPreflight` success and checks that the configuration/display/device identities remain valid. Existing naming/unsaved-confirmation happens first, without creating a formal project until confirmation.

- [ ] Write failing UI/controller tests: cancellation leaves project unchanged; independent source cards and thumbnail; silence acknowledgement; hard errors block; all-audio-disabled clearly says silent recording; double-click cannot create two starts; configuration changes rerun checks; late replies cannot overwrite a new window.
- [ ] Verify RED; implement compact bilingual cards, visible privacy explanation,5-second trial progress, replay/stop and Cancel/Start actions with centered buttons. Start remains disabled during preparation/trial/replay/stop and until screen confirmation; silence requires a distinct acknowledgement, not automatic continuation.
- [ ] Integrate home start, append and editor insertion. Pause/resume with unchanged identities does not prompt; changed configuration forces preflight. Preserve existing naming cancellation, unsaved work, editor recording locks and automatic export policies.
- [ ] Run new UI/controller tests, existing project-start/pause/insertion and localization suites. Expected0 failures; commit locally.

### Task 6: Verification and documentation

**Files:** Update `docs/USER_GUIDE.zh-TW.md`, `docs/USER_GUIDE.en-US.md`, and add `docs/verification/2026-10-15-pre-recording-check.md` with concrete manual steps and evidence.

- [ ] Run `dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false`, player/website tests and version/diff checks. Expected0 failures; report existing warnings and all skips by scope, not as device PASS.
- [ ] With explicit device-testing authorization, check Windows/macOS microphone-only, system-only, both, silence, wrong device, denied permission, unplug/change display, repeated cancellation and immediate formal recording after replay. Otherwise mark these REQUIRES MANUAL VALIDATION; do not trigger sensitive device capture merely to obtain green results.
- [ ] Perform one concise inline safety check of capture ownership, bounded memory, cancellation and owned-file cleanup. No additional review agent, merge, version bump, push or release.
- [ ] Commit completed implementation/documentation locally; summarize what ran and what still needs physical device validation.
