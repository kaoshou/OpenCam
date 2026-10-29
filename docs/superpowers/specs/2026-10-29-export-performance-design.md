# Safe export performance improvements

The approved direction is to measure export phases, avoid video re-encoding for audio-only changes, and evaluate hardware encoding before changing the default encoder.

Preserve NativeOnly recording, source fingerprints, packet/timing validation, cancellation cleanup and publish-after-verification. No recording-core changes, version changes, release or new user settings.

Approaches: (1) extend the existing verified copied-video/normalized-audio exporter (selected first); (2) hardware rendering (evaluate next, requires platform quality/failure testing); (3) parallel render/cache redesign (defer until measurements justify complexity).

For complete clips with compatible H.264 video, volume, mute and audio fades may use normalized audio while preserving encoded video. Trims, visual changes, unsupported stream combinations, changed resolution/quality and editable pointer composition retain the current render path. Single-clip audio normalization must not depend on a concat manifest. The original validation requirements remain unchanged.

Measure inspection, rendering, copying, audio conversion, verification and total export wall time. Compare the new path to the existing render path on the same generated media; synthetic tests prove algorithm/media correctness, not physical recording performance or all hardware quality.
