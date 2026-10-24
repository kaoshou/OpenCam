# Recording smoothness investigation and implementation

## User evidence

Uploaded OpenCam-Diagnostics-20261025-024014-ca232e.zip SHA256:
`20f587b812ffd7bc21b81920f76e1ef9c2e55498520f5624163ca690220fa596`.
Its actual app hash `8006f4245a987f7ffd9e509a400587b2be6cea9f66ce546184bf2d49a30b4717` matches our TeachingPointers-20261025 executable. User confirmed movement/video on the captured screen, not a stationary desktop. Intel Graphics and NVIDIA RTX5070 Laptop GPU are present; no capture/adapter mapping is available, so a cross-GPU root cause is not established.

| Local recording start | Capture | Requested FPS | Final FFmpeg FPS | Frames / media seconds | Reported drops |
|---|---|---:|---:|---:|---:|
| 02:31:01 | WGC | 30 | 4.11 | 150 / 36.5 | 0 |
| 02:33:41 | GDI | 30 | 14.46 | 311 / 21.5 | 58 |
| 02:34:56 | WGC | 30 | 4.27 | 94 / 22 | 0 |
| 02:36:09 | GDI | 30 | 16.02 | 305 / 19 | 53 |
| 02:37:04 | GDI | 20 | 13.51 | 554 / 41 | 68 |

All use NVENC at1920x1080 with system+microphone audio. Speed is approximately1, which does not establish sufficient video cadence: progress media time also advances with audio. Measured output-copy wait peaks are below1.3ms, not evidence of significant application destination backpressure. This does not establish every storage operation is fast.

WGC is measurably worse here; GDI is still below target. No claim that withdrawal alone solves all stuttering. No raw frame samples were attached, so per-frame PTS jitter and content motion were not independently decoded. Other logged events were read: preflight TaskCanceledException at02:30 is an earlier cancelled initialization; broken command-response pipes occur after stops, not at the low-FPS intervals. They are retained as separate evidence, not attributed as the video root cause or silently declared fixed.

## Source findings and bounded changes

- WGC currently downloads D3D11 BGRA to system memory before ordinary encoding. It is not an end-to-end zero-copy GPU path. [FFmpeg documentation](https://ffmpeg.org/ffmpeg-filters.html#gfxcapture) also describes variable compositor-driven cadence, capped rather than guaranteed by max_framerate.
- Removed editor PointerSettingsButton/flyout only; old metadata/readers/export validation remain. This intentionally removes the legacy editing entry as requested, not the stored pointer tracks.
- Both systems now share Screen capture dropdown placement. Windows GDI/Desktop Duplication remains; macOS Standard/ScreenCaptureKit maps to its existing native bool. No Mac capture/audio transport change.
- Stale Windows native flags are cleared in settings/main configuration and at Windows capture-plan entry. Pinned retired WGC sessions are rejected, never silently changed during recording.
- Former Windows overlay work-then-Sleep16 accumulated processing time and had no fixed-rate guarantee. New monotonic60Hz deadlines skip missed deadlines. A scoped high-resolution waitable timer is used where supported; bounded ordinary waits are the fallback. No global timer-resolution changes, busy spin or altered A/V clocks.
- Static teaching-stick pixels upload once; movement repositions the existing layered surface. Animated halos retain redraw. Existing macOS repeating60Hz timer remains unchanged.
- One overlay summary on stop contains sample/movement counts, mean/max poll interval, missed deadlines, timer mode and selected style/size; no coordinates or pictures. Recording performance adds recent output FPS and sample duration, without inventing capture/drop counters.
- No automatic FPS increase, resolution/quality reduction, duplicate-frame filler, audio-route changes or recorder restart for decoration.

## Verification evidence

- Editor removal RED3 then GREEN27: /tmp/opencam-remove-pointer-button-{red,green}.log.
- Capture preference/UI RED7; first GREEN49/50 exposed a stale programmatic editable-pointer UI property (Native=false was already unchanged, so its callback could not clear it). Added an always-disabled editable preference guard, not a weakened assertion. GREEN50 in .superpowers/sdd/2026-10-25-recording-smoothness/task-2-tests.log.
- Pacing/diagnostics RED6, unchanged2pass; GREEN22 includes pure monotonic deadlines, late skipping, counts/intervals, failed/throwing timer bounded fallback and ownership disposal, selected artwork and recorder lifecycle: /tmp/opencam-overlay-pacing-{red,green}.log.
- macOS helper compiled without opening a desktop overlay: /tmp/opencam-smoothness-swift.log. Website41 tests/build exit0: /tmp/opencam-smoothness-website.log, /tmp/opencam-smoothness-website-build.log. Version remains0.4.1.
- Full solution Release test exit0: Core356pass, Media1242pass/21skip, zero failures (1598pass total); /tmp/opencam-smoothness-full.log. Final targeted lifecycle/pacing verification22pass. Fresh immutable-tree review found no change requests. These checks are not a Windows physical-performance PASS; Windows test-package publishing is verified separately at handoff.

## REQUIRES MANUAL VALIDATION

Use a new disposable project. Back up existing projects. At30FPS, same1920x1080 region, same audio and same stick/style/size, record30–60seconds of continuous motion on the captured display. Verify ordinary cursor baseline, then stick; pause/resume and record a second time. Check original MKV, editor preview and MP4 with seeking and both selected audio sources. Compare recent output FPS, drops and overlay mean/max intervals from logs. Report if ordinary timer fallback is used.

Repeat Windows GDI/Desktop Duplication where supported and macOS Standard/ScreenCaptureKit with Auto/Apple encoder. Repeat100/125/150/200% Windows DPI and Retina/mixed/negative-origin screens. Expected: no missing A/V, duplicate cursor, shifted tip, orphan helper or renderer-driven restart; dropdown selection survives language/restart and locks during recording. Failures preserve originals and logs. If video remains below target despite regular overlay timing, further capture/encoding profiling is required; never claim frame duplication restored missing motion.
