# macOS Audio, Recording Stability, Finder, and App Icon Design

## Status

Approved in chat on 2026-09-16. The user also approved raising the minimum supported macOS version to macOS 13 so OpenCam can use ScreenCaptureKit for system-audio capture without requiring a third-party virtual audio driver.

This is the second macOS compatibility increment. It builds on the already implemented IPC, display enumeration, AVFoundation screen selection, screen-recording permission, microphone probing, and custom-region geometry work.

## Problem Statement

Hands-on testing of the first macOS build found five runtime defects and one packaging defect:

1. Recording a selected display fails, while a custom region can record successfully.
2. The custom-region selector has an opaque or blurred center instead of showing the content beneath it.
3. The output-location **Open Folder** action does nothing on macOS.
4. macOS cannot record system audio.
5. Audio from the built-in microphone sounds intermittent or noisy.
6. Finder and the Dock do not use the repository's OpenCam icon for the macOS application bundle.

The recorder log provides a reproducible explanation for the selected-display failure. Direct 2240×1260 AVFoundation input reaches `h264_videotoolbox` without a conversion filter and the encoder exits with `Error while opening encoder`. The same full-size input succeeds when an otherwise identity crop filter is present, because the filter graph forces frame conversion. The current 350 ms startup check can miss this delayed failure and leave the UI incorrectly showing a recording state.

The remaining implementation evidence is also concrete:

- `RegionSelectWindow` asks for macOS Blur first and supplies a semi-opaque fallback background.
- `OpenOutputFolder` invokes `explorer.exe` unconditionally.
- `MacOsAudioLoopbackCapture` reports `IsSupported == false` and returns no audio source.
- macOS microphone capture is combined with the screen in one AVFoundation input and does not normalize timestamps or sample rate before AAC encoding.
- the bundle copies `app_icon.png`, but its `Info.plist` does not declare `CFBundleIconFile` and no `.icns` resource is produced.

## Goals

- Record a selected macOS display reliably with Apple VideoToolbox and fall back cleanly when hardware encoding cannot start.
- Keep the custom-region center genuinely transparent while retaining a visible border, resize handles, instructions, and controls.
- Reveal the most recent recording in Finder, or open the output directory when no recording exists.
- Record macOS system audio with no separately installed driver.
- Support system-only, microphone-only, and mixed system-plus-microphone modes.
- Stabilize built-in microphone capture and preserve audio/video synchronization.
- Apply the existing OpenCam artwork to the macOS application bundle.
- Preserve existing Windows recording, WASAPI loopback, Explorer, icon, and packaging behavior.

## Non-Goals

- Replacing the full macOS video path with ScreenCaptureKit.
- Supporting macOS 12 or earlier for system-audio capture.
- Installing or configuring BlackHole, Soundflower, or another virtual audio driver.
- Redesigning the OpenCam logo or inventing new artwork.
- Code signing with an Apple Developer certificate, notarization, or App Store packaging.
- Changing Windows audio mixing or capture arguments except where shared abstractions need platform-neutral naming and tests prove unchanged behavior.

## Chosen Architecture

Use a hybrid macOS pipeline:

```text
AVFoundation screen input --------------------+
                                                |
AVFoundation microphone input (optional) -------+--> FFmpeg filters/mix --> MKV --> MP4
                                                |
ScreenCaptureKit audio helper (optional) --------+
```

Video remains in the existing FFmpeg/AVFoundation pipeline. A small bundled Swift executable uses ScreenCaptureKit only for system audio and streams normalized PCM to FFmpeg through a private per-recording FIFO. This limits native code to the capability FFmpeg cannot provide directly on macOS and leaves the Windows WASAPI implementation untouched.

The alternatives were rejected for these reasons:

- A complete ScreenCaptureKit video-and-audio rewrite would duplicate display, crop, cursor, recovery, and encoder behavior and create substantially more regression risk.
- A BlackHole-based solution would require end users to install a driver and manually manage audio routing.

## Component Design

### 1. Reliable selected-display encoding

`MacOsFFmpegProvider` will emit an explicit VideoToolbox-compatible pixel format for every full-display recording, not only recordings that happen to contain a crop filter. Custom-region filters and full-display conversion must converge on a single valid output format with even dimensions.

The output arguments will declare the hardware encoder's required `nv12` format. Software fallback will retain its compatible YUV format. Provider-level tests will cover both full-display and custom-region commands so a future filter change cannot reintroduce the failure.

Recorder startup will become an observable handshake rather than a fixed 350 ms sleep:

