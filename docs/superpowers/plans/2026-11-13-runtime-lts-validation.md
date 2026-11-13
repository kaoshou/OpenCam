# Supported LTS runtime and deterministic UI validation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline. User explicitly authorized fixing the blockers and validating them, without another planning confirmation.

**Goal:** Move OpenCam 1.3.0 to supported .NET 10 LTS, resolve reproducible UI/test-lifecycle defects, then resume the approved publication plan.

**Architecture:** Pin SDK 10.0.404 and self-contained runtime 10.0.15 from verified Microsoft metadata. Retarget existing projects, probes, packaging and CI; preserve OBS source pins, features and platform boundaries. Keep real runtime notices sourced from the actual publish assets.

**Spec:** User instruction: “你想辦法幫我改到好 測試到好”; existing publication scope remains binding except approved LTS migration and verified fixes.

## Global Constraints

- Product version 1.3.0; existing three platforms only; no legacy capture fallback.
- No skipping failures, weakening source/notice checks or treating isolated passing tests as a green full suite.
- Isolated toolchain; do not alter system .NET, user settings, original media or unrelated dirty work.
- Supported runtime is 10.0.15, SDK 10.0.404; Windows CI diagnostic tool 10.0.750501, not shipped in the app.

## Review Focus

- Every native-only and normal publish must use net10.0 and the pinned supported runtime.
- Correct RID/runtime notices must be copied; wrong/mixed framework assets must fail rather than silently omit licenses.
- UI test results must not depend on a real user's locale/settings or callbacks from cleaned-up models.
- Child probe paths and diagnostic tools must match the new runtime on all platforms.
- No recording behavior or data preservation checks are weakened to obtain passing tests.

### Task 1: .NET LTS migration

**Files:** project files under src/tests; Directory.Build.props; global.json; .github/workflows; scripts/assemble-third-party-notices.mjs; runtime and notice tests.

- [ ] Add failing net10 publish-assets notice test and unsupported/mixed-framework rejection; observe RED.
- [ ] Retarget all projects/probe paths and CI, pin SDK/runtime and compatible diagnostic tool. Mechanical updates must exclude historical reports and source locks.
- [ ] Build with verified isolated SDK, run full script/website tests and inspect actual self-contained notices. Commit.

### Task 2: UI lifecycle and isolation

**Files:** MainViewModel.cs, FoolproofUiLogicTests.cs, focused new lifecycle tests.

- [ ] Reproduce language callback activity after Cleanup and document locale/order-dependent failures before changing code.
- [ ] Use named, removable language handler; preserve active-model notifications. Isolate logic tests from real settings, restore locale and always clean up models. Keep original functional assertions and add explicit locale cases.
- [ ] Rerun original failed classes, then complete managed suite. Investigate remaining failures independently, not by raising timeouts or removing assertions. Commit verified fixes.

### Task 3: Whole-branch verification and resume publication

- [ ] Correct packaged Linux README lifecycle guidance, retain dependency compatibility.
- [ ] Run build, complete managed/scripts/web-player/website suites, vulnerability and source checks; retain failures/skips.
- [ ] One fresh whole-branch review of this extension; one verified important-fix pass, no re-review.
- [ ] Resume Task 3/4 of docs/superpowers/plans/2026-11-13-v130-publication.md. CI must build actual supported-runtime packages before publication; keep prior immutable release/backup guarantees.
