# Direct raw frames for live preflight

Status: implemented locally, native acceptance pending.

## Context

The live range check encoded a disposable MKV stream and decoded it with a second FFmpeg process. Encoder/container/decoder buffering could make window movements appear late. The user approved direct latest-frame preview rather than encoded playback for this check.

## Decision

Reuse the existing platform capture inputs, validated selection and source configuration. In explicit live-preview mode, emit aspect-fitted 960×540 RGBA at 10 FPS to a latest-complete-frame sink. Consume selected audio into a null output while retaining the existing input-level services. Maintain one UI frame request at a time.

Formal recording and five-second trial still use the existing saved-media path. Trial startup, actual recording and finalization are distinct observable phases.

## Alternatives

- Shorter H.264/MKV buffers retain the entire encode/decode path and did not sufficiently resolve the reported native delay.
- New C# native capture implementations would duplicate device/region/cursor selection and introduce a second set of platform lifecycle rules. The current capture host can already deliver raw frames directly.
- Increasing UI polling alone cannot remove upstream encoded media latency.

## Consequences and risks

No compressed-video encoder, container or second decoder runs for production live preview. Raw transfer uses more bandwidth than compressed video, bounded by resolution and rate; the sink retains only a latest immutable complete picture and partial assembly buffer. The UI can show fewer than 10 FPS under load. Capture/audio initialization and OS scheduling may still cause delay.

Direct preview confirms capture range and input levels, not the full saved encoder path; use the real five-second trial to check the resulting video/audio. Native Windows/macOS latency, audio combinations, multi-monitor coordinates and DPI need manual acceptance. Do not claim a fixed latency or 30/60 FPS.

Evidence and manual steps: [direct preview verification](../verification/2026-10-20-direct-preflight-preview.md).