1. Start reading stderr immediately so diagnostics cannot block and progress can be observed.
2. Treat the first valid FFmpeg progress/frame report as successful startup.
3. Treat process exit before that signal as startup failure and retain the relevant stderr tail.
4. Apply the existing hardware-to-software fallback once.
5. If both attempts fail, return the actual error through IPC and never transition the UI to Recording.
6. Bound the handshake with a short timeout so unresponsive devices do not hang the UI.

### 2. Truly transparent custom-region selector

On macOS the selector will request transparent composition directly, without Blur, and use a transparent fallback background. The outer selection surface will have no fill. The green outline, resize handles, size label, and control toolbar remain opaque enough to operate.

Windows keeps its current presentation and native geometry path. Existing macOS scaling and physical-pixel bounds calculations remain unchanged.

### 3. Platform-aware output folder action

Move process argument construction into a small testable platform helper:

- Windows file: `explorer.exe /select,<file>`.
- Windows directory: `explorer.exe <directory>`.
- macOS file: `/usr/bin/open -R <file>`.
- macOS directory: `/usr/bin/open <directory>`.

Use `ProcessStartInfo.ArgumentList` rather than interpolated shell commands so spaces and punctuation in paths are safe. Failure will be logged and reflected in the status message instead of being silently swallowed.

### 4. Native macOS system-audio helper

Add a Swift command-line helper to the macOS platform project. It will:

- require macOS 13 or later;
- obtain `SCShareableContent` and choose the requested display, falling back to the main display only when the requested identifier is no longer available;
- create an `SCContentFilter` and an audio-enabled `SCStreamConfiguration`;
- capture 48 kHz, two-channel system audio;
- exclude OpenCam's own process audio to avoid feedback;
- normalize sample buffers to interleaved signed 16-bit PCM;
- write PCM to a private FIFO path supplied by the recorder;
- report readiness and structured errors over stderr;
- stop cleanly on termination and remove no files outside its assigned temporary directory.

The build will compile the helper only for macOS publish targets and copy it beside the OpenCam recorder binaries. Windows builds will not invoke Swift or require Xcode. End users receive the compiled helper and do not need developer tools.

`MacOsAudioLoopbackCapture` will own the helper process and FIFO lifecycle. It will implement the existing `ISystemAudioLoopbackCapture` contract, return FFmpeg raw-PCM input arguments, surface permission/runtime errors, and stop the helper before removing the FIFO directory.

### 5. Stable built-in microphone input and audio mixing

Screen video and microphone audio will use separate AVFoundation inputs. Each input receives its own thread queue so slow video frames cannot starve microphone callbacks. The microphone stream is normalized to 48 kHz before encoding.

Audio filters will be generated explicitly by mode:

- **Microphone only:** resample asynchronously to 48 kHz and correct small timestamp gaps before mapping the microphone stream.
- **System only:** map the helper's already normalized 48 kHz stereo PCM stream.
- **System and microphone:** normalize both sources, mix with `amix`, and map the single mixed output.
- **No audio:** emit no audio input or encoder arguments.

The resampler may add or trim a very small number of samples to follow the recording clock; it must not insert periodic synthetic blocks into active microphone audio. AAC remains the output codec. Queue overflow, device loss, or helper exit becomes an actionable recorder warning/error.

### 6. Capability and permission presentation

`SupportsSystemAudio` will be true on Windows and on macOS 13 or later when the bundled helper exists. The existing checkboxes continue to resolve to `None`, `SystemOnly`, `MicrophoneOnly`, or `SystemAndMicrophone`.

ScreenCaptureKit system-audio access is governed by Screen Recording permission, which OpenCam already preflights. Microphone permission remains separate. A missing helper or permission failure will not prevent video-only recording; OpenCam will show that audio capture could not start and use an explicit, tested fallback rather than claiming that system audio was recorded.

### 7. macOS app icon and bundle metadata

Use `src/ScreenRecorder.UI/Assets/app_icon.png` as the single macOS artwork source. The current source is 256×256 with alpha. The macOS packaging step will:

1. generate the standard iconset sizes using `sips`;
2. build `OpenCam.icns` using `iconutil`;
3. copy it to `OpenCam.app/Contents/Resources`;
4. set `CFBundleIconFile` to `OpenCam.icns`;
5. set `LSMinimumSystemVersion` to `13.0`;
6. sign the final bundle after the icon and native audio helper have been added.

Windows keeps `app_icon.ico` and its existing project properties. A future 1024×1024 replacement PNG will improve Retina sharpness without changing the packaging contract; this increment uses the user's current artwork as requested.

## Data Flow

