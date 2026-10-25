# Native-only Windows test build

Version remains0.4.1. Development branch only; no master, tag or Release operation.

## Observed failure, not an invented root cause

The supplied Windows log shows one successful30FPS libobs run (steady29.84–30.09FPS), two successful20FPS runs, and four later30FPS startup failures with zero output bytes after about10.4 seconds. Each failed native attempt subsequently selected GDI, whose observed steady output was approximately14.7–17.5FPS. This explains why a nominal30FPS setting could actually use a slower old core, but does not identify why native initialization failed. The old adapter discarded the native code as `capture_failed`; it lacked screen/audio readiness facts. No GPU, audio-device, thread-pool or silent-source root cause is claimed.

## Build policy and safety

`OpenCamNativeOnlyTest=true` excludes the legacy FFmpeg screen-recorder implementation from the Media assembly. Shared interfaces remain in RecordingEngineContracts.cs. The Windows test package omits OpenCam.ScreenCapture.exe. Factories, router and orchestrator reject missing/corrupt runtime, incompatible configuration, pinned legacy sessions and compatibility retry. Old GDI/DDA preferences normalize to Automatic in this profile only. Existing projects remain editable/exportable; old-core recording sessions cannot resume in this profile. Normal builds, including macOS, retain their current policy.

FFmpeg/ffprobe remain for independent audio preflight, editing, preview, MP4/GIF/web-player exports and media checks. Hardware-encoder failure can retry CPU within libobs only before first media with confirmed cleanup. Real source geometry, selected audio callbacks, encoded-frame/first-byte acknowledgement, exclusive owned output, runtime hashing, bounded cleanup, source-loss handling and MKV recovery are retained. Native10s/managed15s startup deadlines are unchanged. Neither fake black video nor invented input audio is used to satisfy readiness.

Optional protocol1 startup diagnostics carry only fixed phases, elapsed milliseconds, actual/expected geometry, selected/audio-sample flags and frame/byte counters. Managed code allowlists codes/stages, bounds fields and rejects contradictory ready diagnostics. Raw upstream paths, titles and device identifiers are not logged. OBS compositor render/lag/frame-time observations are separate from encoded output counters and do not prove unique captured images.

First launch now selects20FPS across settings defaults, recording configuration and main-window selection. Explicit saved5/10/20/30/40FPS preferences remain unchanged; the pre-existing60→30 preference migration is unchanged and existing session timing is retained.

## Verification evidence

- Strict routing RED3 cases; profile RED old type present. Exact native-only probe then passed: old managed recorder type absent, missing runtime fails closed, stale settings normalize, independent audio preflight retained.
- Diagnostic preservation RED2 cases; contradictory ACK RED3 of5 cases; failed-source native diagnostic RED missing startup stage. New native generated suite10/10 passed, including changing decodable H264/AAC, content synchronization, source failures and killed-partial-MKV recovery.
- CPU full-HD generated recordings on Apple M3/macOS26.5.2:20FPS120frames/6.000s,30FPS181frames/6.033s; both decode video/audio completely, compositor lagged frames0, average render about2.10/2.14ms. CPU encoding is not hardware video encoding, but OBS still uses the OS graphics compositor.
- Package safeguard RED2 cases, then10/10 passed. Full packaging/provenance tests41PASS1environmentSKIP; sandbox AppKit failure was rerun with ordinary local process permissions, not changed assertions/timeouts. actionlint/diffcheck passed.
- Pre-20FPS full normal solution: Core356PASS; Media1339PASS22OS/deviceSKIP,0fail. Final20FPS regressions and exact Windows CI/artifact results are recorded at delivery.
- Fresh independent whole-branch safety review found no Critical/Important issue. Minor deferred: profile reflection inspects its own separately built assemblies, not assemblies extracted from the final single-file EXE. The explicit profile publish command and immutable CI provenance provide build assurance; package runtime inventory is checked independently. No claim of embedded-assembly extraction is made.

## Acceptance limits

No dedicated GPU or hardware video encoder is required, but functioning OS graphics support and a desktop session are required. CPU-only tests establish only the tested environment/content, not universal full-HD smoothness, physical YouTube/game load, real microphone/system endpoints or physical A/V synchronization. Windows generated CPU20/30 tests are included in the independent build workflow.

macOS arm64 helper compilation, bundle relocation/initialization, CPU/Apple-encoder generated output, source-loss and decode/sync checks pass locally. The internal display is online but asleep; physical ScreenCaptureKit recording, permissions, actual audio endpoints, hotplug/sleep/DPI and long-duration acceptance are not claimed. No macOS Release is produced here.

Inventory checks detect ordinary missing/changed files; they do not authenticate against a local actor replacing both a manifest and binaries or racing launch. Existing pointer/editor features are preserved with regressions, without a new visual-acceptance claim. Any recurrent physical startup fault remains an explicit failure requiring the new diagnostic facts, never a silent GDI recording.
