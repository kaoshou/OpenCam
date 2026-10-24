# Windows live PCM input probing

Scope: OpenCam 0.4.1 Windows WASAPI loopback FFmpeg input arguments only.
The declared s16le sample format, sample rate, channel count, and live pipe
already describe the audio stream. Default FFmpeg stream analysis can wait
for several seconds of PCM before starting the video encoder.

The input now places `-probesize 32 -analyzeduration 0` before its `-i`.
The small internal argument builder is also used by the cross-platform
regression, so that test runs the arguments supplied by the Windows capture.
Sample conversion, timestamps, silence keep-alive, backup heartbeat, and
device selection are unchanged.

## Automated evidence

Environment: macOS arm64, .NET SDK 8 / runtime 8.0.31, local FFmpeg 7.1.2
with libx264 and AAC. This checks FFmpeg input behavior without requiring
a Windows audio device.

`WindowsWasapiLoopbackInputTests` contains three format/pipe argument cases
(48 kHz stereo, 44.1 kHz mono, 96 kHz six-channel) and a real FFmpeg regression.
The regression feeds exactly 192,000 bytes (one second of 48 kHz stereo
s16le silence), keeps stdin open, and requires positive video progress from
a test video plus AAC encoding before EOF. Its 15-second timeout is a hang
guard, not a performance target. EOF then allows graceful process exit;
forced cleanup is treated as failure.

TDD RED: all four cases failed with the previous arguments. The real FFmpeg
case reported no video progress until EOF, then exited successfully. A
separate bounded FFmpeg characterization with the probe limits reported
video progress with the same finite input and open pipe.

TDD GREEN: Release build succeeded; all 31 tests selected by
`WindowsWasapiLoopbackInputTests`, `WindowsCaptureArgumentsTests`, and
`NativeCaptureTests` passed (exit 0). The real PCM regression passed and
FFmpeg exited normally after EOF. The build emitted the existing AVLN3001
preflight-window constructor warning. This focused run does not establish
the result of the complete solution suite.

Reproduction command (put the local FFmpeg directory on `PATH`):

```sh
dotnet test tests/ScreenRecorder.Media.Tests/ScreenRecorder.Media.Tests.csproj \
  -c Release --no-restore -m:1 /nodeReuse:false \
  --filter FullyQualifiedName~WindowsWasapiLoopbackInputTests
```

The local test runner needs permission to bind its loopback communication
socket. The first sandboxed invocation built successfully but aborted before
executing tests with a socket permission error. The RED invocation then ran
with that permission. An intermediate GREEN build encountered concurrent
OBS tests referencing their not-yet-created implementation types. After
those separate files were implemented, the final focused GREEN run above
built and passed with the loopback permission.

## Windows manual validation required

Prerequisites: a Windows machine, OpenCam 0.4.1 built with this change, and an
available system playback device.

1. Select system-audio recording and start a short recording with moving
   screen content and visible/audible synchronized markers.
2. Repeat with system audio plus a microphone, and with silence followed by
   playback. Stop each recording normally.
3. Check start, middle, and end markers and verify the resulting MKV/MP4
   video/audio streams and duration with ffprobe.

Expected: video starts without waiting for several seconds of PCM probe
prefill, recording stops cleanly, and the intended audio remains present.
Failure: a persistent startup delay, missing/corrupt media, or incorrect
audio/video timing. Retain sanitized FFmpeg arguments, application log,
session state, original media, marker offsets, and ffprobe output.

Physical WASAPI behavior, named-pipe behavior on Windows, and long-duration
audio/video synchronization remain REQUIRES MANUAL VALIDATION. This change
does not establish that all Windows synchronization problems are fixed.
