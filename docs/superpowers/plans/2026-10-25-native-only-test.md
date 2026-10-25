# Native-only Windows Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline; user waived further questions. One fresh final safety review.

**Goal:** Produce a fail-closed Windows test package with no legacy screen recorder and actionable native initialization diagnostics.

**Architecture:** Compile-time test profile physically excludes FFmpegScreenRecorderEngine; strict native routing and existing owned-output/cleanup guards remain. Optional bounded protocol fields preserve startup facts without leaking device identities. Independent Windows CI assembles and verifies the portable ZIP.

**Tech Stack:** .NET8, C++17/libobs33, Node22, existing Windows CI.

**Spec:** docs/superpowers/specs/2026-10-25-native-only-test-design.md

## Global Constraints

- Version0.4.1; no master/tag/Release writes or unrelated staging.
- Normal builds/macOS behavior unchanged. Preserve FFmpeg for audio-only preflight/editing/export.
- No increased deadlines, skipped safety assertions, fake media or guessed physical root cause.

## Review Focus

- Missing/tampered runtime must not instantiate a legacy recorder.
- Stale manual settings and pinned old-core sessions must not bypass strict selection.
- No ready/media acknowledgement must be mistaken for a running recording.
- Error strings/diagnostic fields must not expose arbitrary helper-provided text.
- Native-only and normal builds must not overwrite one another's output/intermediate files.

### Task 1: Strict profile and routing

**Files:** Directory.Build.props; Core/Models/RecordingBuildProfile.cs; Media csproj/router/engine; Recorder factory/preflight factory/orchestrator; UI settings/main VM; tests/ScreenRecorder.NativeOnlyProbe; related Media tests.

**Interfaces:** `RecordingBuildProfile.NativeOnlyTest`; router optional `requireNative`; engine optional `allowCompatibilityFallback` (capture fallback only).

- [ ] Add failing strict-routing/startup tests and a runnable native-only profile probe asserting legacy type absence and missing-runtime fail-closed behavior.
- [ ] Run RED, implement build exclusion/isolated outputs and strict factory/router/orchestrator enforcement; preserve same-core encoder fallback.
- [ ] Normalize native-only settings and test UI choices; run focused/default regressions and profile probe GREEN.

### Task 2: Native readiness diagnostics

**Files:** native/obs-capture/main.cpp; optional focused readiness header; ObsScreenRecorderEngine.cs; native integration and managed subprocess tests.

**Interfaces:** Optional `startup` object on existing ready/error events, optional numeric renderer observations on progress. Protocol stays1.

- [ ] Write RED tests proving error-code preservation, privacy/range validation and failed-source readiness facts.
- [ ] Implement fixed-phase numeric/bool snapshots without relaxing actual startup gates; log normalized objects only.
- [ ] Build native helper; generated-media/sync/failure/kill-recovery and managed tests GREEN.

### Task 3: Native-only package

**Files:** verify-obs-core.yml; prepare-obs-user-test-package.mjs/tests; Windows test instructions and verification report.

- [ ] Make package require native-only build evidence, omit old capture helper and assert physically absent legacy managed type using the profile probe.
- [ ] Fresh final review and one evidence-backed correction pass; complete normal regressions/build and native-only probe.
- [ ] Push immutable dev snapshot, verify Windows CI and packaged real-media tests; download raw Actions ZIP and match API digest, fully unpack/check PE/files/notices; deliver local ZIP. Record physical acceptance as pending.
