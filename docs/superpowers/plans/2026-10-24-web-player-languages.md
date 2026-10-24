# Web Player Languages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Export a single-language, configurable zh-TW/en/ja player without changing playback or subtitle content.

**Architecture:** Persist a normalized language policy in app settings and carry an immutable snapshot through the existing export request/job into player data. Embedded catalogs and a pure resolver select the UI language; an in-place renderer updates text and accessible labels without rebuilding player state.

**Tech Stack:** C#/.NET 8, Avalonia, existing JSON IPC, embedded HTML/JavaScript, xUnit, Node test runner, Playwright.

**Spec:** docs/superpowers/specs/2026-10-24-web-player-languages-design.md

## Global Constraints

- Version remains 0.4.1; no merge, push, release, deployment, or automation.
- Initially enable zh-TW, en, ja; default Auto-detect; never allow zero languages.
- Fallback order en, zh-TW, ja; all Chinese variants map to zh-TW.
- Newly exported players only; no external dependencies/network resources.
- UI language must not translate or reset user content, subtitle selection, playback, search, or reading preferences.
- Preserve unrelated work and existing pointer/export safety boundaries.

## Review Focus

1. Corrupt/old settings and disabled defaults: normalization and view-model tests in Task 1.
2. Settings changed during export/retried operation: snapshot and request identity tests in Task 1.
3. Storage/locale API denial: resolver and browser tests in Tasks 2–3.
4. Switching during playback/search with Japanese or malicious user text: state and escaping tests in Tasks 2–3.
5. Narrow screens, keyboard focus, and mixed UI/content languages: browser/download tests in Task 3.

### Task 1: Settings policy and export snapshot

**Files:** Create src/ScreenRecorder.Core/Models/WebPlayerLanguageOptions.cs and tests/ScreenRecorder.Core.Tests/WebPlayerLanguageOptionsTests.cs. Modify UserSettings.cs in that directory; src/ScreenRecorder.UI/ViewModels/SettingsViewModel.cs; src/ScreenRecorder.UI/Views/SettingsWindow.axaml; src/ScreenRecorder.Core/Localization/LocalizationService.cs and LocalizationService.JaJp.cs; src/ScreenRecorder.UI/Projects/ProjectWorkspaceExport.cs and ProjectWorkspaceViewModel.cs; src/ScreenRecorder.Infrastructure/IPC/ProjectMessages.cs; src/ScreenRecorder.Recorder/Services/ProjectIpcDispatcher.cs, ProjectRecordingCoordinator.cs, RecordingContentExportJob.cs, ProjectWebPlayerExporter.cs, ProjectWebPlayerDocument.cs. Extend tests/ScreenRecorder.Media.Tests/SettingsViewModelTests.cs and WebPlayerExportTests.cs.

**Interfaces:** WebPlayerLanguageOptions exposes string[] EnabledLanguages and string DefaultLanguage (auto/zh-TW/en/ja); static Normalize(WebPlayerLanguageOptions? value) returns a defensive normalized copy. UserSettings.WebPlayerLanguages persists it; ProjectRequest.WebPlayerLanguages carries the export snapshot. Thread an optional WebPlayerLanguageOptions parameter through web export into WebPlayerData.UiLanguages; include it in export operation identity.

- [ ] RED: add normalization cases asserting null/empty -> all three+auto, duplicate/unknown removal, disabled default -> auto, input-array mutation cannot change normalized result. Add settings tests preventing last deselection, save/cancel/reload and disabling fixed default. Add export tests asserting options survive generation and do not change when settings change.
- [ ] Run targeted Core/Media tests using /tmp/opencam-sdk8-20261020/dotnet test, -c Release -m:1 and filters FullyQualifiedName~WebPlayerLanguageOptions, ~SettingsViewModelTests, ~WebPlayerExportTests; confirm failures are missing behavior.
- [ ] Implement normalization, localized settings controls, and explicit snapshot transport. Keep subtitle and MP4 options unchanged.
- [ ] Repeat targeted tests; require zero failures. Verify old callers without options preserve default behavior.
- [ ] Commit only task files: feat: configure exported player interface languages.

### Task 2: Embedded three-language player

**Files:** Modify src/ScreenRecorder.Recorder/WebPlayer/player.html and tests/web-player/player.test.mjs; extend ProjectWebPlayerDocument.cs only for embedded resource generation if needed.

**Interfaces:** resolveUiLanguage(options, remembered, browserLanguage) returns zh-TW/en/ja using Task 1 policy. setUiLanguage(language, remember=false) updates catalog-backed UI and document lang; t(key, parameters={}) returns localized text. Player consumes data.uiLanguages.

- [ ] RED: table-test every nonempty language subset with auto/fixed defaults, valid/obsolete remembered values, zh-CN/zh-HK/ja-JP/en-US/fr-FR/missing browser language. Assert single-language forcing, catalog key parity and no bilingual stock UI strings.
- [ ] Run node --test tests/web-player/player.test.mjs; confirm new cases fail.
- [ ] Add embedded zh-TW/en/ja catalogs, safe storage and locale lookup, compact selector, and textContent/attribute-based localized rendering. Relabel track options while preserving values. Translate dynamic messages and download boilerplate; preserve original content and appropriate content lang.
- [ ] Add and pass tests for absent Intl.DisplayNames/localStorage, malicious titles/captions, dynamic statuses after switching, and unchanged video time/search/selected tracks/reading settings. Run the whole Node player suite.
- [ ] Commit: feat: localize exported player interface in three languages.

### Task 3: Browser integration and handoff

**Files:** Extend tests/browser/accessibility.mjs and tests/ScreenRecorder.Media.Tests/WebPlayerExportTests.cs; create docs/verification/2026-10-24-web-player-languages.md.

**Interfaces:** Real generated exports from Tasks 1–2, existing browser fixture and accessibility harness.

- [ ] RED: add browser cases for all three UI languages, hidden selector with one enabled language, remembered preference after reload, denied storage, offline requests, and keyboard selection without playback/search/track reset.
- [ ] Run PLAYWRIGHT_BROWSERS_PATH=/private/tmp/vts-accessibility-browsers node tests/browser/accessibility.mjs; inspect failures before changes.
- [ ] Correct integration/layout issues without expanding scope; verify 390/768/1280 widths, focus return from menus, translated accessible names, and downloaded HTML/TXT keeping original transcript language/content.
- [ ] Run full Core/Media Release tests serially, Node player suite and browser suite. Preserve actual failures and platform limitations; do not label unrun Windows/macOS checks passed.
- [ ] Save representative desktop/mobile screenshots and verification record; run git diff --check and version check. Commit: test: verify configurable player languages and responsive layouts.

## Self-review and execution

Spec coverage maps settings/export policy to Task 1, complete rendering/resolution to Task 2, and offline/responsive/accessibility/state integration to Task 3. No subtitle-language migration or existing export rewriting is included. Recommended execution is native in this session because the three tasks share one tightly coupled export contract; perform a fresh final review after implementation.
