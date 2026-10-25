# 1.0.0 startup and release preparation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce avoidable startup work without weakening recording safety; prepare truthful 1.0.0 UI and attribution.

**Architecture:** Preserve OBS recording and FFmpeg media processing. Initialize independent preflight audio devices concurrently, wait for both before starting the consumer, and cancel/join both before cleanup on failure. Keep runtime hashes, actual audio acknowledgement and first-frame checks.

**Tech Stack:** .NET 8, Avalonia, libobs, FFmpeg.

**Spec:** User authorization in this conversation: plan and implement conservatively, preserve normal functions, update appearance/text and licensing; version 1.0.0. No release authorized.

## Global Constraints
- No deletion of recordings or changes to media timestamps.
- Preserve all three UI languages, 20 FPS first-run default and recovery.
- Do not label unexecuted platform/hardware tests as passed.
- Retain upstream licenses; version is not a claim of completed stable-release gates.

## Review Focus
- One audio device fails while the other is still starting: cancel/join before cleanup.
- Cancellation during initialization must not leave a device running.
- A disabled source must never be initialized.
- No-audio/CPU-only recording and resumed sessions retain existing behavior.
- Runtime changes must still invalidate integrity checks; no unsafe cache.

### Task 1: Audio startup
Files: `src/ScreenRecorder.Media/Preflight/FFmpegPreflightAudioCapture.cs`, `tests/ScreenRecorder.Media.Tests/PreflightAudioCaptureTests.cs`.
- [ ] Add a gated dual-source test proving both starts enter before either completes; run RED.
- [ ] Start independent sources concurrently with linked cancellation and joined failure cleanup; retain acknowledgement.
- [ ] Add stage timings and increase audio-only progress reporting frequency to 100 ms.
- [ ] Run preflight tests and full Media regression, recording skips/failures.

### Task 2: Release presentation and attribution
Files: `VERSION`, `NOTICE.md`, `THIRD-PARTY-NOTICES.md`, localization and relevant views.
- [ ] Audit visible provisional text, layout and actual runtime notices; make targeted changes, not a redesign.
- [ ] Set 1.0.0 and document OBS/FFmpeg responsibilities and package-specific corresponding sources.
- [ ] Build UI and run localization tests; do not remove internal test identifiers or historical documentation.

### Task 3: Verification and handoff
- [ ] Review changes independently; address safety findings.
- [ ] Record exact test/build evidence and unresolved release gates; sync scoped changes to original development worktree.
- [ ] No merge, push, release or claim of faster hardware startup without measurement.

## Decisions and progress
- Completed scoped audio concurrency / 100 ms progress / stage timings, version and three-language copy / OBS notices. Full Release suite: 1,709 passed, 23 environment skips, 0 failures. Native-only build and probe passed. See `docs/verification/2026-10-25-1.0.0-startup-preparation.md` for exact evidence and pending physical acceptance.
- Compact dialog size retained after full tests caught the attempted enlargement; no broad visual redesign.
- Independent review completed; delayed-start cleanup coverage and owned-helper notice version follow-ups addressed.
- Preserve current trial stop/reopen lifecycle for this pass: shared capture would change ownership and trial boundaries.
- Preserve full runtime revalidation: hash caching without immutable file ownership would weaken safety.
- Preserve encoder probing until an OBS-native capability selection replacement is verified, especially CPU-only Windows.
