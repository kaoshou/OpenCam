# Direct presentation pointer on Windows and macOS

Accepted in chat on 2026-10-24: the operating desktop retains its normal system cursor; the recording contains only the presentation stick. This replaces editable-pointer recording for new recordings. Recording audiovisual safety takes precedence over decoration.

## Behavior and boundaries

- Keep the normal GDI/DDA/WGC or AVFoundation/ScreenCaptureKit recording path. Do not introduce a new video transport for the pointer.
- Render the user's supplied transparent silver-stick PNG during recording. Its tip is the hotspot. Do not retain the simplified red-tip drawing in either platform.
- Native cursor capture is off only when the presentation overlay is confirmed ready. If startup fails, record the ordinary cursor instead and report the optional-effect warning.
- During recording, loss of the overlay reports a warning and preserves video/audio without restarting the recorder. Do not claim the pointer is present when it is not.
- Preview and MP4 contain the same baked-in effect. New recordings never create editable pointer tracks, even when old settings request them.
- Do not change global system cursors, hide the desktop cursor, change cursor themes, use private APIs, install hooks, or record pointer event history.
- Preserve old project sources and pointer sidecars. Existing pointer projects remain readable/editable through the compatibility path.
- Start/stop/pause/resume/failure must own and clean up overlays. The macOS helper must exit on owner pipe EOF, not survive a crashed recorder.
- Use one source graphic; correct physical-screen coordinates on Windows and AppKit screen coordinates on macOS. Verify hotspot geometry, negative coordinates, scaling and clipping without recording private desktops.

## Approaches

1. Replace system cursors: rejected as the common basis because cross-application behavior differs and restoration affects the user's desktop.
2. Editable pointer sidecars and postprocessing: rejected by the user as unnecessary complexity, with an observed Windows capture failure.
3. Real-time display overlay captured by the existing recorder: accepted. Desktop may show the normal cursor alongside the stick; native cursor capture is disabled in the video, so the saved picture contains only the stick.

## Validation and release boundaries

No new application dependencies or version change (0.4.1). Do not merge, push shared branches, or publish a Release for this implementation without a later request. Local tests and native compilation do not substitute for Windows/macOS physical capture tests. Do not automatically capture private desktops. Preserve all original user recordings.
