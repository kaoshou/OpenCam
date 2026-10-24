# Mature recording core integration

The user approved implementing the recommended mature-core approach on 2026-10-25, without further questions, provided current functionality is preserved. Version stays 0.4.1. This is not authorization to publish or merge.

## Decision and alternatives

Use a pinned libobs runtime in a separate, narrowly scoped helper. Preserve Avalonia UI, recorder ownership, immutable project sources, MKV recovery, pause/resume as segments, preflight, direct teaching-pointer decoration, editor, MP4/GIF and web exports. Continuing CLI tuning alone does not provide capture timestamps; replacing all platform capture and clock code with our own implementation would create greater maintenance risk.

The helper owns capture, clocked mixing and encoding; libavformat writes MKV to stdout through custom AVIO. The parent copies into its already exclusively opened output stream. No helper receives a destination file path, no reopening user files, no temporary untracked recording. Diagnostics only go to stderr. No device capture occurs in synthetic tests.

## Contract

- Helper invocation: `OpenCam.ObsCapture --runtime ABSOLUTE_RUNTIME_ROOT`; stdin first line is bounded UTF-8 JSON (max 64 KiB), then `STOP` or EOF. Stdout contains only binary MKV. Stderr protocol lines start `OCOBS ` followed by JSON.
- Request v1: `protocol`, `synthetic`, `fps`, `width`, `height`, `x`, `y`, `monitor` (native identity), `monitorWidth`, `monitorHeight`, `cropX`, `cropY`, `audio` (`none`, `system`, `microphone`, `both`, `silence`), `microphone`, `systemDevice`, `cursor` boolean, `encoder` (`cpu`, `nvenc`, `qsv`, `amf`, `apple`), `quality` (18/23/28). Inputs are validated before starting devices. No arbitrary paths or module names in the request.
- Events: `{"event":"ready","protocol":1,"backend":"wgc|dxgi|sck|synthetic","encoder":"cpu|nvenc|qsv|amf|apple"}`, `{"event":"progress","frames":N,"mediaUs":N,"bytes":N,"systemPeak":0..1,"microphonePeak":0..1}`, `{"event":"stopped"}` or `{"event":"error","code":"fixed_identifier"}`. Ready requires actual source geometry and muxed video, not merely output activation; selected audio requires real sample callbacks, silence counts as samples.
- Runtime contains pinned modules only, no user/system plugin discovery. Build manifest gates platform, protocol, source revision and file digests. Missing or unsupported runtime leaves the existing path available. A resumed segment pins its recording core; never fall back mid-session to a different core or device.
- Unsupported topology/device mappings retain existing functionality through explicit pre-recording fallback. Never guess a DirectShow microphone name corresponds to a WASAPI endpoint. Windows native module resolves exact endpoint identity, not first-device selection.
- Capture stays same resolution/FPS/quality requested. Bounded output buffering, no catch-up frame bursts or synthetic motion claims. Optional pointer failures cannot restart recording.

## Safety / acceptance

Real libobs synthetic integration must verify H.264/AAC MKV decodes, expected geometry, changing frames, audio/video timing and recoverability of already written bytes on forced termination. Parent tests cover malformed events, early EOF, cancellation, output failures, stale runtime, no overwrites, source-loss signaling and stopping. Full existing regression suite remains required. Hardware capture, hybrid GPU/DPI, device unplug and long-duration sync are separately reported, never inferred from synthetic PASS. A validated runtime is not enabled by mere presence of DLLs.

## Consequences

Additional native runtime size/build/notice obligations; dedicated helper preserves process isolation. No OBS UI, browser, streaming, game injection or third-party plugin loading. No existing package, recording, project, setting or Release is overwritten during development. New runtime deployment remains staged until its acceptance gates are evidenced.
