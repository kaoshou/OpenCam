# macOS Recording and Custom Region Compatibility Design

## Status

Approved in chat on 2026-09-16. This design covers the first compatibility increment: screen recording, custom-region recording, optional microphone recording, and actionable macOS permission handling. macOS system-audio capture is explicitly out of scope.

## Problem Statement

OpenCam works on Windows, but the current macOS path cannot start a recording and its custom-region selector opens without a visible border.

The investigation found four concrete platform defects:

1. The UI creates pipe names such as `ScreenRecorder_IPC_<32 hex chars>`. On macOS this becomes a 111-byte Unix-domain-socket path under the per-user temporary directory, exceeding the platform path limit. The server retries after swallowing the bind error, while the UI eventually reports a background-engine timeout.
2. `MacOsFFmpegProvider` hard-codes AVFoundation video device index `1`, without verifying that it represents `Capture screen 0` or that screen-recording permission is available.
3. `MacOsDisplayService` returns a hard-coded 1920x1080 display with index `1`, so monitor labels, capture dimensions, Retina scaling, and custom-region coordinates can be wrong.
4. `RegionSelectWindow` calls the Windows-only `GetWindowRect` API and relies on a transparent, undecorated window configuration that is not rendering a visible selector on the tested macOS/Avalonia combination.

## Scope

### Included

- Start and stop macOS screen recording.
- Record a selected display or a custom rectangular region.
- Enumerate actual macOS displays and use physical-pixel capture geometry.
- Request or explain macOS screen-recording permission before capture.
- Record the selected microphone when enabled.
- Keep MKV as the working container and retain the existing MP4 remux flow.
- Preserve the existing Windows capture, audio, display, IPC, and region-selection behavior.

### Excluded

- macOS system-audio capture. The control will be disabled on macOS with an explanatory label or tooltip; it remains unchanged on Windows.
- Native ScreenCaptureKit capture and CoreAudio loopback. These belong to a later compatibility increment.
- Cross-display custom regions. A custom rectangle is associated with one display and is clamped to that display's capture bounds.
- Unrelated refactoring or UI redesign.

## Architecture

The existing platform boundary remains in place:

```text
ScreenRecorder.UI
    -> platform-aware capability and permission presentation
    -> RecordingConfiguration over IPC

ScreenRecorder.Recorder
    -> IDisplayService
    -> IFFmpegPlatformProvider
    -> RecordingOrchestrator

Platform.Windows                 Platform.macOS
    existing behavior               CoreGraphics displays
                                    screen permission check
                                    AVFoundation screen input
```

Windows code paths remain selected by `OperatingSystem.IsWindows()`. New macOS behavior is implemented behind the existing platform services or explicit `OperatingSystem.IsMacOS()` branches.

## Components

### 1. Platform-safe IPC session names

Introduce a small pipe-name factory in the infrastructure layer. Windows retains the current descriptive name. Unix platforms receive a short prefix plus a truncated random identifier, keeping the complete `CoreFxPipe_...` path safely below the macOS Unix-domain-socket limit.

The listener must log unexpected bind/listen exceptions instead of silently retrying forever. This turns future IPC startup failures into actionable evidence.

### 2. macOS display enumeration

Replace placeholder display data with CoreGraphics calls:

- enumerate active display identifiers;
- identify the main display;
- obtain global display bounds;
- obtain physical pixel width and height;
- expose stable zero-based indices for the current enumeration.

Capture geometry uses physical pixels. UI coordinates from Avalonia are converted with the active screen's render scaling, then normalized to even width and height for H.264.

### 3. macOS screen-recording permission

Add a macOS-only permission helper around CoreGraphics screen-capture preflight/request APIs. Before the first recording command:

1. Preflight the permission.
2. If absent, request it once.
3. Do not report a successful recording while permission is unavailable.
4. Present an actionable message directing the user to System Settings and explaining that an app restart may be required.

The recorder also validates that the requested AVFoundation screen input is present. Permission or device failures retain the session metadata and return a specific error to the UI.

### 4. AVFoundation input selection

Select the screen by AVFoundation device name, `Capture screen N`, rather than assuming numeric device index `1`. Camera count and device ordering therefore cannot redirect recording to a webcam or a nonexistent input.

For a full-display recording, the provider captures the chosen screen without cropping. For a custom region, global coordinates are converted to coordinates relative to that screen and clamped to the screen bounds before emitting the FFmpeg crop filter.

Microphone audio continues to use an AVFoundation audio device. System-audio-only configuration is not emitted on macOS because the current loopback provider explicitly reports unsupported.

