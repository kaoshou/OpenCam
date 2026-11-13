# OpenCam v1.3.0 Publication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish validated 1.3.0 packages for the three existing platforms and simplify release attachments without losing source delivery.

**Architecture:** Keep application and capture behavior unchanged. Verify source archives using existing validators, then consolidate delivery; make release uploads use an explicit asset allowlist instead of directory globs. Audit and back up 1.2.1 before narrowly scoped metadata cleanup.

**Tech Stack:** Node.js, .NET 8, GitHub Actions, existing OBS/FFmpeg builders, Inno Setup, macOS packaging, Ubuntu packaging.

**Spec:** `docs/superpowers/specs/2026-11-13-v130-publication-design.md`

## Global Constraints

- Version 1.3.0; Windows x64, macOS arm64, Ubuntu x64 Beta only.
- No Windows ARM64, RPM, AppImage, Flatpak or recording-core changes.
- Preserve existing unrelated Linux/GNOME modifications and all original recordings/projects.
- No published-tag movement, existing-binary replacement or source removal.
- Public downloads stay on 1.2.1 until verified 1.3.0 publication.

## Review Focus

- A missing, altered or incorrectly versioned source input must block publication.
- Bundled source pointers must resolve to the actual new delivery layout.
- Extra logs/test metadata must not become release assets through wildcard uploads.
- Cleanup must not remove old binaries, source archives or required fixed metadata links.
- Site/download publication must not advertise absent assets or unsupported Linux distributions.

### Task 1: Delivery and packaging guards

**Files:** `scripts/assemble-release-sources.mjs`, `scripts/check-release-source-provision.mjs`, `scripts/release-artifact-names.mjs`, `.github/workflows/build-and-release.yml`, `.github/workflows/verify-obs-core.yml`, `docs/testing/windows-obs-test.zh-TW.txt`; relevant tests in `tests/scripts/`, `tests/ci/` and `website/tests/`.

**Interfaces:** Consume existing verified OBS/Windows/macOS/Linux source inputs. Produce a versioned consolidated source archive, source manifest inside it, common SHA-256 list and explicit release asset allowlist. Keep internal per-component checks intact.

- [ ] Write failing tests for omitted/tampered sources, incorrect version, unwanted metadata uploads, source-pointer layout and stale test-title version; run and retain RED output.
- [ ] Implement minimal delivery changes and dynamic test title; do not weaken provenance checks or alter pinned dependency bytes merely to pass tests.
- [ ] Run affected script/CI/website tests, version check and `git diff --check`; expect zero failures. Commit this independently testable task.

### Task 2: Bilingual publication documentation

**Files:** `docs/releases/v1.3.0.md`, `docs/releases/v1.3.0-source-coverage.json`, `README.md`, `docs/USER_GUIDE.zh-TW.md`, `docs/USER_GUIDE.en-US.md`, `website/src/content.mjs`, `website/DOWNLOADS.md`, related website tests.

**Interfaces:** Consume Task 1 asset names/layout. Describe already implemented editor controls, preview scheduling, range playback/audio meter and support diagnostics without promising measured speedup or unvalidated OS support.

- [ ] Add/check assertions for 1.3.0 development documentation, accurate source download explanation, published-vs-development download separation and Linux limitations; retain any RED results.
- [ ] Update bilingual descriptions and release coverage inventory after verifying unchanged dependency/source pins.
- [ ] Run `npm test --prefix website`, `npm run build --prefix website`, version consistency and relevant script tests; expect zero failures. Commit.

### Task 3: Review, official builds and publication

**Files:** final review package and evidence in `docs/testing/`; existing manual package and build/release workflows.

**Interfaces:** Consume reviewed final source commit; produce five official binaries plus consolidated corresponding sources/checksums, immutable tag and published release. Publication is user-authorized; do not move an unexpected pre-existing tag.

- [ ] Use an isolated checkout or verified existing worktree at current approved source; exclude all unrelated dirty files. Record source SHA and baseline evidence.
- [ ] Run fresh appropriate managed regression and whole-branch review. Fix important findings with RED→GREEN verification; retain skipped/manual limits.
- [ ] Push a development branch; build official packages with immutable expected revision. Inspect actual failures and pass all required platform/source gates before proceeding.
- [ ] Integrate reviewed changes into master without overwriting newer work; tag only the reviewed final SHA, dispatch the existing release flow, verify uploaded bytes and source access.
- [ ] Advance `website/downloads.json` only after all five public binaries and source archive pass download verification; deploy and verify site. Record actual results, not assumed physical acceptance.

### Task 4: v1.2.1 metadata cleanup

**Files/resources:** local release backup/evidence and the existing v1.2.1 Release; no binary or tag changes.

**Interfaces:** Consume live release inventory, original metadata bytes and package source-pointer audit. Produce a recoverable cleanup report, consolidated metadata access and updated release description.

- [ ] Download/hash targeted metadata; save release JSON, original notes and asset IDs/URLs. Inspect references in existing packages before deciding the exact removable set.
- [ ] Preserve the five binaries and four source archives. Upload a clearly named metadata archive containing retained original bytes and verify round-trip hashes before deleting anything.
- [ ] Put source/checksum guidance and Ubuntu lifecycle caveat in bilingual release notes. Remove only audited redundant standalone metadata; retain any required fixed link.
- [ ] Re-download preserved/replacement assets, verify checksums/access, and record exactly what was removed and how local backups can restore it.

## Self-review

Tasks share source artifact names/layout; Task 2 and publication must consume Task 1 names rather than invent parallel constants. The old-release cleanup is independent and cannot weaken the new release's gate. No application feature work is required. Final release safety takes precedence over compacting every attachment.