```text
User presses Record
    -> UI validates screen and microphone permissions
    -> RecordingConfiguration resolves requested audio mode
    -> recorder starts ScreenCaptureKit helper when system audio is requested
    -> helper reports FIFO ready
    -> recorder starts FFmpeg with separate video/mic/system inputs
    -> FFmpeg normalizes/mixes audio and converts video pixel format
    -> first valid progress frame completes the startup handshake
    -> UI enters Recording

User presses Stop
    -> FFmpeg receives q and finalizes MKV
    -> ScreenCaptureKit helper stops and FIFO is removed
    -> existing safe MP4 remux runs
    -> Finder reveal action targets the final MP4
```

## Error Handling and Recovery

- VideoToolbox startup failure triggers one software-encoder retry; both failures include the captured stderr tail.
- FFmpeg exit before its first frame is a failed start, not a successful recording.
- System-audio helper failure produces a clear warning and does not corrupt video capture.
- A missing microphone produces a microphone-specific warning and never silently substitutes an unrelated input device.
- FIFO and helper processes are always cleaned up on normal stop, failed startup, cancellation, and disposal.
- Existing MKV session preservation remains the recovery boundary for interruptions after recording has begun.
- Finder launch failures are logged and shown to the user.
- No macOS process path or argument is passed through a shell.

## Test Strategy

Implementation follows red-green TDD for every behavior change.

### Automated tests

1. Full-display VideoToolbox arguments contain the required pixel format.
2. Custom-region arguments retain a valid crop plus pixel format chain.
3. A process that exits after 350 ms but before its first frame is reported as failed startup.
4. Hardware failure performs exactly one software fallback and does not report success early.
5. macOS selector configuration requests transparent composition and has no center fill.
6. folder-opening commands are correct for file and directory targets on both platforms and preserve paths containing spaces.
7. macOS audio arguments are correct for no audio, microphone only, system only, and mixed modes.
8. microphone filters normalize to 48 kHz and include asynchronous timestamp correction.
9. system-audio helper lifecycle creates a private FIFO, reports readiness, stops, and cleans up.
10. macOS capability detection requires macOS 13 and the bundled helper; Windows capability behavior remains true.
11. bundle verification confirms `OpenCam.icns`, `CFBundleIconFile`, minimum macOS 13, and executable helper permissions.

### Local integration and acceptance

1. Publish and assemble a fresh ad-hoc-signed `OpenCam.app`.
2. Confirm Finder and Dock show the OpenCam icon.
3. Record the selected 2240×1260 display for at least ten seconds.
4. Record a custom region and visually verify that the content remains visible through the selector.
5. Verify **Open Folder** reveals the final recording in Finder.
6. Record system audio from a known playback source for at least fifteen seconds.
7. Record the built-in microphone for at least fifteen seconds while speaking continuously.
8. Record system audio and the built-in microphone together.
9. Inspect all outputs with `ffprobe` for stream count, codec, sample rate, duration, frame rate, and monotonic timestamps.
10. Listen to the three audio cases for dropouts, repeated blocks, clipping, and drift.
11. Launch the finished app for user acceptance testing.

### Windows regression protection

- Run the complete Core and Media test suites.
- Build the full Release solution including Windows projects.
- Assert that Windows FFmpeg argument snapshots and WASAPI loopback behavior are unchanged.
- Keep `.ico`, Explorer, and Windows capability paths unchanged.
- Live Windows capture remains manual validation because the development host is macOS.

## Acceptance Criteria

- Selected-display and custom-region recording both produce playable MP4 files.
- The UI enters Recording only after FFmpeg has demonstrated a healthy start.
- The custom-region interior shows the desktop content without blur or solid fill.
- **Open Folder** opens Finder and selects the most recent file when available.
- macOS system-only, microphone-only, and mixed modes each produce one stable 48 kHz AAC audio stream.
- A fifteen-second built-in microphone recording has no audible periodic gaps or injected noise and has monotonic audio timestamps.
- The packaged app displays the repository's OpenCam icon in Finder and the Dock.
- The packaged app declares macOS 13 as its minimum version.
- Automated tests and Release builds pass without modifying Windows runtime behavior.

## Risks and Mitigations

- **Swift helper packaging:** compile only for macOS runtime identifiers, verify presence and executable permissions during packaging, and sign after copying it into the bundle.
- **ScreenCaptureKit format changes:** convert the runtime sample-buffer format to a fixed PCM contract instead of assuming buffer interleaving.
- **FIFO startup deadlock:** use an explicit readiness handshake, cancellation timeout, and deterministic process cleanup.
- **Audio clock drift:** normalize both inputs to 48 kHz and apply bounded asynchronous resampling before mixing.
- **Hardware encoder format differences:** cover hardware and software output arguments separately and retain one software fallback.
- **256×256 icon source:** generate a valid `.icns` now; document that a future 1024×1024 source improves high-density display quality without structural changes.
