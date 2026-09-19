# Runtime Settings Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make persisted quality, disk guard, and working-file cleanup settings control each real recording session.

**Architecture:** Extend the existing cross-process `RecordingConfiguration` contract with backward-compatible defaults, construct it from `UserSettings` in the UI, and consume it in platform FFmpeg providers and the recorder orchestrator. Keep final deletion behind the existing remux and ffprobe success gates.

**Tech Stack:** .NET 8, C#, Avalonia, FFmpeg, xUnit

**Spec:** `docs/superpowers/specs/2026-09-19-runtime-settings-wiring-design.md`

## Global Constraints

- Preserve Windows and macOS capture behavior.
- Missing or invalid new settings must use Standard quality, 2 GB warning, and 500 MB critical defaults.
- Delete MKV segments only after MP4 remux and ffprobe validation succeed.
- Keep the fixed 1 GB recording-start preflight check.
- Do not bump the version, push, or publish a release.

## Review Focus

- Unknown quality preset must not generate invalid FFmpeg arguments and must fall back to Standard.
- Hardware and software encoders must receive semantically equivalent quality choices.
- Warning threshold must remain greater than the critical threshold for UI-provided choices.
- Older IPC payloads that omit new fields must retain current safe defaults.
- Failed remux or failed probe must never delete recovery segments.

---

### Task 1: Runtime quality contract and platform arguments

**Files:**
- Modify: `src/ScreenRecorder.Core/Models/RecordingConfiguration.cs`
- Modify: `src/ScreenRecorder.Platform.Windows/WindowsFFmpegProvider.cs`
- Modify: `src/ScreenRecorder.Platform.macOS/MacOsFFmpegProvider.cs`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Test: `tests/ScreenRecorder.Core.Tests/RecordingConfigurationTests.cs`
- Test: `tests/ScreenRecorder.Media.Tests/EncoderDetectorTests.cs`
- Test: `tests/ScreenRecorder.Media.Tests/MacOsFFmpegProviderTests.cs`

**Interfaces:**
- Produces: `RecordingConfiguration.VideoQualityPreset`, `VideoQualityValue`, and `VideoBitrateKbps`.
- Consumes: Existing provider `BuildOutputArguments` contract.

- [ ] Add tests for Ultra, Standard, Compact, and unknown preset normalization.
- [ ] Run focused tests and verify they fail because the runtime quality contract is absent or arguments remain hard-coded.
- [ ] Add the minimal configuration mapping and consume it in both platform providers.
- [ ] Replace CRF-specific UI wording with encoder-neutral quality wording.
- [ ] Run focused tests and verify they pass.

### Task 2: UI-to-recorder configuration propagation

**Files:**
- Modify: `src/ScreenRecorder.Core/Models/RecordingConfiguration.cs`
- Modify: `src/ScreenRecorder.UI/ViewModels/MainViewModel.cs`
- Modify: `src/ScreenRecorder.Core/Localization/LocalizationService.cs`
- Test: `tests/ScreenRecorder.Core.Tests/RecordingConfigurationTests.cs`
- Test: `tests/ScreenRecorder.Media.Tests/FoolproofUiLogicTests.cs`

**Interfaces:**
- Consumes: Task 1's `VideoQualityPreset` contract.
- Produces: Recording configurations containing quality, disk threshold bytes, and cleanup policy; UI low-space warning status.

- [ ] Add tests proving runtime configuration contains all persisted settings and threshold conversion is correct.
- [ ] Add a test proving telemetry below the configured warning threshold displays a warning.
- [ ] Run focused tests and verify the new expectations fail.
- [ ] Extract a testable configuration builder, use it from `StartRecordingAsync`, and add localized warning behavior.
- [ ] Run focused tests and verify they pass.

### Task 3: Recorder disk guard and recovery-safe cleanup

**Files:**
- Modify: `src/ScreenRecorder.Recorder/Services/RecordingOrchestrator.cs`
- Test: `tests/ScreenRecorder.Media.Tests/ActualRecordingIntegrationTests.cs`
- Test: `tests/ScreenRecorder.Core.Tests/RecordingConfigurationTests.cs`

**Interfaces:**
- Consumes: Task 2's threshold byte values and cleanup policy in `RecordingConfiguration`.
- Produces: Per-session disk monitor thresholds while preserving the existing post-probe cleanup gate.

- [ ] Add tests for valid threshold application and invalid-value fallback.
- [ ] Add/extend an integration test proving cleanup removes segments only after successful finalization and retained mode keeps them.
- [ ] Run focused tests and verify the new expectations fail.
- [ ] Apply thresholds before starting disk monitoring without changing the 1 GB preflight check.
- [ ] Run focused tests and verify they pass.
- [ ] Run `dotnet test --no-restore -p:RollForward=Major` and both platform builds.
- [ ] Review the complete diff for compatibility and recovery safety.
