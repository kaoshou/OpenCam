# Export performance implementation plan

> **For agentic workers:** Use superpowers:executing-plans for inline execution, with test-first changes.

**Goal:** Preserve video packets when only audio is edited and expose export stage costs without weakening validation.

**Architecture:** Extend ProjectExportPlanner and the existing normalized-audio exporter. Wrap export progress for timing; retain existing software rendering as the safe path.

**Tech Stack:** .NET 8, FFmpeg/ffprobe, xUnit.

**Spec:** docs/superpowers/specs/2026-10-29-export-performance-design.md

## Global Constraints

- Do not alter recording, version, original assets or output verification.
- Do not merge or release. The subsequent user continuation authorizes a development-branch push and dual-platform test packages.
- Hardware encoding is an evaluation deliverable, not an untested default replacement.

## Review Focus

- Single clip must not hit concat's minimum-two-input contract.
- Mute, zero volume and fades must actually change decoded audio.
- Changed visual range/settings must still render.
- Unsupported/missing audio must retain the existing safe render path.
- Failure/cancellation must leave no published invalid output or modified source.

### Task 1: Audio-only edits

Files: ProjectExportStrategy.cs, ProjectFfmpegNormalizedAudioExporter.cs, ProjectExportStrategyTests.cs, new ProjectAudioOnlyExportTests.cs.

- [x] Add failing planner tests for complete single/multi clips with audio effects and safe rejection cases.
- [x] Add real-media tests checking video payload/timing preservation and decoded audio amplitude/fades, both single and concatenated clips.
- [x] Extend planner eligibility and single-clip video staging; retain verifier and cleanup.
- [x] Run planner and real exporter regression tests (48 passed after AAC correction).

### Task 2: Measure and evaluate

Files: new ProjectExportTimingProgress.cs, ProjectFfmpegExporter.cs, export timing tests and performance report.

- [x] Test phase forwarding and timing emission through the actual exporter before adding timing wrapper. Detailed completed=true/false assertion is deferred below.
- [x] Log phase duration and overall export duration, including incomplete runs.
- [x] Benchmark new versus forced-render path on identical media, recording environment and raw timings (final medians 2341.5140 ms versus 595.6679 ms, including verification).
- [x] Evaluate hardware API availability/quality mapping and document unverified platforms (Mac hardware-only probe succeeded; quality and Windows remain unverified).
- [x] Run full solution tests, NativeOnly build and review diff (final Core 374 pass; Media 1465 pass / 23 skipped / 0 failed; NativeOnly build passed).

## Progress / rulings

- Existing unrelated settings/tray/picker edits are preserved in the working tree.
- Ruling: preserve the current CPU renderer; hardware selection will not ship without platform-specific tests and quality mapping. Risk avoided: silently different quality or broken output on untested drivers.
- Task 1 RED: 6 planner cases failed the whole-clip eligibility gate; 6 real-media cases reported Rendering rather than ConvertingAudio. GREEN: all passed with preserved packet payloads and verified decoded audio.
- Task 2 RED: real exports lacked phase/total logs. GREEN: timing wrapper preserves existing phase sequences and logs phase/overall wall time.
- Final review: independent read-only reviewer found no Critical/Important correctness defects. AAC bitrate discrepancy regraded as important for preserving the previous export quality contract; failing emitted-argument test added, then normalized mux explicitly set to 192k. 48 focused tests passed afterward.
- Deferred minors: ConvertingAudio still groups copying/PCM/muxing; multi-clip decoded volume/fade assertions currently cover the first segment (packet order covers all); completion logging tests assert emission but not both true/false values. These do not weaken the production verifier.
- Validation tooling: an external benchmark project-reference restore temporarily replaced repository assets with incomplete transitive graphs; full solution restore recovered them without source/package version changes. Initial full-suite invocation did not build and is not counted as passed.
- Final verification after AAC correction: `dotnet test ScreenRecorder.sln -c Release --no-build --no-restore -m:1` completed successfully; Media 1488 total (1465 passed, 23 skipped), Core 374 passed. No merge/push/release performed. Detailed measured results and deferred minors: docs/export-performance.md.

## Follow-up authorized by user: finish validation and build test packages

- [x] Close previous minor gaps: CopyVideo/RenderAudio/Mux timers; different effects checked in both segments; structured completed=true/false assertions.
- [x] Add five-minute real-media exports at 20/30 FPS with packet hash, monotonic DTS, endpoint, complete-decode and source preservation assertions. Synthetic 64x36 tests do not replace physical recording or realistic resolution performance tests.
- [x] Full local verification: Core 374 passed; Media 1467 passed / 23 skipped / 0 failed (6m30s); packaging scripts 77 passed / 2 skipped; NativeOnly build passed. Independent follow-up review found no Important bugs.
- [x] Add export regression filters to Windows packaged-runtime, macOS portable and macOS packaged-media jobs. Test README includes export and settings checks.
- [ ] Commit and push development branch, dispatch verify-obs-core with release_package=false.
- [ ] Verify both packages and CI results, deliver links with source commit and explicit manual-validation gaps.

Ruling: hardware encoder enablement remains separate from this test-package round; it has no verified quality mapping or Windows hardware test evidence yet. No change to recording core or version.
