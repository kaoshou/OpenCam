# Capture performance instrumentation and presentation stick

## Scope and observations

User reports Modern selected but GDI displayed, approves measurement before
optimizing capture, and selects a thin presentation stick instead of a laser dot.
This change extends existing capture diagnostics and cursor overlays; no new
capture backend or GPU adapter policy is introduced.

WindowsCapturePlanner requires an attested FFmpeg default-adapter mapping.
DxgiOutputCatalog intentionally rejects ambiguous hardware adapters, including
headless secondary hardware. Existing generic UnsupportedTopology obscured that
reason. AdapterIdentityUncertain is appended to the enum (existing numeric
values retained), displayed in zh/en, and logged with requested/actual capture,
filter availability and monitor/output/unattested-output counts. Eligibility,
pinned source identity, startup fallback and stop/recovery policy are unchanged.
Other possible causes remain missing filter, uncertain display mapping,
cross-monitor/rotated output and actual DDA startup failure. No current log from
the user's selected-Modern attempt is available, so their exact reason is not
claimed proven.

## Measurements

FFmpeg's existing progress stream supplies frame count, fps, speed, media time,
dup_frames and drop_frames. Each process attempt has a fresh random measurement
ID and logs a JSON line with prefix `Recording performance:` at process start,
at most every five seconds on progress, and after joined shutdown. Context
contains requested/actual backend, fallback reason, FPS, dimensions, encoder,
synthetic flag and whether output is a bound stream. No new device IDs, paths,
pixels or environment variables are logged by these measurements.

OutputFps is output frames divided by monotonic process elapsed time, including
startup/stop overhead. It is not unique captured FPS. FFmpeg duplicate/drop
counters describe its output processing and do not count every DDA repeated
desktop frame. Static frames are not automatically evidence of a stall.
Missing or invalid progress is null, not zero. Bound stream bytes, cumulative
WriteAsync wait and maximum write wait are measured without changing output
ordering or flushing; direct-to-file output has null byte/write metrics because
FFmpeg itself performs those writes. These waits include stream/OS buffering,
not physical disk latency alone. Live preview has no new performance log flow.

`node scripts/summarize-recording-performance.mjs app-YYYYMMDD.log` produces
one JSON summary per attempt, retaining actual backend, null values and whether
the attempt has a Stopped record. An unfinished run is not marked completed.

## Native comparison — requires Windows hardware

Use the same 60-second moving video/game scene, same single-monitor region,
resolution, encoder, quality, audio selections and local output drive. Close
other recording applications and keep the game's graphics/FPS settings fixed.
First compare with native cursor (no decorative overlay) to isolate capture.

| Run | Requested capture | FPS |
| --- | --- | --- |
| A | Compatible GDI | 30 |
| B | Compatible GDI | 60 |
| C | Modern experimental | 30 |
| D | Modern experimental | 60 |

After each run confirm actual capture and fallback in the main window. If C/D
fall back to GDI, label them GDI fallback; they cannot be used as DDA benchmark
results. Supply application log with approximate times. Compare stable progress
windows after startup, output cadence, speed, duplicates/drops and write waits.
Review both original recording and exported MP4 in an external player to
separate capture from preview/export/playback. No native GDI/DDA comparison or
measured performance gain is claimed from macOS synthetic tests.

## Presentation stick

Append PresentationStick (value 4) to preserve stored cursor values. The existing
selection and zh/en localization route the mode through recording/resume. Both
platform overlays draw a white outlined diagonal shaft down-right, red tip at
the pointer hotspot, click-through and without taking focus. No trail or drawing
mode is introduced. Windows pre-renders the static shape once and only moves
the overlay. macOS passes `--mode stick` to the existing AppKit helper. The
original system cursor is retained, as for existing decorative modes.

Manual checks: select stick; verify tip alignment, shape and normal clicking;
record on GDI and DDA; check recorded appearance; pause/change style/resume;
stop and confirm overlay removal; check negative-coordinate monitors and DPI.
Native appearance/capture on Windows and GUI appearance on macOS require manual
validation. Native helper compilation is not a visual acceptance result.

## Verification

Initial targeted RED: 4 failures, 1 pass (missing metrics, stick choice and
specific adapter fallback), `/tmp/opencam-performance-pointer-red.log`.
First GREEN: 40 pass, 1 native Windows skip,
`/tmp/opencam-performance-pointer-green.log`.
Integration initially exposed an invalid test assumption that direct-file output
has measurable bound-stream writes. Direct-file metrics are now explicitly null,
and a separate real-FFmpeg bound-stream test verifies all byte counts against
the actual destination. Integration GREEN: 9 passed, no failures/skips,
`/tmp/opencam-performance-integration-green.log`.
Summary script: two tests passed; AppKit helper compiled for arm64 macOS13.
Full Release solution build/test exited 0: Core255 + Media1002 passed, 20 skipped,
zero failures (1,257 passed total); Media duration 5m17s.
Log: `/tmp/opencam-performance-pointer-full.log`; SDK8.0.425, FFmpeg7.1.2.
Windows self-contained cross-publish exited 0 and x64 PE/source/license checks
passed. The local verification ZIP integrity passed, SHA256
`078cc33d2d6caa08485d7f5036b7fc5be0070f14c0c01265bdc6bcd7ddd5493d`.
Temporary source/package/build log: `/private/tmp/opencam-performance-pointer.HgNaEj`.
It is based on c737e6b421b31f3b1b9de5c9e1e98dbdf0714eec plus changes.patch,
not an Actions-built or native-Windows-verified release. Version remains0.3.12;
no commit/push/tag/Release mutation or schedule was performed for this task.
