# Native motion capture implementation plan

> Implement inline with executing-plans; preserve the existing working-tree changes.

**Goal:** Add opt-in Windows Graphics Capture and macOS ScreenCaptureKit recording paths while preserving timestamped MKV recovery and existing audio capture.

**Architecture:** Windows uses the bundled FFmpeg gfxcapture source when its required options are available, targeting an exact HMONITOR. macOS uses an isolated Swift ScreenCaptureKit/VideoToolbox producer emitting timestamped H.264 Matroska over an inherited pipe; the existing FFmpeg process copies video and muxes existing audio into the bound recording destination. The native producer is stopped and joined with the recording engine. Compatibility paths remain selectable; native startup failure is reported rather than silently claiming a different backend.

**Tech stack:** .NET 8, FFmpeg, Swift, ScreenCaptureKit, VideoToolbox.

## Constraints and risks

- Version stays 0.3.12; no release or scheduling action in this task.
- No game-process injection. Existing full-screen/monitor/region selection is retained; a separate window picker is outside this first integration.
- Preserve existing 5/10/20/30/40/60 FPS, pointer, measurement, audio, project owner and recovery work.
- Native options are opt-in. Record actual backend; unsupported native capture must produce a clear startup failure.
- No raw-frame transport across processes on macOS. Bounded encoder backlog; sample the latest surface using monotonic elapsed time. Static surfaces may repeat; late ticks preserve elapsed gaps rather than accelerating playback.
- Validate dimensions, native display identity, cancellation, process cleanup, encoder errors and resize/display loss.
- Actual Windows capture, Mac TCC/HDR/Retina and long audio/video synchronization require native manual validation; synthetic tests cannot certify them.

## Tasks

- [x] Add explicit native capture choices and round-trip settings/localization/status tests.
- [x] Add WGC capability probe and exact monitor/region plan, with malformed geometry and pinned-output tests.
- [x] Add timestamped Mac H.264 producer, including synthetic validation of Matroska timestamps and independently decodable frames.
- [x] Connect Mac producer lifecycle and video-copy muxing to the existing recording engine; keep audio mapping and MKV destination safety.
- [x] Compile Swift, build .NET, run relevant tests and summarize hardware validation and remaining risks.

## Verification focus

1. Selected display cannot be replaced by an unrelated display after enumeration changes.
2. Sampling timestamps remain monotonic despite delayed ticks; audio is not sped up to compensate.
3. Backpressure does not create an unbounded frame or encoded-packet queue.
4. Stop/cancel/startup failure reaps helper and FFmpeg and retains already-written MKV.
5. Unsupported filters/helpers do not masquerade as successful native recording.
