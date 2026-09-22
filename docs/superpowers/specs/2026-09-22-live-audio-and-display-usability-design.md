# OpenCam live audio and display usability design

## Purpose and constraints

Improve users' confidence about what OpenCam is recording without making the recorder less reliable. During a recording, the status panel must show separate, genuinely data-driven activity for system audio and microphone input. Before recording, users must be able to identify **every currently connected monitor**, not only two, and match its on-screen label to the monitor selected for capture. The custom-region frame must remain transparent while its instructions stay readable against arbitrary desktop content. Remove two unused PNGs from the repository root after verifying that no source, documentation, build script, or website references them.

Reliability outranks presentation. A meter or display-identification failure must not abort, slow, or alter a valid recording. Do not change the existing MKV → MP4, pause/resume, or Crash Recovery contracts. Keep Windows capture behavior intact. Do not publish a new version or Release as part of this change unless separately requested.

## Existing architecture and findings

- The UI and Recorder are separate processes. `MainViewModel` polls `GetTelemetry` every 500 ms; the telemetry currently carries file/time/health fields but no audio levels. Some audio health flags are placeholders and cannot be used as a waveform.
- macOS microphone and system-audio helpers write 16-bit PCM to private FIFOs read by FFmpeg. Windows system audio is produced by a WASAPI loopback callback. Windows microphone audio is read directly by FFmpeg DirectShow, so no equivalent PCM callback currently exists in the Recorder.
- Display enumeration already supports an arbitrary list. The selected monitor is identified by `MonitorInfo.Index`, but the UI does not visually identify the physical display.
- The custom-region interior is transparent; its center instructions are pale text without a contrasting background.
- `preview_enus.png` and `preview_zhtw.png` are tracked root-level files with no repository references. The website uses different files under `docs/images/`.

## Considered approaches

1. **Read-only capture-side metering (selected).** Compute compact audio-level summaries beside existing capture paths. macOS helpers publish bounded level messages from a background channel; Windows loopback exposes its already-captured PCM; a separate best-effort Windows microphone observer watches the uniquely matched input device. Existing recording inputs and FFmpeg arguments remain unchanged. If the observer cannot attach, show unavailable instead of guessing.
2. FFmpeg audio-statistics filters could observe the exact encoder input but would alter every audio filter graph, including pause/resume combinations, and could turn a display feature into a recording-start failure. This is not the default path.
3. An animated UI indicator based only on audio-enabled settings or placeholder health flags would be easy but misleading, so it is rejected.

For monitor identification, use short-lived numbered overlays on **all** active displays rather than only adding resolution text to the dropdown. Resolution and primary status remain supplemental information, not the sole identification mechanism.

## Audio-level contract and data flow

The Recorder owns audio-level snapshots. Each source has an explicit state: `Off`, `Live`, `Silent`, `Paused`, or `Unavailable`; a timestamp; and normalized RMS/peak levels in the inclusive range 0–1. The state is derived from requested source, current recording state, sample freshness, and observer health. `Live` means activity at the input, **not** proof that the finalized MP4 contains that sound. Stale levels (more than one second without fresh samples) become `Unavailable`, not a misleading zero. Audio samples, device names, and level history are not persisted to session metadata or logs.

The UI requests lightweight audio telemetry at no more than five times per second through a separate IPC command. This response does not probe files, disks, or FFmpeg. The existing 500 ms recording-health telemetry remains unchanged. Each source's waveform is a bounded history of recent level envelopes (roughly 20–30 bars), not an artificial loop or a sample-accurate oscilloscope trace. Level normalization and smoothing make normal speech visible while an idle source remains flat. The UI also exposes localized text labels, so color or animation is not the only signal. Disabled sources are marked off; paused sources are dimmed and do not imply live input. On resume, new samples replace stale history; on stop or process loss, the meters clear.

Audio monitoring must be observational and fail-open:

