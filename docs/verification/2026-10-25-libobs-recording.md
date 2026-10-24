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

The first complete solution run was **not green**: Core356 passed; Media1300 passed,21 skipped,6 UI failures (`/tmp/opencam-libobs-full.log`). The full exceptions were retained and read. Related42 UI tests subsequently passed with real Skia rendering (`/tmp/opencam-libobs-ui-followup2.log`). A fresh complete solution run including build then exited0: Core356 and Media1315 passed,21 skipped,0 failures (`/tmp/opencam-libobs-full-final.log`,6m25s Media). Its UI lifecycle trace recorded no render callback without font services. This run precedes the final explicit Automatic-choice/fallback-pin/thumbnail-cache/performance-diagnostics corrections, which require another fresh regression pass.

## Outstanding acceptance boundaries

Windows native compilation/packaging is being verified independently; no physical Windows success is claimed. macOS host has no active display link, so physical ScreenCaptureKit, microphone/system-device timing, permissions, mixed DPI/GPU, display unplug, lock/sleep, disk-full and multi-hour resource/sync behavior are not established by these tests. Original user media was not provided, so the exact offset in the old recording is not reconstructed or retrospectively corrected.

The recording runtime must remain disabled for general distribution until those relevant acceptance gates are evidenced. Existing released packages, media and user settings are not overwritten.
