# Recording core integration — 2026-10-25

Version remains 0.4.1. Development changes only; no Release or master update.

## Safety design

An isolated libobs helper owns capture, timestamps, mixing and encoding. Mature libavformat writes streaming Matroska to stdout; the existing recorder retains its exclusive output stream and project ownership. No destination pathname reaches the helper. Existing preflight, direct teaching-pointer decoration, segment/pause/recovery, editor, MP4/GIF and web exports remain in place.

Runtime absence, disabled manifest or unsupported configuration retains the compatibility engine before recording. Core/display identity is pinned across segments. Only an identified initial setup failure with confirmed cleanup, no ready acknowledgement and **zero copied media bytes** may take a fresh-path fallback. A failure after media starts preserves the recording and reports failure instead of silently switching cores. Explicit audio device identities are never guessed or substituted; choosing the system's default audio device intentionally retains the platform's default-device semantics.

The assembler defaults to `enabled:false`; this remains a development stage until physical acceptance. Source pins, recursive SHA-256 inventory, architecture/dependency closure and notices are checked before the managed selector may load a runtime. See `third-party/obs-runtime/SOURCE-INFO.md` for source and redistribution obligations.

## Evidence so far

- Native macOS build against official OBS33.0.0 and matching FFmpeg8.1.3 headers succeeded. Actual generated-media tests10/10 passed (`/tmp/opencam-libobs-source-health-green.log`). The output is decoded H.264/AAC, not fabricated media or header-only checks.
- A one-minute generated flash/beep sequence remained aligned within100ms at every measured second (`/tmp/opencam-libobs-60s-sync2.log`). This is compositor/codec/mux evidence, not physical audio-device latency evidence.
- Apple hardware encoding was exercised with generated video and decoded successfully. Initial failure exposed VideoToolbox extradata arriving only after its first encoded frame; MKV header writing now waits for the encoded packet callback.
- All requested audio modes produced the expected stream counts; recovery silence decoded as actual zero PCM. Selected-source callbacks and sample age prevent old meter values being marked live.
- A killed helper left previously streamed MKV decodable. Final progress includes all encoded frames and their full video span, so pause/stop does not lose the previous half-second observation gap.
- Startup source failure emits no placeholder media. Fatal SCK source failure is observed through its explicit status signal rather than stale texture dimensions. WGC capability is checked before capture; no unreported upstream DXGI substitution.
- Managed subprocess/runtime/identity/fallback integration76/76 passed (`/tmp/opencam-obs-final-managed-green.log`), including a real native helper through the already-owned managed output stream. Earlier broad recorder regressions281/281 passed.
- Known-format Windows PCM input now specifies bounded probing (`-probesize32 -analyzeduration0`) before its pipe. RED4→GREEN31 tests include a finite real PCM producer that stays open; no audio samples, capture timestamps or timeout values were changed.
- Web-player and performance-summary regressions37/37 passed (`/tmp/opencam-libobs-web-regression.log`).
- Final macOS disabled runtime was rebuilt from the corrected native source and independently tested:10/10 (`/tmp/opencam-libobs-staged-native-final2.log`); packaging/provenance33/33 (`/tmp/opencam-libobs-packaging-final2.log`). Stage `/tmp/opencam-libobs.3AGPQI/final-runtime-2` contains459 hashed files. Manifest SHA256 `62a9a5fb7440f4fb133e5d481cf3d3c080b649c77f505d39f845506a94a9553c`; helper SHA256 `4fde4b82acb843ec0dd6d6fd2ffccdb1cd5e93af3dfcf1209461ca1b981a9b80`. Build succeeded with two retained warnings (deprecated OBS data-path API and a platform-unused helper), not a warning-free claim.
- The task-created read-only OBS image was unmounted after verification; sources and evidence were retained. A fresh staged changing-video/H.264/AAC encode/decode test still passed after unmount (`/tmp/opencam-libobs-detached-stage.log`), excluding an accidental dependency on that mounted development image.
- Explicit Windows GDI/Desktop Duplication choices and macOS Standard capture are preserved. Only eligible Automatic/ScreenCaptureKit selections may enter the validated native runtime; pinned native recording cannot silently switch to Standard on resume. Mac guard tests were RED3→GREEN34.

The first complete solution run was **not green**: Core356 passed; Media1300 passed,21 skipped,6 UI failures (`/tmp/opencam-libobs-full.log`). The full exceptions were retained and read. Related42 UI tests subsequently passed with real Skia rendering (`/tmp/opencam-libobs-ui-followup2.log`). A fresh complete solution run including build then exited0: Core356 and Media1315 passed,21 skipped,0 failures (`/tmp/opencam-libobs-full-final.log`,6m25s Media). Its UI lifecycle trace recorded no render callback without font services. This run precedes the final explicit Automatic-choice/fallback-pin/thumbnail-cache/performance-diagnostics corrections, which require another fresh regression pass.

