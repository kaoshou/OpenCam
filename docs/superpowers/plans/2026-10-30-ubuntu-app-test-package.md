# Ubuntu Application Test Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline. The user explicitly requested continuous implementation through delivery of a Linux test package, not another approval loop.

**Goal:** Deliver a downloadable Ubuntu 24.04 x64 OpenCam test application with actual libobs recording, project/media processing, bundled tools, source notices and honest validation status.

**Architecture:** Retain the approved separate Linux platform assembly. Reuse the proven Linux source probe as a separately compiled product helper speaking the existing OCOBS request/event protocol; preserve its diagnostic mode. Add explicit Linux composition branches without rewriting Windows/macOS implementations. Use a Linux C shim for glibc spawn/FD actions rather than Darwin ABI casts.

**Tech Stack:** Existing .NET 8/Avalonia 11.2.5, pinned OBS 33.0.0 and FFmpeg n8.1.3, Ubuntu 24.04 x64, X11/PipeWire/PulseAudio.

**Spec:** `docs/superpowers/specs/2026-10-30-ubuntu-platform-support-design.md`. This is the next bounded package milestone, not full three-platform release acceptance.

## Global Constraints

- Version stays 1.2.0; development branch only, no merge/tag/Release.
- Do not change Windows/macOS native sources, dependencies or packaging workflows.
- No FFmpeg video-recording fallback. FFmpeg remains audio preflight/media processing only.
- Existing MKV/project source preservation, owned FD outputs, pause/resume and no-content failure behavior remain mandatory.
- X11 and Wayland are targets; missing physical/GNOME verification stays explicitly manual, never equivalent to Xvfb or wlroots virtual validation.
- Test package must be self-contained for .NET, contain necessary media tools, exact revision, notices and corresponding source; OS desktop/driver libraries are declared prerequisites.
- No silent feature loss: initial Linux test limitations and capability errors must be visible; do not claim complete feature parity.

## Review Focus

1. FD collisions, inherited unrelated handles and cancellation cannot escape bound media sources/output; real Linux child-process tests exercise this.
2. Wayland must use a real portal source, never XWayland root; independently exercise the native backend and reject unavailable sessions.
3. Silent sources, default endpoint changes and missing selected devices must not be silently omitted; IDs are resolved identically for preflight and recording.
4. New shared factory branches must preserve Windows/macOS defaults and runtime hash checks; regression tests pin all three platform branches.
5. A green build alone cannot publish a misleading package: actual project recording/export, relocation, decode, hashes and source inventory gate packaging.

### Task 1: Runtime and native product protocol

**Files:** `native/obs-linux-probe/main.cpp`, `CMakeLists.txt`, new small Linux request/desktop helpers as necessary; `ObsRuntime.cs`, `ObsScreenRecorderEngine.cs`, `ObsRecordingEngineRouter.cs`; tests `LinuxRuntimeTests.cs`, `tests/native/linux-obs-probe.test.mjs`.

**Interfaces:** Existing `ObsCaptureRequest` protocol 1/2 and `OCOBS` ready/progress/stopped/error; Linux runtime `bin/OpenCam.ObsCapture`, backends `x11`/`pipewire`, CPU encoder. Diagnostic OCLINUX mode remains unchanged.

- [ ] Add failing manifest tests for linux-x64, complete inventory, tampering, wrong backend and unchanged legacy platforms; run focused .NET tests RED.
- [ ] Implement Linux runtime/platform handling; add explicit backend acceptance without relaxing old platform checks.
- [ ] Add product-mode native integration tests that fail against the diagnostic-only helper, then implement product entrypoint, gains, cursor, bounded crop, progress, owned stdout and clean stop. No diagnostic duration lease in product mode.
- [ ] Build/run native tests on Ubuntu; wrong source/gain/geometry fail before media; actual 20/30 FPS recording through existing managed engine passes. Commit verified changes.

### Task 2: Linux platform providers and composition

**Files:** New `src/ScreenRecorder.Platform.Linux/` assembly (`LinuxDesktopCatalog`, `LinuxFFmpegProvider`, audio-source adapters); explicit branches in Recorder `Program`, `RecordingEngineFactory`, `PreflightAudioCaptureFactory`, `PreflightAudioIdentity`, UI `MainViewModel`, project references.

**Interfaces:** Existing `IDisplayService`, `IRecordingDisplayIdentityService`, `IAudioDeviceService`, `IFFmpegPlatformProvider`, `IPreflightAudioPlatformProvider`; catalog enumerates native source IDs/geometries and exact Pulse IDs, with no fabricated monitor fallback.

