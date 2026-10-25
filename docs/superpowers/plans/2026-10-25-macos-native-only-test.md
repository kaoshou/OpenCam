# macOS native-only user test package

Spec: same user-authorized native-only 0.4.1 profile as Windows, Apple Silicon, first-run 20 FPS. No version bump, merge or Release. Recording reliability takes priority; do not claim physical recording acceptance from generated media tests.

## Task 1: Normalize macOS profile capture selection

Interfaces: SettingsViewModel and MainViewModel feed NativeCaptureEnabled into the strict recording router. Only the native-only profile forces ScreenCaptureKit; normal builds retain current choices.

1. Extend NativeOnlyProbe to exercise first launch, legacy settings, save/reset, and main recording configuration on macOS. Run it and observe failure.
2. Implement profile-only normalization and localized notice. Run probe and ordinary relevant tests. Expected: success, no legacy recording choice.

## Task 2: Build and verify macOS package

Interfaces: verified runtime inventory, explicit user-test staging, app packaging and signatures must agree. Preserve runtime inventory hashes during outer-app signing.

1. Add macOS staging tests; observe failure, implement platform-specific helper validation.
2. Add explicit native-only macOS packaging mode: omit old screen helper, assemble profile-specific notices, include validated runtime and source, sign without modifying verified runtime bytes. Keep ordinary packaging unchanged.
3. Snapshot and commit only the test development branch. Build arm64 application and helper from pinned dependencies; package a fresh app with 20 FPS default.
4. Verify architecture, signatures, licenses/source, runtime inventory, missing-runtime failure, CPU and Apple generated-media tests. Run ordinary regressions.
5. Fresh final review of this macOS delta; resolve important findings with regression evidence. ZIP the verified app, re-extract and verify again, record SHA256, deliver with physical recording acceptance limitations.

## Review Focus

Check Mac old preferences cannot select removed code; Windows native-only and normal builds remain unchanged; all selected audio readiness gates remain enforced; nested signing cannot invalidate runtime hashes; executable permissions and source/license provenance survive ZIP extraction. No unsupported claim of physical ScreenCaptureKit/audio acceptance.
