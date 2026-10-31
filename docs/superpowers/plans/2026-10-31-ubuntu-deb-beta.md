# Ubuntu deb Beta implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver an installable Ubuntu 24.04 amd64 Beta with truthful build identity and license notices.

**Architecture:** Package the existing verified application under `/opt/opencam-beta`, with an executable launcher and desktop entry. Read one runtime dependency manifest from both the legacy prerequisite script and the deb builder. Keep recording engines and Windows/macOS packaging unchanged.

**Tech Stack:** Node.js packaging scripts, dpkg-deb/APT, existing .NET assembly metadata and Avalonia.

**Spec:** ../../testing/ubuntu-readiness-20261031.md (approved delivery direction)

## Global Constraints

- Version 1.2.0; Linux Beta only, no merge or Release.
- No legacy recording fallback, GLIBC replacement, new repositories or application execution as root.
- No project/settings deletion on upgrade or uninstall. No maintainer scripts which launch the application.
- Fixed-English package suffix; localize only the surrounding version label. Missing RID means Development Build, not a guessed package identity.
- Linux currently remains X11/CPU-only; desktop integration does not imply Wayland support.

## Review Focus

- Reject symlinks/special files in package input rather than installing escaping paths.
- Dependency declarations must include dynamic desktop/native assets, not merely startup executables.
- Desktop launch from arbitrary working directories must resolve packaged assets correctly.
- Package installation/removal must not touch user recordings or home directories.
- Incomplete licenses, missing app/helper/icon, wrong revision/version must fail packaging before emitting a deb.

## Task 1: Build identity and notices

Files: `Directory.Build.props`, `src/ScreenRecorder.Core/ProductInfo.cs`, SettingsViewModel, AboutWindow, translations, NOTICE.md, THIRD-PARTY-NOTICES.md, ProductInfoTests.

- [ ] Add tests for RID-derived Windows x64/macOS arm64/Linux x64 Beta labels, unknown/empty RID development label and unchanged numeric product version; run RED.
- [ ] Implement `ProductInfo.FormatBuildDescription(string? runtimeIdentifier)` and `PackageDisplayVersion`, sourced from Core assembly `OpenCamRuntimeIdentifier` metadata generated from `RuntimeIdentifier`. Use PackageDisplayVersion for Settings/About only, not main footer/export schemas.
- [ ] Add OBS license entry in About and accurate Linux notices/source locations, remove version-bound NOTICE introduction.
- [ ] Run Core tests GREEN; build UI to verify bindings/localization compile.

## Task 2: Debian tree builder and single dependency manifest

Files: `scripts/linux/runtime-packages.txt`, Install-Dependencies.sh, `scripts/linux/build-deb.mjs`, `tests/scripts/linux-deb.test.mjs`.

- [ ] Write tests exercising tree generation: identity/Depends, fixed application path/desktop entry, missing required file, unrecognized source metadata, symlink rejection and fresh destination. Run RED.
- [ ] Implement `stageDeb({appRoot, stageRoot, version, revision})`: validate entire input before creating destination, copy regular files into `/opt/opencam-beta`, create DEBIAN/control, `/usr/bin/opencam-beta` and desktop entry/icon. Require Ubuntu package metadata matching exact version/revision and all license/source files.
- [ ] CLI builds with `dpkg-deb --root-owner-group --build`, uses a private temporary stage and never overwrites an existing output.
- [ ] Move prerequisite array to manifest; copy it with legacy setup script. Add conservative GLIBC floor via libc6 >=2.39 and amd64 control architecture; Beta description states Ubuntu24.04/X11/CPU limitations.
- [ ] Run scripts tests GREEN; Linux CI exercises real dpkg-deb and desktop validation, not merely source matching.

## Task 3: Verified package integration and clean installation

Files: package-verified-app.sh, linux-app-test.yml, README-LINUX-TEST.txt, Linux deb CI.

- [ ] Produce deb only after source/notices and existing app checks, then SHA256SUMS covers deb and tar/source archives; upload deb alongside sources.
- [ ] In a clean Ubuntu24 container install the real deb via APT (no build packages), inspect every ELF dependency under installed path using existing exception policy, verify desktop registration, launcher and uninstall/reinstall preserve a user project sentinel.
- [ ] Verify installed menu launcher in Xvfb as ordinary user on Linux runner. Keep physical GNOME acceptance explicitly unverified.
- [ ] Run Core/scripts/UI checks, obtain fresh scoped review, commit/push development branch and run CI. Report failures/unverified scenarios; do not claim a tested artifact before CI finishes.
