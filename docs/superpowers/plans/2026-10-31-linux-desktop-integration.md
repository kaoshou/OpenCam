# Linux desktop integration follow-up

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Repair reported Ubuntu folder/preview failures, then provide usable Wayland capture and capability-checked teaching tools.

**Architecture:** Continue the existing Linux development branch and approved platform-isolation design. Keep OBS/libobs capture, existing project safety and Windows/macOS behavior. Linux desktop adapters must report actual capabilities; a visible button is not proof of support.

**Tech Stack:** Existing .NET 8/Avalonia, Linux libobs helper, PulseAudio/PipeWire, desktop portals.

**Spec:** ../specs/2026-10-30-ubuntu-platform-support-design.md

## Global Constraints

- Version 1.2.0; no merge, tag change or Release.
- No FFmpeg/GDI recording fallback, fake display identity or synthetic selected audio.
- Preserve original MKV/project files on failure.
- Do not modify Windows/macOS native implementations or published packages.
- The original approved design already includes Wayland, teaching pointer, annotations and configurable shortcuts. User reiterated those requirements on 2026-10-31.
- Actual physical Ubuntu/GNOME validation remains distinct from virtual CI.

## Review Focus

- Paths containing spaces, shell metacharacters or leading dashes must remain arguments, never commands.
- A bounded preview producer must start actual playback before reaching its queue limit.
- Fresh desktop images must not rely on build-runner development packages.
- Portal-selected output identity, scale and permissions must be authoritative.
- Desktop tool/shortcut unavailability must not break ordinary recording.

## Task 1: Open output folders on Linux

Files: `src/ScreenRecorder.UI/Services/PlatformFolderOpener.cs`, `tests/ScreenRecorder.Media.Tests/PlatformFolderOpenerTests.cs`.

- [ ] Add tests showing Linux builds an explicit `xdg-open` argument vector, opens configured folder rather than old output, and reveals file via its containing directory.
- [ ] Watch the tests fail against the missing Linux branch.
- [ ] Add explicit Linux platform selection without changing Windows/macOS branches or unsupported-platform behavior.
- [ ] Run folder tests and shared Media suite. Actual file-manager opening requires Ubuntu desktop validation.

## Task 2: Streaming editor preview

Files: `src/ScreenRecorder.Platform.Linux/Native/playback.c`, `tests/ScreenRecorder.LinuxAppProbe/Program.cs`, Linux CI.

- [ ] Reproduce bounded writes without EOF against actual PulseAudio; require an advancing played-sample clock before completion.
- [ ] Exercise full `ProjectPreviewDecoder` using actual Linux media/audio processes, not just still decoding and EOF/drain.
- [ ] If default prebuffer blocks the bounded producer, configure explicit low-latency playback buffering, and keep actual device-clock accounting.
- [ ] Verify audible and muted playback, cancellation/restart and frame/position progress. Preserve first underlying error.

## Task 3: Dependencies and actionable launch failures

Files: Linux launcher, prerequisite manifest/setup, README, diagnostic collector, scripts tests, Linux CI.

- [ ] Test missing tools, x264 and xcb-xinput on controlled paths before implementation.
- [ ] Provide one checked setup entry point, explicit consent before apt mutation, and named missing items; never alter GLIBC manually.
- [ ] Validate complete package dependencies on a fresh Ubuntu runtime without compiler/development-package leakage.

## Task 4: Portal-selected Wayland capture

Files: Linux catalog/engine/helper, Linux UI source-selection adapter, portal protocol tests and desktop fixtures.

- [ ] Inspect OBS portal source lifecycle and negotiate selected streams before marking capture ready.
- [ ] Keep the selected authorized stream and geometry authoritative, not an X11 monitor index.
- [ ] Test cancellation, denial, retry, session loss, pause/resume and crop/scale boundaries.
- [ ] Verify actual GNOME portal capture before declaring Ubuntu Wayland support; retain fail-closed behavior until then.

## Task 5: Teaching tools and shortcuts

Files: Linux desktop capability adapters, existing cursor/annotation/shortcut composition points, platform tests.

- [ ] Probe X11 and Wayland separately; inspect safe ordinary-user APIs before selecting implementations.
- [ ] Keep teaching pointer tip alignment, sizes, no duplicate cursor and existing drawn-content semantics.
- [ ] Preserve configurable shortcuts, F10 pen toggle, F11 toolbar, F12 pause, Ctrl+F12 start/stop, Escape finish-and-clear defaults.
- [ ] Test drag/hide/show across mixed-DPI displays and annotation capture; no root/input-injection workaround.
- [ ] If Wayland cannot safely implement existing desktop-overlay behavior, record an explicit alternative for approval rather than silently dropping functionality.

## Task 6: Regression and test artifact

- [ ] Run Core/Media/native/script suites, protected-platform diff checks and Ubuntu real service integration.
- [ ] Test 20/30 FPS, pause/resume, preview and MP4 decode/timestamps, plus fresh package dependencies.
- [ ] Obtain one fresh whole-branch review, fix important findings with RED/GREEN coverage.
- [ ] Build development artifact with commit/hash/source/notices and explicit unvalidated physical scenarios; no Release.
