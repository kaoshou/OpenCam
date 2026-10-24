# Editable pointer: timing boundary investigation

Status: player improvements implemented; editable pointer capture/composition NOT implemented. No version, public release or website deployment change.

## Required invariant

The user operates a normal desktop cursor, records cursor-free frames, and can change the rendered pointer afterward. A pointer sample must map to the acquisition time of the source frame, not to the time an encoder progress line reaches the application. The approved design requires reporting an architecture change before expanding the capture implementation.

## Current boundaries

- `FFmpegScreenRecorderEngine.cs` starts its performance stopwatch immediately before starting FFmpeg (around line 502). Stderr parsing provides encoded frame counts/media progress, not the original acquisition clock anchor. First-frame ACK confirms output, not the time of the original screen sample. Audio startup, encoding and pipe delivery can intervene.
- Windows uses external FFmpeg capture filters. Upstream `gfxcapture` obtains `SystemRelativeTime`, then subtracts the first timestamp before publishing frame PTS. The host application does not receive that removed origin through its current protocol. `ddagrab` similarly uses a relative origin; GDI uses a different clock path. One offset inferred from process launch or progress is not a verified bridge for all three routes.
- macOS already has an owned ScreenCaptureKit/VideoToolbox helper. Its timer selects the latest surface, obtains the host clock and subtracts a local `origin` before encoding (`main.swift` around lines 186–188). Neither that origin nor the selected surface's acquisition timestamp is currently sent to the application. The current READY message is a readiness signal, not a clock mapping. Retaining the surface timestamp and reporting a clock anchor is feasible within that helper, but still requires native verification.
- Source-PTS trim/reorder mapping in the editor cannot recover a capture clock origin that was never recorded. Segment sealing or successful media probing does not repair this missing information.

Upstream source references (master inspected, NOT a claim that the exact shipped FFmpeg revision was verified):

- https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavfilter/vsrc_gfxcapture_winrt.cpp
- https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavfilter/vsrc_ddagrab.c
- https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavdevice/gdigrab.c

## Controlled probe

`tests/diagnostics/capture-clock-probe.mjs` supplies 30 synthetic frames at 30 FPS to FFmpeg, checks resulting frame count and first PTS with ffprobe, varies input start delay, and observes the same progress message immediately and through a real 500 ms delayed callback. It captures no desktop, microphone or user media.

Evidence: `/private/tmp/opencam-pointer-clock.json` (FFmpeg 7.1.2, local macOS).

| Start delay | First supplied frame | First source PTS | Immediate inferred origin | Delayed inferred origin |
|---|---:|---:|---:|---:|
| 0 ms | 2.638 ms | 0 s | 9.241 ms | 509.623 ms |
| 350 ms | 353.260 ms | 0 s | 329.760 ms | 831.397 ms |

The identical progress payload produces a roughly 500 ms different inferred origin solely due to observation delay. This demonstrates why receipt-time-minus-media-time is not a trustworthy acquisition mapping. It does NOT measure Windows WGC latency, establish the user's stuttering root cause, or prove an OS acquisition implementation. Input supply time is not an OS capture timestamp.

## Concrete alternatives requiring scope choice

1. Recommended: introduce an owned Windows WGC capture helper that exports acquisition-clock anchors alongside video; extend the existing macOS helper likewise. Begin editable-pointer support on these high-performance routes only. Keep compatible GDI/Desktop Duplication recording unchanged until their clock bridges are independently established. Costs: a new Windows native capture lifecycle, GPU/monitor compatibility work, packaging, failure recovery and hardware regression coverage. Benefit: owned timestamps and cursor suppression without maintaining a custom FFmpeg distribution.
2. Maintain a pinned custom FFmpeg build exposing capture clock anchors for supported filters. This retains the external FFmpeg capture path but adds patch maintenance, platform builds, source/license distribution and upgrades; every supported capture filter still needs verification.

Do not silently replace the existing capture implementation, expose an apparently working editable-pointer switch, or use a fixed startup delay. Persistence, schema 5, style switching and preview/export composition remain pending this boundary decision and validation. Old baked-in cursor recordings are unaffected and cannot gain removable cursors retroactively.

## Player verification

- Browser suite: 19 groups passed, including desktop/tablet/phone widths, touch, local-file export, keyboard navigation, dialogs, reflow and axe checks. `/private/tmp/opencam-pointer-final.log` and matching `screens/` directory.
- Regression added for compact controls: language/search must fit in the visible toolbar; sidebar grows when needed while retaining at least half for transcript reading, bounded for short viewports.
- This is targeted automated coverage, not certification of Taiwan accessibility conformance or a physical-device claim.
- Node/player/CI 36/36; embedded `WebPlayerExportTests` 6/6, zero failures. The initial sandboxed .NET attempt could not establish MSBuild IPC and was stopped; the approved single-node run completed successfully (`/private/tmp/opencam-pointer-export-final-approved.log`). Existing build warnings remain.
- Independent Task 1 review found no critical/important issue. Added a direct keyboard-help-link focus assertion from its minor suggestions. Native pointer composition and physical assistive-technology behavior were outside that review, not approved by implication.
