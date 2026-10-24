# Editable pointer: clock bridge and integration evidence

The user approved an owned Windows WGC helper and an extension to the existing macOS helper. The desktop cursor remains under normal system control; editable recordings carry cursor-free video and private, replaceable pointer metadata. This document records implementation and executed checks on the development branch. It is not physical capture acceptance, a release approval, or a claim that the current worktree passed Windows CI. The product version and public release are unchanged.

## Clock contract

`FRAME_CLOCK v1` carries source video PTS, presentation host time, selected surface acquisition host time, and acquisition sequence. `POINTER v1` carries a host timestamp, capture-relative physical coordinates, visibility and the three-button mask. Host times use QPC microseconds on Windows and the monotonic native host clock on macOS. The source PTS/presentation pair establishes the explicit bridge; acquisition time remains a separate field when a static surface is repeated. Neither process launch nor receipt of an FFmpeg progress message establishes that bridge.

Private pointer/frame records are parsed by `NativePointerProtocol`, then forwarded through `INativePointerCapture`; they do not enter application diagnostic logs. Invalid metadata or `POINTER_ERROR` latches an incomplete pointer track and stops further metadata collection in the host while video can continue. Video transport failure remains a recording failure. Source probing and final clock verification must still establish that the encoded source PTS match the recorded bridge before accepting a complete sidecar.

## Implemented Windows boundary

- The owned helper is selected only when both `NativeCaptureEnabled` and `EditablePointerEnabled` are true. Existing compatible capture and the existing FFmpeg `gfxcapture` route retain their selection behavior when editable mode is disabled.
- WGC disables cursor capture on the capture session. It does not hide, replace, move, or constrain the user's desktop cursor. BGRA video uses a streaming Matroska transport with a one-microsecond timebase and `READY bgra-matroska-clock-v1` readiness.
- Button changes come from a mouse-only Raw Input registration on a dedicated message-window thread. Position and visibility are sampled separately. Coordinates use `GetPhysicalCursorPos`; crop/negative-origin clipping and hidden samples emit zero coordinates/buttons. The host resolves the monitor under a temporary PerMonitorV2 thread context and restores the caller's original DPI context synchronously.
- A fixed 2,048-sample queue preserves accepted button transitions; timestamps are strictly monotonic even when QPC quantizes adjacent events to one microsecond. Overflow disables pointer collection and emits `POINTER_ERROR`; it does not terminate video. A separate writer prevents native input callbacks from writing to the metadata pipe.
- WGC delegates retain shared state and use a callback gate. Shutdown stops entry, revokes both subscriptions, drains active callbacks, then closes the session/frame pool. The mouse window is unregistered/destroyed on its owning thread before joining that thread.
- The managed video bridge uses an asynchronous current-user-only named pipe and a bounded copy buffer. It waits for FFmpeg to connect before sending `start`. It stops the producer and drains the bridge before requesting muxer shutdown; joins have deadlines and unconfirmed cleanup prevents reuse of the engine. Metadata subscribers remain attached through final producer stop records. Windows keeps the selected encoder instead of using the macOS H.264-copy policy.

Raw Input timestamps and absolute position are sampled at event receipt, not supplied by mouse hardware. Heavy message-processing load can therefore delay a transition's reported time/position. The protocol currently covers left/right/middle buttons; injected input, extra mouse buttons, secure-desktop behavior and touch/pen semantics are not established by these tests. An application deliberately hiding its normal cursor is not overridden. A parent that stops draining the shared stderr protocol can still backpressure video clock reporting; queue overflow itself is bounded and nonfatal to the video producer.

## Executed Windows/portable checks

The baseline helper at `69cd9c5632dd12ff735dd60303a80d1cc8536dce` compiled in independent workflow run `40067962578`. That evidence predates the sampling, lifetime and managed integration changes documented here and does not validate their Windows compilation/runtime.

Local portable C++ checks on macOS:

```text
clang++ -std=c++17 -Wall -Wextra -Werror -pthread tests/native/capture-clock-tests.cpp -o /private/tmp/opencam-capture-clock-tests
/private/tmp/opencam-capture-clock-tests
clang++ -std=c++20 -Wall -Wextra -Werror -pthread -fsanitize=address,undefined tests/native/capture-clock-tests.cpp -o /private/tmp/opencam-capture-clock-tests-sanitized
/private/tmp/opencam-capture-clock-tests-sanitized
```

Both builds/runs passed. Tests were introduced before the queue, button decoder and callback gate: the initial compile failed on those absent APIs. Coverage includes a complete down/up pair between sampling ticks, transitions in one packet, bounded queue overflow, ordered draining, monotonic timestamp correction, negative origins, all crop edges, hidden-coordinate/button redaction, rejection of late callbacks and waiting for an active callback lease. These tests do not execute Windows Raw Input or WGC.