- [ ] Write failing literal-fixture tests for native enumeration parsing, silent valid endpoints, missing devices and default-input/output distinction.
- [ ] Implement Linux-only catalog and provider; reject unsupported video fallback methods. Reuse audio-only preflight with real Pulse endpoint arguments; share identity resolution with formal recording.
- [ ] Add factories/UI Linux branches; preserve Windows/macOS selection. Disable neither selected audio source nor native safety checks. Unsupported desktop decorations are explicitly described rather than silently promised.
- [ ] Run catalog/provider/three-platform factory tests plus Core/Media regressions. Commit.

### Task 3: Linux bound media process and playback

**Files:** New Linux `LinuxProjectMediaProcess.cs`, `LinuxProjectAudioOutput.cs`, `Native/spawn.c`, `Native/playback.c`; `ProjectMediaBackend.cs`; tests `LinuxProjectMediaProcessTests.cs` and native FD fixture.

**Interfaces:** Existing `IProjectMediaProcess`, `IProjectStreamingMediaProcess`, `IProjectAudioOutput`; native spawn accepts validated executable/argv and bound descriptors, duplicates outside mapping range, maps only 0/1/2/3..N, closes all remaining FDs, owns process group and waits/reaps.

- [ ] Write and run failing Linux process tests: actual frame/PCM/probe/export, stream replacement, read-only input requirement, FD collision/unrelated-FD closure, cancellation and output quotas.
- [ ] Implement C ABI shim using glibc `posix_spawn` file actions and closefrom; never use Darwin structure layout/constants. Implement managed ownership/bounds/timeout/reaping without reopening source paths.
- [ ] Implement Pulse PCM playback with actual played-sample clock, not queued-byte count; connect Linux media backend. Run virtual-audio preview and trial playback.
- [ ] Run media integration tests under Linux and existing platform regressions. Commit.

### Task 4: Reproducible app build and test environments

**Files:** New `scripts/linux/build-app-media.sh`, Linux-only application workflow and fixture scripts; existing feasibility workflow unchanged.

**Interfaces:** Builds pinned FFmpeg with required media-processing features in a private prefix, same source revision as feasibility; stages private OBS/native helper, .NET self-contained application and Linux shim. Source/runtime manifests include all shipped code.

- [ ] Add executable packaging tests with temporary trees for missing helper, symlink escape, missing notices, hash tamper and source coverage; RED then implement assembler.
- [ ] Add Ubuntu job with exact development-branch trigger/read-only permissions. Build libraries/product helper, run tests in private X11/Pulse session, record at least 5 minutes at 30 FPS and 20 FPS comparison with silence/sound transitions, record genuine media checks.
- [ ] Attempt isolated Wayland compositor/portal session using OS packages, own bus/runtime directory; no user permission bypass or host session modification. Keep GNOME physical validation manual.
- [ ] Publish .NET application and run actual project record/pause/resume/editor-frame/export via test harness against the same production services. Preserve first errors and all failing logs. Commit fixes with RED→GREEN evidence.

### Task 5: Package, sources, diagnostics and verification

**Files:** Linux `Start-OpenCam.sh`, `Collect-Diagnostics.sh`, package readme/notices, assembler, `docs/testing/linux-app-test-package.md`.

**Interfaces:** tar.gz + SHA256SUMS + corresponding-source archive, version and full commit. Launcher resolves its own directory, clears injection paths, uses private libraries, checks declared OS prerequisites, never installs packages or requests root silently. Diagnostics excludes private recordings and raw audio by default.

- [ ] Bundle original source licenses/notices/build options for OBS/FFmpeg/application plus .NET notices. Do not redistribute unidentified system libraries; record required Ubuntu runtime packages and exact ELF `ldd` audit.
- [ ] Validate relocated package under a fresh directory with spaces, actual startup and recording/export using bundled tools. Decode MKV/MP4 and check timing/dynamic content; physical sync remains manual.
- [ ] Produce artifact only after gates pass, download and verify hash/commit/archive inventory locally. No Release.

### Task 6: Whole-change review and handoff

**Files:** Test report, plan/ledger; bounded fixes only.

- [ ] Run complete scripts/Core/Media suites and Linux integration jobs, inspect skips and list unverified physical/Wayland/desktop items.
- [ ] Request one fresh whole-change read-only review per executing-plans; resolve Important/Critical once using RED→GREEN tests, record deferred minor items.
- [ ] Commit/push development branch only; deliver genuine test package link, startup instructions, tested commit and limitations. Do not call Linux full support or physical A/V acceptance complete.

## Execution decisions

- Continue inline in the existing isolated Linux checkout/branch; no new worktree and no repeated execution-method question, as explicitly requested.
- Prior core-feasibility phase stays measured and incomplete for GNOME/physical acceptance; this plan may build a clearly labelled development package with those tests still manual, not waive final support gates.
- Shared application wiring is now in scope (unlike core-only phase); Windows/macOS native/build trees remain immutable. Original feasibility isolation test remains unchanged and is not invoked as an app-source invariant.
