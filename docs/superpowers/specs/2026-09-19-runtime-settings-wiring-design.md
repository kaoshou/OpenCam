# Runtime Settings Wiring Design

## Goal

Make the video quality, disk guard thresholds, and post-remux working-file cleanup settings affect each recording session on Windows and macOS while preserving existing defaults and recovery safety.

## Confirmed root cause

The settings page persists all three settings in `UserSettings`, but `MainViewModel.StartRecordingAsync` does not copy them into `RecordingConfiguration`. Windows also hard-codes quality 23, macOS always uses the default 6000 kbps, and the recorder's disk monitor remains at its constructor defaults. The cleanup branch already exists and is correctly gated by a successful remux and media probe, but its flag is never sent by the UI.

## Design

### Video quality

`RecordingConfiguration` carries the stable preset name (`Ultra`, `Standard`, or `Compact`) and exposes normalized quality values. Unknown or empty values fall back to `Standard` for compatibility with older settings files and IPC clients.

- Ultra: quality value 18; VideoToolbox bitrate 10000 kbps.
- Standard: quality value 23; VideoToolbox bitrate 6000 kbps.
- Compact: quality value 28; VideoToolbox bitrate 3500 kbps.

Windows passes the quality value to libx264 CRF, NVENC CQ, QSV global quality, and AMF QP. macOS libx264 uses CRF; VideoToolbox keeps reliable bitrate control and uses the preset's bitrate. UI wording describes a general encoding quality preset rather than claiming every encoder uses CRF.

### Disk guard

`RecordingConfiguration` carries warning and critical thresholds in bytes. The UI converts its GB and MB settings when constructing the session configuration. Before monitoring starts, the recorder applies positive values to `IDiskSpaceMonitor`; invalid values fall back to the existing 2 GB / 500 MB defaults. The UI compares telemetry free space with the warning threshold and presents a localized low-space status. The existing critical event continues to stop and finalize safely. The existing fixed 1 GB preflight requirement remains unchanged.

### Working-file cleanup

The UI copies `DeleteWorkingFileAfterSuccessfulRemux` into `RecordingConfiguration`. The existing recorder behavior remains authoritative: segment MKV files are deleted only after MP4 creation and successful ffprobe validation. Any remux or validation failure preserves source segments for recovery.

## Compatibility and safety

- Existing JSON settings and IPC payloads retain safe defaults when new values are absent.
- Paused-session updates still change only audio and cursor behavior.
- No Windows-only or macOS-only capture path is replaced.
- No version bump, push, or release is part of this change.

## Verification

- Unit tests cover preset normalization and Windows/macOS encoder arguments.
- UI logic tests cover construction of runtime configuration and low-disk warning state.
- Recorder tests cover threshold application and cleanup-flag propagation through session configuration.
- Full solution tests and builds run with `RollForward=Major` because this Mac has .NET 10 runtime while the solution targets net8.0.
