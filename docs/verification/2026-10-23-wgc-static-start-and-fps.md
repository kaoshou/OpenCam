# WGC static-desktop startup and recording FPS choices

## Evidence and scope

User log on Windows: WGC/NVENC previously started and recorded 338 frames.
Three later attempts stayed at frame=0 with a live FFmpeg process and only a
681-byte Matroska header until the unchanged five-second startup deadline.
The WASAPI broken-pipe errors followed forced termination. The user confirmed
that continuously dragging a window on the captured monitor permits startup
without switching from NVENC. This is evidence of sensitivity to incoming
desktop updates, not proof of an audio-device or unsupported-GPU failure.

The bundled FFmpeg identifies commit 29e619e767. Its NVENC encoder defaults
`delay` to INT_MAX, clamps async_depth to the surface count minus one, and
`output_ready()` requires enough ready/pending surfaces before releasing a
packet. The WGC source waits for capture frames rather than generating a
constant stream of duplicate desktop frames. Together these explain why the
old profile can withhold an initial frame while awaiting later desktop updates.

References (inspected upstream source, not copied into this repository):

- https://github.com/FFmpeg/FFmpeg/blob/29e619e767/libavcodec/nvenc_h264.c
- https://github.com/FFmpeg/FFmpeg/blob/29e619e767/libavcodec/nvenc.c
- https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavfilter/vsrc_gfxcapture_winrt.cpp

## Change

Only Windows native/WGC plus NVENC output receives `-delay 0 -bf 0
-rc-lookahead 0 -zerolatency 1`. This removes application-side asynchronous
frame buffering and requests no frame reordering/lookahead. Preset p4, CQ,
pixel format, selected audio, safe Matroska storage and cluster duration stay
unchanged. GDI/Desktop Duplication profiles and Mac capture are unchanged.
Potential cost: lower compression efficiency, larger files at comparable
quality, and less encoder pipelining. It is not a general throughput increase.
Other encoder-specific low-latency settings are not guessed or changed.

No startup deadline/positive-ACK/first-frame checks, identity protections,
capture target, cancellation, fallback or cleanup rules are relaxed. A process
or header alone still cannot acknowledge recording startup.

The recording selector offers 5/10/20/30/40 FPS, default 10. UserSettings alone
migrates old 60 FPS preferences to 30; RecordingConfiguration/session/project
metadata continues to read 60 unchanged. Existing media is not rewritten.
Chinese/English settings help, README, both guides and website home features
explain when high-performance capture is useful, platform differences and
Windows selection priority. Historical release notes are unchanged.

## Verification

New tests first: `/tmp/opencam-wgc-start-fps-red.log` exited 1 with the expected
missing NVENC delay options (four audio modes), retained 60 FPS menu choice,
and unmigrated preference failures. Focused implementation run exited 0:
Core 15 and Media 5 passed, no failures/skips, in
`/tmp/opencam-wgc-start-fps-green.log`.

Complete Release build/test results are retained in
`/tmp/opencam-wgc-start-fps-full.log`; website tests/build in
`/tmp/opencam-wgc-start-fps-website.log` and
`/tmp/opencam-wgc-start-fps-website-build.log`. Package verification is recorded
beside the delivered archive. Local host is macOS arm64, SDK 8.0.425 and
FFmpeg 7.1.2. Local assertions verify production-generated NVENC arguments,
not successful execution of Windows WGC/NVIDIA hardware.

## Required Windows manual validation

Prerequisites: Windows, NVIDIA NVENC, supplied package, same secondary monitor
and system-only audio configuration as the reported failure.

1. Enable high-performance capture and Auto/NVENC, 1920x1080 at 30 FPS.
2. Leave the captured display static; do not move the pointer or any window.
3. Start recording, wait 15 seconds, then play video for 15 seconds and stop.
4. Repeat with silence, audio playing, and pause/resume. Confirm the default
   start-only audio-confirmation mode does not reopen preflight on resume.
5. Play/export the result: inspect the first static section, the transition to
   motion, duration/audio sync and correct selected display.
6. Compare sustained motion with the old profile; log output FPS, speed,
   dropped frames and actual capture/encoder. Check for leftover processes.

Expected: startup acknowledges real encoded video without requiring desktop
movement; stop seals playable media and retains the selected capture target.
Failure: startup timeout, false recording indication, missing video, incorrect
timing/audio, unusable output or a remaining capture process.
Collect app log, performance entries and ffprobe metadata; no private pixels or
audio are required for diagnosis. Status remains REQUIRES MANUAL VALIDATION
until tested on the user's Windows hardware.