`ffprobe` consumed the test program's actual raw Matroska output and reported `rawvideo`, 32×32 pixels, with packet PTS `0.000000`, `0.033333`, `0.066666`, `0.500000` seconds. This verifies the transport's deliberate timestamp gap without desktop capture.

Managed scoped verification used the explicit .NET 8 SDK because the system SDK 10 sandboxed MSBuild run could not create its local IPC sockets. The failed sandbox run was stopped; it is not passing test evidence.

```text
/tmp/opencam-sdk8-20261020/dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj --no-restore -m:1 /nodeReuse:false --filter 'FullyQualifiedName~WindowsNativeVideoCaptureTests|FullyQualifiedName~WindowsNativePipelineTests|FullyQualifiedName~NativeCaptureTests' -v minimal
```

Result: **40 passed, one Windows-only integration case skipped, zero failures** on macOS. Real owned child-process fixtures exercised readiness, binary stdout, private clock/pointer parsing, invalid coordinates, explicit overflow, a 100,000-character diagnostic line, cancellation during readiness, idempotent stop and forced termination of an unresponsive helper. Synthetic FFmpeg integration exercised encoder preservation, both-flag eligibility, final-stop metadata delivery and subscriber detachment. Real local named-pipe tests exercised connection-before-start, binary bytes, EOF/drain, and cancellation before connection. An initial run exposed the macOS domain-socket path limit; shortening only the pipe-name prefix retained the full random GUID, and the unchanged assertions then passed.

The final two DPI-context scope/restoration cases were subsequently included in the UI/geometry agent's combined managed run: **73 passed, zero failures**. This is portable scope/lifetime coverage using an injected OS boundary, not a Windows mixed-DPI result. The existing Avalonia `AVLN3001` preflight-constructor warning was observed in incremental builds; it was not suppressed.

`pwsh -NoProfile -File tests/native/WindowsNativePackageTests.ps1` passed after an initial failure on the absent copy script. It uses generated binary fixture bytes and checks exact copied content, a known SHA-256, missing-source rejection and missing-publish-directory rejection. It does not compile or execute the Windows helper on macOS.

## Executed macOS checks

The macOS agent reported these completed commands:

```text
bash tests/native/OpenCamScreenCaptureTests.sh
PATH=/tmp/opencam-task2-ffmpeg.GOzyFj:$PATH bash tests/native/OpenCamScreenCaptureTests.sh --synthetic
PATH=/tmp/opencam-task2-ffmpeg.GOzyFj:$PATH bash tests/native/OpenCamScreenCaptureTests.sh --clock
```

All passed, including the final native run (`60770`, exit 0). They compile the helper, reject missing arguments, run synthetic Swift pointer assertions, encode 60 generated VideoToolbox frames, compare ffprobe PTS to emitted clock records, preserve a deliberate 0.533-second gap, decode all video frames and mux/decode synthetic AAC. They capture no desktop, microphone or system audio. Pointer assertions include Retina 2× scaling, negative X/Y origins, crop edges/Quartz Y conversion, hidden redaction, sampling limits, rapid transitions, quantized-clock monotonicity, mouse-only event masks and ring overflow/drain/wake behavior.

Managed `MacNativePointerCaptureTests` passed **8/8** using explicit SDK 8, Release, `--no-restore -m:1 /nodeReuse:false` (run `40624`). Portable pointer-track cursor tests passed **7/7** (run `26119`). Native VideoToolbox and managed IPC runs used approved sandbox escalation. No event-tap/permission preparation, permission prompt, physical screen capture, CPU comparison or physical one-frame alignment was performed.

## Validation packaging and outstanding acceptance

The independent `verify-editable-pointer.yml` configures native compilation, portable native/copy tests, the complete Core suite, managed host/bridge and pointer compositor/clock/cursor/encoder/geometry/export tests with the existing pinned FFmpeg installer, and a development Windows validation artifact. Generated media exercise MP4/GIF/web, hidden pointers and damaged tracks without desktop capture. A runner-only `global.json` pins the exact SDK 8 returned by setup-dotnet with roll-forward disabled; this does not change the repository's general release SDK policy. Expected helper argument rejection is checked for exit code 1 and exact diagnostics, then mapped to a successful verification step.

The validation artifact includes the exact native executable beside the managed application, FFmpeg/ffprobe, existing source/license notices, environment/TRX diagnostics and a helper hash record. Both existing Windows packaging workflows also compile and copy the helper for future packages. No workflow was dispatched, no package was published and no release/website/version action was performed by this implementation task. Local YAML/PowerShell syntax checks are not a replacement for the forthcoming Windows job.

Still **REQUIRES MANUAL VALIDATION**:

1. Build the current revision with the Windows SDK and execute the Windows-specific synthetic named-pipe/FFmpeg case. Verify the copied executable/hash and preserve the exact run/source revision.
2. On Windows, record a stationary and moving desktop with rapid physical press/release events; verify that every transition survives, cursor-free video stays valid after a forced metadata-queue failure, and the normal desktop cursor remains usable. Cover mixed DPI, negative monitor origins and crop edges.
3. Exercise repeated start/stop, cancellation, monitor loss, secure-desktop/lock transitions and sustained load. Preserve video, private sidecar, frame-PTS probe and process/handle/thread evidence; establish that all owned processes and native callbacks stop.
4. On macOS, validate real ScreenCaptureKit permission/event-tap behavior, Retina/multiple displays, normal auto-hidden cursor semantics and physical click-to-frame alignment. Generated VideoToolbox frames do not prove those OS interactions.
5. Compare preview/export placement against known visual click markers, including trim/reorder/speed/segment boundaries. Do not infer a physical one-frame alignment or reduced stutter from synthetic transport tests.

## Original progress-receipt probe

The earlier `tests/diagnostics/capture-clock-probe.mjs` experiment remains relevant: 30 synthetic 30-FPS frames were fed to FFmpeg, first source PTS was checked with ffprobe, and the same progress payload was observed immediately and through a real 500-ms delayed callback. Evidence was `/private/tmp/opencam-pointer-clock.json`, FFmpeg 7.1.2 on local macOS.

| Start delay | First supplied frame | First source PTS | Immediate inferred origin | Delayed inferred origin |
|---|---:|---:|---:|---:|
| 0 ms | 2.638 ms | 0 s | 9.241 ms | 509.623 ms |
| 350 ms | 353.260 ms | 0 s | 329.760 ms | 831.397 ms |

The roughly 500-ms change in inferred origin came only from observation delay. It demonstrates why progress receipt cannot recover a removed acquisition origin; it does not measure Windows latency or explain the user's physical stutter. Upstream master references inspected at that stage were [gfxcapture](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavfilter/vsrc_gfxcapture_winrt.cpp), [ddagrab](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavfilter/vsrc_ddagrab.c) and [gdigrab](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavdevice/gdigrab.c), not an attestation of the exact shipped FFmpeg source revision.

## Integrated local verification (2026-10-24)

The completed integration passed the fresh Release solution run with SDK8.0.425 and FFmpeg7.1.2: Core336 passed; Media1183 passed,21 platform/manual cases skipped,0 failures (`/private/tmp/opencam-pointer-integrated-full.log`). The refreshed cross-platform export matrix passed9/9 (`/private/tmp/opencam-pointer-export-final.log`): MP4/GIF/web MP4 visible/hidden/damaged-sidecar paths, real decoded pixels and unchanged source hashes. No desktop/audio was captured.

Fresh browser19 groups, Node36 and website41 tests/build passed. Native macOS synthetic clock/encode/decode/mux passed again (`/private/tmp/opencam-pointer-native-final.log`). Desktop and390px reading/help screenshots were visually inspected. These checks do not certify physical touch devices, screen readers, real WGC/ScreenCaptureKit permissions or physical pointer alignment.

A final regression reproduces an internal frame-clock probe cancellation escaping segment promotion despite an uncancelled caller. Promotion now records an incomplete pointer receipt on this internal deadline, preserving the sealed video; explicit caller cancellation still propagates. The initial faulty fixture was corrected before the genuine RED was observed. The post-fix full suite is tracked separately in `/private/tmp/opencam-pointer-deadline-full.log`.

## Independent whole-branch review and Windows integration

Fresh read-only review of `c1fd395..515eaec` found no Critical or Minor findings and one Important issue: destination-write failure could leave the pointer video encoder waiting for blocked sibling tasks. A generated-noise/disk-full regression and bounded worker/process cleanup are required before handoff. This is not a second review or a claim of physical validation.

Windows run40074334873 compiled the native helper and passed its protocol/package fixtures. Core reported330passed,3skipped,5failed. Complete failure evidence is `/private/tmp/opencam-pointer-windows-515eaec-failed.log`. Three collector cases attempted to reopen a file before releasing its write handle; production already disposes the collector before promotion. The replacement test attempted rename while its verified reader was still Windows-locked; the fixture now closes the reader to target the intended verification/reopen window. The symlink rejection test expected the Unix IOException rather than Windows InvalidDataException; platform-specific exact expectations preserve rejection. Storage guards are unchanged; the current Windows rerun must pass before package handoff.

Review limitations retained: real Windows capture/mixed DPI/secure desktop, real macOS permissions/Retina/click alignment, long-duration resource use, and actual screen readers/touch devices need manual acceptance. No physical results are invented from synthetic tests. macOS app-controlled cursor auto-hiding remains outside the user-approved ordinary-desktop scope; editor Hidden remains available.

## Earlier player evidence

The prior player work recorded 19 passing browser groups across desktop/tablet/phone widths, touch, local-file export, keyboard navigation, dialogs, reflow and axe checks (`/private/tmp/opencam-pointer-final.log` and `screens/`). Node/player/CI passed36/36 and embedded WebPlayerExportTests passed6/6. These are earlier targeted results, not certification of Taiwan accessibility conformance/physical assistive-technology behavior.