### 5. macOS custom-region selector

Keep the existing Windows native-bounds path unchanged. On macOS:

- do not invoke `user32.dll`;
- derive final bounds from Avalonia position, client size, and render scaling;
- use a macOS-compatible visible topmost selector presentation instead of relying solely on unsupported/unstable transparent-window composition;
- keep move, resize, presets, Enter confirmation, and Escape cancellation;
- associate the selection with the display containing its center and prevent an invalid cross-display crop.

The selector remains modal so recording cannot start before the user confirms or cancels the region.

### 6. Platform capability presentation

On macOS, the system-audio checkbox is disabled and its text explains that this increment supports microphone audio but not system audio. Windows retains its WASAPI checkbox and defaults.

Configuration creation must never send `SystemOnly` on macOS. Existing saved settings that request system audio are normalized at load time without altering Windows settings.

## Data Flow

```text
User selects display/region
    -> UI stores physical-pixel region and selected display index
    -> UI verifies macOS permission
    -> UI creates a short IPC session name
    -> Recorder receives RecordingConfiguration
    -> RecordingOrchestrator resolves display bounds
    -> MacOsFFmpegProvider selects "Capture screen N"
    -> optional display-relative crop is applied
    -> FFmpeg writes MKV
    -> Stop performs graceful finalization and existing MP4 remux
```

## Error Handling

- IPC bind failures are logged with pipe name and exception details; the UI receives a startup failure rather than waiting on silent retries.
- Missing screen-recording permission returns a permission-specific message and never transitions the UI to Recording.
- Missing AVFoundation screen input returns a device-specific message and preserves any created session directory for diagnosis.
- Invalid or out-of-bounds regions are clamped before FFmpeg launch; a region smaller than the minimum safe size is rejected.
- FFmpeg early exit remains subject to the existing hardware-to-software encoder fallback, but device/permission errors are not misreported as encoder failures.
- Existing MKV preservation and remux safety behavior remains unchanged.

## Testing Strategy

### Automated regression tests

1. A generated macOS/Unix pipe name stays below the safe full socket-path limit and supports client/server communication.
2. Windows pipe naming remains unchanged in shape.
3. AVFoundation arguments select `Capture screen N` by name rather than numeric device index.
4. Full-display capture emits no crop filter.
5. Custom-region capture converts global coordinates to display-relative coordinates and clamps the result.
6. macOS audio configuration never advertises unsupported system loopback, while microphone arguments remain valid.
7. Region-bound calculation uses the cross-platform Avalonia fallback on macOS and preserves the Windows native path.

Each production change follows red-green TDD: add the smallest failing test, run it to observe the expected failure, implement the minimum fix, and rerun the relevant suite.

### Local macOS acceptance

1. Build and run the app as a macOS application.
2. Verify the actual iMac display name/resolution appears.
3. Open the custom-region selector and visually verify a visible, movable, resizable border.
4. Confirm a region and start recording with system audio off.
5. Record visible motion for at least five seconds, stop normally, and verify MKV/MP4 output.
6. Run `ffprobe` and verify a nonzero duration, H.264 video stream, expected dimensions, and playable output.
7. Repeat with microphone enabled if permission is available.

### Windows regression protection

- Build the Windows-referencing solution on macOS to catch shared-code compilation regressions.
- Run all platform-independent unit tests.
- Keep Windows implementations and argument generation unchanged.
- Mark live Windows capture validation as `REQUIRES MANUAL VALIDATION` because the current host is macOS.

## Acceptance Criteria

- macOS no longer reports background-engine timeout due to the generated pipe name.
- Starting a permitted screen recording creates a growing MKV file.
- Stopping produces a playable MP4 that passes `ffprobe` validation.
- The macOS custom-region selector is visible and returns correct even-pixel bounds.
- macOS does not imply that system-audio loopback is available.
- The full automated test suite and Release build pass with zero errors.
- No Windows capture implementation is modified except shared code guarded by regression tests or OS checks.

## Risks and Mitigations

- **TCC permission attribution:** FFmpeg is a child process of OpenCam. The application preflights permission and validates the actual device list before claiming success. Packaged-app testing is required.
- **Retina and multi-display coordinates:** keep physical pixels at the capture boundary and isolate conversion in testable geometry helpers.
- **Avalonia transparency differences:** use a visible fallback presentation on macOS and retain the existing Windows presentation.
- **FFmpeg version differences:** select screens by stable device name and fail with diagnostic output when the device is absent.
