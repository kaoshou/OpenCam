# Desktop polish implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve editor interaction, teaching controls and support access without touching capture.
**Architecture:** Existing Avalonia/.NET UI; isolated support service and bounded background scheduling changes.
**Tech Stack:** C# / .NET 8, Avalonia, xUnit headless tests.
**Spec:** `docs/superpowers/specs/2026-11-04-desktop-polish-design.md`

## Global constraints

- No capture/core/dependency/version changes, publishing or uploading diagnostics.
- Preserve existing user GNOME prototype modifications.
- Local execution in existing non-master development checkout; no new worktree or branch switching.
- English/Traditional Chinese/Japanese user-facing labels.

## Review focus

- Late background replies must not publish across project/revision changes.
- Playback/scrubbing must retain priority over derived images/waveforms.
- Configured shortcut labels must remain correct after settings/language changes.
- Diagnostics must not follow links or collect media/settings or unbounded logs.
- Save-dialog cancellation/failure must not lose files or disable normal settings.

### Task 1: Editor background scheduling

**Files:** ProjectWorkspaceThumbnails.cs, ProjectWorkspaceWaveforms.cs, ProjectWorkspaceView.axaml.cs; ProjectPreviewPresenterTests.cs / new editor scheduling tests.
**Interfaces:** `SetVisibleThumbnailClips(IEnumerable<Guid>)`, `PollWaveformAsync()`, `PollThumbnailAsync()`.
- [x] Add failing tests for no background requests during scrubbing and visible-waveform priority.
- [x] Defer new requests during active playback/scrubbing; prioritize selected and visible waveforms, retaining cache bounds and stale-reply checks.
- [x] Verify existing inspector/cache/scrub tests and add operation timing without paths/content.

### Task 2: Annotation hints and stability

**Files:** AnnotationToolbar.cs, AnnotationOverlayHost.cs, MainWindow.Annotations.cs, MainViewModel.Annotations.cs; ScreenAnnotationUiTests.cs.
**Interfaces:** optional shortcut-label callback passed to annotation host/toolbar; no capture dependency.
- [x] Add failing tests for configured shortcut hints plus mode/visibility/mark preservation.
- [x] Display tool/toolbar/finish shortcuts in tooltips/accessibility; maintain toolbar sizing and input behavior.
- [x] Run headless annotation tests including repeat hide/show and three locales. Record physical mixed-DPI limits.

### Task 3: Support and diagnostics

**Files:** new UI Services/SupportDiagnostics.cs and Views/SupportPanel control; SettingsWindow.axaml(.cs), AboutWindow.axaml(.cs); Core localization partial; tests/ScreenRecorder.Media.Tests/SupportDiagnosticsTests.cs.
**Interfaces:** summary and bounded zip creation in service; reusable support panel handles save/clipboard/folder/browser UI.
- [x] Add failing tests: fixed environment fields, ZIP allowlist, symlink/size limits, no media/settings, no overwrite.
- [x] Add localized About actions: Check updates, Open logs folder, Copy environment, Export diagnostics; no background networking. Use existing platform folder opener and Avalonia save picker.
- [x] Run service/headless UI tests and build solution, then full suites.
- [x] Independent whole-change review and fix important findings; document completed checks and remaining manual acceptance. Do not push/release.