- macOS helpers compute a small RMS/peak summary after successful PCM conversion/write. A background, bounded-rate publisher sends prefixed lines over their existing control/stderr stream; the audio callback never waits to publish a level. The C# wrapper consumes these lines without filling ordinary logs.
- Windows system-audio loopback computes a level from the PCM already handed to its pipe. Meter work is bounded, cannot block a pipe write, and never modifies the bytes sent to FFmpeg.
- Windows microphone monitoring starts **after** the normal DirectShow recording path is established. A separate shared-mode observer matches the selected microphone to one unique Windows input endpoint; if matching or opening fails, only the meter is unavailable. It must never silently measure a different/default microphone, change the selected device, or interrupt FFmpeg.
- Monitor setup, reads, and cleanup are independently guarded; exceptions are logged at warning level without high-frequency spam. Stop/pause/source changes dispose or reset observers deterministically. No meter operation may be awaited on the capture callback's critical path.

## Multi-monitor identification

Place a localized `Identify displays` action next to the monitor selector. It is enabled only while idle and monitor mode is selected. On activation, take one fresh, ordered `IDisplayService.GetMonitors()` snapshot and show a brief, topmost label on **each** currently connected display: `1`, `2`, `3`, and so on for the full list. The label corresponding to the selected capture monitor is visually highlighted and includes a selected marker; primary status and resolution may appear as supplemental text. The action does not capture the desktop, store screenshots, or change the selection.

Map each capture-monitor index to the physical screen where the label is placed, accounting for negative desktop coordinates and per-monitor DPI. Do not assume enumeration order from two APIs is identical. Use stable geometry/native identity where available and test the mapping with same-sized displays, non-primary origins, and mixed scaling. If a mapping is ambiguous or a display disappears, omit that label and show a localized, nonblocking explanation rather than displaying a potentially wrong number. Close all label windows automatically after a few seconds, when recording preparation begins, when displays change, or when the app closes. Repeated clicks replace the current labels instead of stacking windows. No hard-coded two-monitor limit.

## Custom-region text contrast

Keep the full region interior transparent and the existing border, handles, move, resize, confirm, and cancel behavior. Give only the center instruction group a small dark, semi-opaque rounded backdrop with high-contrast white text. The instruction group must not intercept pointer input; users can still drag from that area. Avoid an opaque fill over the capture area. Both Traditional Chinese and English text must remain readable, including at the minimum selectable region size.

## File cleanup

Before deleting `preview_enus.png` and `preview_zhtw.png`, repeat an exact repository reference search and verify that both files are tracked. Delete only those two root-level files. Retain `docs/images/preview_main_*.png`, `docs/images/preview_settings_*.png`, and all website assets. A build/reference test guards against newly broken links.

## Verification and acceptance

- Automated tests: level math, clipping/normalization, silence and stale transitions, source combinations, pause/resume/stop reset, observer-failure isolation, IPC serialization, monitor mapping for 1/2/3+ screens with negative coordinates and mixed DPI, overlay lifetime, and region-text hit testing/appearance where testable.
- Build and run the existing .NET test suite on macOS. Do not claim Windows hardware behavior from a macOS run; retain Windows-specific automated tests and mark physical Windows microphone/multi-monitor checks as manual until performed on Windows.
- macOS manual smoke: record with microphone only, system audio only, both, and neither; verify the matching meter moves only when its source receives sound, a silent source stays flat, pause dims meters, and the saved media still has the intended audio. Repeat a normal stop and a short second recording to catch resource leakage.
- Multi-monitor manual smoke: identify all connected displays, including three or more if available; verify every on-screen number matches the dropdown and recording target. If fewer displays are physically available, report that limitation rather than claiming a 3+ hardware pass.
- Custom-region smoke: test dark and bright desktop backgrounds, minimum region size, move/resize from center and handles, and macOS transparency. Windows regression tests protect existing behavior.
- Final review must confirm only the two unused root images were deleted, no website screenshot broke, and recording/recovery code paths were not modified merely to animate the UI.
