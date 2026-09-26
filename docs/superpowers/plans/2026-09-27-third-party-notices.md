# Third-party notices implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution, or superpowers:subagent-driven-development only if the user selects delegated execution. Steps use checkbox syntax for tracking.

**Goal:** Correct the bilingual third-party summaries and ship the attribution and license material required by the actual Windows/macOS dependencies.

**Architecture:** Keep a concise About summary and a detailed package notice with upstream license texts. Derive the release inventory from resolved/published dependencies and the bundled FFmpeg configuration rather than treating every development dependency as shipped. Packaging checks must fail when required notice material is missing.

**Tech Stack:** Avalonia XAML, C# localization, Node.js validation/tests, PowerShell and shell packaging.

**Spec:** User-approved proposal in this conversation: remove duplicate FFmpeg entries, correct Avalonia ownership, add required dependency attribution, synchronize English/Chinese and both platforms. Do not change recording behavior or publish a release.

## Global constraints

- Preserve all existing uncommitted security changes and unrelated user files.
- Keep OpenCam's own license AGPL-3.0-or-later and source version 0.2.3.
- Do not infer native component licenses from a managed wrapper's MIT license.
- Preserve exact applicable upstream copyright, license and NOTICE text.
- Do not label a normal project homepage as complete corresponding source.
- No commit, push, tag or release in this task.

## Review focus

1. AboutWindow and SettingsWindow must not diverge or duplicate FFmpeg.
2. Font/native notices must survive single-file Windows publishing.
3. Platform-specific and build-only NuGet assets must not be falsely described as shipped.
4. Actual FFmpeg/x264 source versions and configuration must match the binaries, including the Windows third-party build.
5. Notices must be present in final bundle resources/installer payload, not merely in the repository.

## Task 1: Inventory and attribution evidence

**Files:** Create `THIRD-PARTY-NOTICES.md` and `third-party/`; inspect each production `.csproj`, resolved `project.assets.json`, runtime publish manifests, FFmpeg build/install scripts and release workflows.

- [ ] Separate runtime components from build/test packages and record the resolved version, owner, source, license and platform.
- [ ] Inspect upstream package license/NOTICE text for Avalonia, CommunityToolkit.Mvvm, Serilog plus both sinks, Microsoft runtime/extensions, NAudio, Tmds.DBus.Protocol and MicroCom.Runtime.
- [ ] Preserve SkiaSharp/HarfBuzzSharp third-party notices, ANGLE native notices, and the actual bundled Inter font license. Check runtime-pack LICENSE and THIRD-PARTY-NOTICES too.
- [ ] Inspect FFmpeg `-L`, `-version` and configuration per release platform. Record FFmpeg/x264 exact sources and source-provision requirements; do not assume the local smoke-test binary equals the release build.
- [ ] If corresponding Windows build-source material is unavailable, record the exact gap and block claims of completed release compliance rather than replacing it with an unverified link.

## Task 2: Correct bilingual About summaries

**Files:** Modify `src/ScreenRecorder.Core/Localization/LocalizationService.cs`, `src/ScreenRecorder.UI/Views/SettingsWindow.axaml`, `src/ScreenRecorder.UI/Views/AboutWindow.axaml`; add focused tests under `tests/ScreenRecorder.Media.Tests` using the existing repository-file test helper.

- [ ] Add tests asserting both About surfaces use one FFmpeg/ffprobe summary; localized Avalonia text names The AvaloniaUI Project; neither locale retains conflicting blanket LGPL/GPL claims.
- [ ] Run focused tests and confirm they fail on the current duplicate/incorrect declarations.
- [ ] Change the section title to major third-party components, correct ownership and summarize the approved dependency groups. Add a clear reference to the complete bundled notices, without inventing a nonfunctional link.
- [ ] Run focused tests, build the UI and visually inspect Chinese/English layout. No recording/audio code changes.

## Task 3: Ship and validate notices

**Files:** Modify `NOTICE.md`, `README.md`, `scripts/package-macos.sh`, `installer/build_installer.ps1`, applicable `.github/workflows/*`, `scripts/check-release-notices.mjs`, `website/tests/release-notices.test.mjs`; add a notice assembly helper only if needed for resolved runtime assets.

- [ ] Add failing tests for missing third-party manifest, missing referenced license file and incomplete platform-specific notice payload. Use real temporary directory fixtures and preserve current project-license/source-revision tests.
- [ ] Include `THIRD-PARTY-NOTICES.md` and required license files in macOS Resources and Windows installer payload. Ensure CI's direct publish path is also covered.
- [ ] Extend package validation to reject missing/empty required material and unsafe manifest paths; tests must check the payload rather than merely matching script text.
- [ ] Update bilingual README and NOTICE to point to the detailed attribution and distinguish OpenCam's license from third-party licenses.
- [ ] Run the full website test command, relevant .NET test suites, Release UI build, shell syntax checks and `git diff --check`.
- [ ] Inspect representative assembled notice payloads for both platforms. Do not bypass the existing dirty-tree release guard or claim an installer was built when only fixtures were validated.

## Handoff

Report exact files changed, verified attribution corrections, test results and any remaining binary/source evidence gaps. Keep security functional acceptance and Windows recording verification as separate pending work. Publication is not authorized by this plan.

## Execution ledger

- Ruling: Work in the existing checkout under the user's approval of direct implementation; preserve the uncommitted security candidate. Creating a clean worktree would omit that candidate. No Git operations or commits are part of this task.
- Ruling: Human license prose is reviewed against upstream material rather than tested with source-text regexes; executable assembly/validation gets temporary-payload tests. The plan's proposed prose snapshot tests would only detect intentional wording changes.
- Ruling: Upstream aggregate notices are retained unabridged; the platform inventory describes conditional runtime assets, not proof that every asset is loaded.
- Ruling: User approved no delegation, so final review is a separate local pass, not a fresh agent review.
- Baseline: website suite had an existing README source-version wording mismatch and sandbox-blocked Swift cache access. Preserve truthful source-version wording, update its expectation and rerun with required build permissions.
- Task 1: imported fixed upstream licenses and NuGet notices; Inter's embedded name table identifies 3.019/git-0a5106e0b and OFL 1.1. Windows FFmpeg corresponding-source coverage remains pending, not certified.
- Task 2: both About surfaces now share the same component groups; remove duplicate FFmpeg, misleading GPL/LGPL version pair and runtime xUnit entry; use exact upstream AvaloniaUI OÜ attribution alongside the package project name.
- Task 3: missing-third-party regression failed before implementation (Missing expected rejection), then passed. Added real payload assembly, integrity and path tests for both RIDs and an unreviewed-dependency rejection test.
- Verification: website suite 26 passed, 0 failed; Core 75 passed; Media 224 passed, 12 skipped, 0 failed. Release UI compiled as part of the .NET test build. Both modified PowerShell scripts parsed successfully; shell syntax and `git diff --check` passed.
- Payload verification: both RID fixtures passed. Actual local macOS resolved assets and installed runtime produced a verified payload with 26 package entries and 25 notice files, using runtime 8.0.29. SkiaSharp and HarfBuzzSharp upstream aggregate notices have identical SHA-256 hashes.
- Remaining acceptance: Chinese/English visual inspection of the new About layout, execution of Windows packaging on Windows, and review of the Windows FFmpeg binary's complete corresponding source. Fixture checks do not establish those results. No signed installer, release package, commit or push was produced.
- Scope: existing security changes were preserved. Automated regression results do not replace the separately pending real recording/security acceptance tests.
