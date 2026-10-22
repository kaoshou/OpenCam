# Native motion capture implementation and verification

## Scope

User approved evaluation and implementation after reporting stutter in both
editor preview and exported MP4 at 20/30 FPS, with normal audio, and asked about
macOS. This adds opt-in capture backends, not a release or a version change.
Version remains 0.3.12. Existing FPS, presentation-stick and performance work is
preserved. No new review agents or scheduled jobs were started.

## Decisions and trade-offs

| Area | Implementation | Cost / limitation |
| --- | --- | --- |
| Windows | FFmpeg gfxcapture / Windows Graphics Capture, exact HMONITOR and relative crop | Requires filter support; explicit hwdownload still feeds existing encoders, so this is not zero-copy |
| macOS | ScreenCaptureKit surfaces → hardware VideoToolbox H.264 → timestamped Matroska pipe → FFmpeg video-copy/audio mux | Requires macOS 13+, Apple Silicon package, permissions and hardware encoder; no CPU fallback in native mode |
| Timing | Mac latest surface sampled on a monotonic clock; late ticks retain elapsed gaps | Static images may repeat; this is not original SCK sample-PTS passthrough and cannot manufacture missing motion |
| Backpressure | One latest surface; at most three pending VT submissions; bounded pipe/input queue | Under load frames can be skipped rather than accumulating unlimited latency |
| Stop / failure | Join producer before finishing muxer; shortest stream ends native mux; unexpected producer EOF terminates muxer | Abnormal termination may need existing MKV recovery; no silent audio-only continuation |
| Display identity | Single-display region pinned; native display loss/geometry change fails closed | Cross-display regions and changing monitor layout while recording are unsupported |

Mac keeps the existing PCM audio producers and AAC muxing. Video initialization
is prepared before opening audio inputs. This avoids adding the permissions and
encoder-setup delay to buffered audio, but does not certify physical-device
startup offsets or long-run A/V drift. Native mode uses SDR H.264; HDR, Retina
crop, rotated displays, pointer alignment and hardware colour behaviour require
physical checks. No game-process hooks or independent window picker are added.
GPU saturation can still cause stutter, including simultaneous high-resolution
playback plus capture; this patch is not evidence of measured speedup.

## Automated evidence

- Initial focused .NET checks: 24 passed (`/tmp/opencam-native-focused.log`).
- Swift helper compiled; generated 60 hardware-encoded frames with an injected
  0.533-second timestamp gap, verified all packets and full decode. The gap also
  survives AAC muxing with video-copy (`/tmp/opencam-native-swift-mux-final.log`).
  This uses generated pixels/sine only, not screen or microphone capture.
- New actual FFmpeg inherited-pipe test exposed native mux continuing the
  infinite silence source after video EOF: container had no duration. Original
  failure is retained in `/tmp/opencam-native-mux-diagnostic.log`. Native output
  now uses `-shortest`; lifecycle checks passed 12/12 in
  `/tmp/opencam-native-lifecycle-green.log`, including producer failure stopping
  the muxer. Later capability checks are included in final regression below.
- First full solution command encountered an existing subtitle limit test's
  RegexMatchTimeoutException instead of InvalidDataException; no assertion or
  timeout was changed. Isolated subtitle group passed 19 tests
  (`/tmp/opencam-native-subtitle-check.log`). First full command must not be
  represented as all-pass (`/tmp/opencam-native-full.log`).
  That command finished with Core 254 passed / 1 failed and Media 1017 passed /
  20 skipped / 0 failed. Final lifecycle cleanup was subsequently extended to
  stop the native producer on muxer EOF/cancellation and report non-clean helper
  shutdown instead of treating it as a successful stop.
- Final Release solution command using `-m:1 /nodeReuse:false` completed with
  exit 0: Core 255 passed; Media 1017 passed / 20 skipped / 0 failed (5m17s),
  total 1272 passed (`/tmp/opencam-native-final-full.log`). This is not proof
  that the earlier subtitle timeout's underlying cause was fixed.
- Performance-summary Node tests: 2 passed. Version consistency: 0.3.12.
  Shell syntax and `git diff --check` passed. An existing Avalonia constructor
  warning remains. No native desktop or physical microphone test was run.
- After the final UI-only adjustment (show Mac backend status only when native
  mode is enabled, notifying bindings on load/settings change), rebuilt focused
  native/status/UI tests passed 77/77, exit 0
  (`/tmp/opencam-native-final-focused.log`). No source changes followed this run.

## Native comparison still required

Use the same 60-second moving clip, resolution, monitor, quality, audio inputs
and output disk. Compare compatibility capture versus native at 30 and 60 FPS;
on Windows compare actual GDI/DDA/WGC, not requested labels. On Mac compare
AVFoundation versus ScreenCaptureKit. Confirm **Actual capture** after startup.
Keep cursor effects off for the first comparison, then verify the presentation
stick separately. Inspect original MKV and exported MP4, not only editor preview.

Use `scripts/summarize-recording-performance.mjs` on the app log. Output frame
rate is not unique capture FPS. Mac helper additionally logs delivered SCK frame
count, repeated surface samples and encoder drops at shutdown; repeats on a
static desktop are expected. Check 10-minute A/V alignment, stop/pause/resume,
device removal, permission denial, monitor changes and simultaneous playback.
No measured GDI/WGC or AVFoundation/SCK improvement is claimed here.

## References

- Apple ScreenCaptureKit: https://developer.apple.com/documentation/screencapturekit/capturing-screen-content-in-macos
- Apple surface/queue guidance: https://developer.apple.com/videos/play/wwdc2022/10155/
- FFmpeg gfxcapture options: https://ffmpeg.org/doxygen/8.1/vsrc__gfxcapture_8c_source.html