## Outstanding acceptance boundaries

Independent development run40133441548 at a1a51a21b8fa3c3f95ecde05a14e5e5e76d3e424 proved Windows native compilation and503-file disabled runtime assembly. Endpoint identity passed; packaging30 passed/3 platform-specific skips. Windows native synthetic acceptance did **not** pass:8 cases failed at `graphics_startup` before media output. Complete individual job logs/artifacts were retained and read. Run40134130010 at fb322bd0a419523099252b04c1ae490d3b7663d6 established D3D11 initialized successfully, but libobs shader lookup failed: its pinned `check_path` directly concatenates the registered prefix with each filename. The helper now supplies the required trailing `/`, on both platforms. No renderer substitution or test threshold change was made. Portable Ubuntu/macOS jobs passed after installing their normal FFmpeg test prerequisite.

Later broad Media runs also initially did **not** pass:1325 passed,21 skipped,1 UI-scope failure before its body (`ProjectWaveformLayoutTests`,height144, `/tmp/opencam-obs-automatic-media-full-green.log`); then a diagnostic Debug run recorded1304 passed,21 skipped,25 failures. Filenames are not success claims. Lifecycle tracing identified the Mac capture-preference localization test posting render work without Avalonia font services. Its OS skip is retained, and it now uses the existing pinned Avalonia test discoverer/service lifetime. Focused Release74/74 passed (`/tmp/opencam-obs-font-scope-release-green2.log`), including former cascade classes and actual managed-to-final-staged-core decoding. The222-row trace confirms the corrected test stays inside a font-service scope.

Final fresh Release verification is green:

- Complete Media suite1329 passed,21 platform/device-gated skips,0 failed,6m30s (`/tmp/opencam-obs-release-final.log`). Its3989-row UI trace has no render callback without font services; repaired Mac test remains within the UI service scope. This includes real managed-to-`final-runtime-2` generated-media acceptance.
- Rebuilt runtime path regression11/11 (`/tmp/opencam-obs-runtime-portable-release-green.log`). Windows had exposed a strict test expectation using mixed path separators; the expected `Path.Combine` now uses separate components, without weakening equality or changing runtime code.
- Complete Core suite356/356 (`/tmp/opencam-obs-core-release-final.log`).
- Whole-solution Release build exited0 (`/tmp/opencam-obs-solution-release-final.log`). This final incremental build reported0 warnings/0 errors; prior compiler/xUnit warnings remain in retained clean-build/test logs and are not claimed eliminated.
- Version consistency passed:0.4.1. Tracked diff whitespace check passed. Verbatim upstream license texts retain their original bytes, including upstream whitespace.

Latest immutable development snapshot is172f01fcc380da9d65a26c3184b81fe10983fa2f on `codex/libobs-core-validation-20261025`; all146 changed source/test/script/workflow files match the current worktree byte-for-byte. Final verification/plan documentation remains local. [Independent run40134960082](https://github.com/kaoshou/OpenCam/actions/runs/40134960082) succeeded on this exact SHA, all3 jobs green:

- Windows compiled the actual helper and minimal display-only DLL, assembled503 hashed files with `enabled:false`, and passed C++ endpoint identity checks. Packaging30 passed/3 Mac-only skips; actual generated-media native acceptance9 passed/1 Apple-only skip; managed contracts/ownership39 passed/25 platform-fixture skips, including actual H.264/AAC through the owned output stream.
- Portable managed macOS74/74; Ubuntu66 passed/8 Mac-only skips.
- Full job logs and artifacts were read and retained at `/tmp/opencam-libobs.3AGPQI/windows-evidence-final`. Windows evidence artifact ID12576577582 has GitHub API SHA256 `80b0ba675aca9c910227ac99987d91964e370adf63b293930c4b052fcc47a0bd`.

Fresh final source review found no remaining confirmed issue in the reviewed corrections; its approval is limited to disabled development staging. Current worktree HEAD stays313d28040dbdfe250e6947d1f444073cff3796b1 with nothing staged. Master's original dirty spec hash25780034e07095597e0d8b6af9d8f3280b88d34e and5 untracked plans remain unchanged. No master merge, tag, Release or version bump was made.

No physical Windows success is claimed. macOS host has no active display link, so physical ScreenCaptureKit, microphone/system-device timing, permissions, mixed DPI/GPU, display unplug, lock/sleep, disk-full and multi-hour resource/sync behavior are not established by these tests. Original user media was not provided, so the exact offset in the old recording is not reconstructed or retrospectively corrected.

The recording runtime must remain disabled for general distribution until those relevant acceptance gates are evidenced. Existing released packages, media and user settings are not overwritten.
