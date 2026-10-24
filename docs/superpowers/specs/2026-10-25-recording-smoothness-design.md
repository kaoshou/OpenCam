# Recording smoothness and capture-setting cleanup

User approved direct inline execution without further per-item confirmation; version0.4.1, no publishing. macOS should remain consistent where practical.

## Evidence and intent

The uploaded diagnostic package identifies TeachingPointers-20261025 and its application SHA256 matches our delivered binary. On moving content (confirmed by user), WGC/NVENC at requested30FPS produces about4.1–4.3FPS; GDI/NVENC produces14.5–16.0FPS, with53–58 reported drops. Destination writes peak below1.3ms; disk backpressure is not supported by this sample. These observations identify a worse WGC path, not the complete cause of GDI underperformance. Two GPUs are listed but capture-to-adapter mapping is unknown.

## Accepted approach

- Remove the editor post-production pointer button/flyout; preserve existing project metadata/readers and failure safety. Existing baked-in teaching-pointer controls stay on the main window.
- Withdraw Windows WGC activation and ignore stale high-performance preferences, using the existing GDI/Desktop Duplication selection instead. Do not silently switch a pinned live backend.
- Present both platforms with one localized Screen capture dropdown in the same setting position. Windows retains GDI/Desktop Duplication; macOS retains standard/ScreenCaptureKit, with its original bool serialization and capture/audio implementation unchanged. No generic high-performance checkbox/name or speed guarantee. Mac selection must survive language refresh and unrelated/locked settings saves.
- Replace Windows overlay work-then-Sleep16 timing with monotonic60Hz deadlines and a scoped high-resolution waitable timer. Skip missed deadlines instead of burst catch-up, without busy spinning or process/global timer-resolution changes. Timer API failure degrades to a bounded ordinary wait; decoration must not restart or kill recording.
- Keep macOS's existing repeating60Hz schedule; do not redesign its capture transport. Retain the same pointer styles, sizes and tip positioning.
- Log bounded overlay update cadence and recent output cadence so future Windows runs distinguish pointer-update irregularity from insufficient recorded frames. Logs contain counts/timing only, not cursor coordinates, images or device paths.
- Do not raise FPS, lower resolution/quality, insert duplicate frames, change audio routes, weaken capture/startup/health assertions, or introduce a new native video pipeline.

Alternatives: retaining/fixing the WGC GPU path entails driver/adapter/zero-copy work with higher capture risk; rejected for this immediate fix. Merely forcing constant-FPS duplicates cannot restore missed motion and is not a solution.

Physical Windows and macOS A/V, DPI, capture inclusion and improvement remain manual verification. No guaranteed30FPS claim. Original test ZIPs and dirty worktree changes stay intact.
