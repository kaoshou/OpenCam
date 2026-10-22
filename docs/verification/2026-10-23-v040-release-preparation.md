# OpenCam 0.4.0 integration and release preparation

The user explicitly requested keeping 0.4.0, merging, pushing and a Release.
This supersedes the previous test-package-only scope. Preserve the development
worktree and the main checkout's unrelated dirty spec/five untracked plans.
No schedule is to be re-enabled. No extra reviewer agents were requested.

## Release description

docs/releases/v0.4.0.md is a new full Chinese/English feature/change description.
Historical release notes are unchanged. README/current guide version references
are updated to 0.4.0; existing 0.3.5 screenshots remain accurately labeled.
The public description does not contain test-result or development-status copy.

## Verification

- Full fresh Release build/test is logged in /private/tmp/opencam-v040-release-full.log,
  exit0: Core284 + Media1092 passed (1376 total), Media20 skipped, zero failed;
  Media5m22s, SDK8.0.425 and FFmpeg7.1.2 on macOS. Four existing
  nullable/xUnit/Avalonia warnings remain. The tests include build, not just
  no-build execution. No runtime code changed after this command.
- Player/CI/performance Node tests: /private/tmp/opencam-v040-release-node.log,
  31 passed, zero failed.
- Initial website check ran after VERSION changed but before README's heading
  was updated: guide.test expected v0.4.0 and found v0.3.12. The source mismatch
  was corrected without weakening the test. Final website39 tests and build
  pass in /private/tmp/opencam-v040-release-website-final{,-build}.log.
- Native helper compilation, argument rejection, hardware encoding of generated
  pixels, timestamp-gap preservation, AAC mux and full decoding pass in
  /private/tmp/opencam-v040-release-native.log. No private desktop/audio captured.
- Version consistency0.4.0, shell syntax and git diff --check pass.
- Author self-review retains existing source/lease/identity/cancellation/output
  verification guards. No additional review agents used. This is not Windows
  physical-device or complete Stable acceptance.

## Publication gate still unresolved

The earlier inspection found no missing reviewed runtime package entries in the
Windows NuGet graph (27 entries plus separately recorded .NET runtime). Existing
notice/hash checks pass, but do not establish complete corresponding-source
coverage for the bundled Windows FFmpeg and its statically enabled dependencies.

The pinned upstream release autobuild-2026-09-30-13-08 was checked through the
GitHub API. It contains binary packages/checksums and no source/license/notice
asset. The upstream README points to build scripts, not a verified complete
source archive for these exact binary dependency revisions. The bundled
ffmpeg-notices/SOURCE-STATUS.txt still explicitly says PENDING.

THIRD-PARTY-NOTICES.md requires source provision before public release. Do not
change PENDING to verified, reuse the macOS x264 pin for Windows, or treat an
upstream homepage/build script as complete source. Source coordination or a
different FFmpeg distribution/build is a material follow-up. Integration and
branch-push CI can proceed independently; do not create a v0.4.0 tag (which would
trigger the auto-publisher), draft or public Release while this gate is unresolved.

Remote master initially c737e6b421b31f3b1b9de5c9e1e98dbdf0714eec. Both v0.4.0
tag and Release API returned404 before integration. No force push or replacement
of an existing release is authorized or needed.
