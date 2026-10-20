# Direct preflight preview

## Implementation plan

User approved direct latest-frame preview, a quiet bilingual range hint, and accurate trial startup feedback. Reuse the existing platform capture input/selection and audio services, but send scaled RGBA directly to a bounded latest-frame sink for live checks. Do not send live pictures through H.264/MKV/a second decoder. Keep trial and formal recording outputs unchanged.

1. Demonstrate raw-output and pending-trial UI regressions against current code.
2. Add platform raw preview arguments preserving crop/cursor/capture selection and draining selected audio.
3. Assemble complete raw frames in bounded memory, retaining only the newest; request preview frames at 10 Hz without concurrent request queues.
4. Distinguish trial startup, acknowledged recording and finalization through session, coordinator, IPC and localized UI. Start five-second progress only after startup acknowledgement.
5. Run focused real FFmpeg, state, cancellation and UI tests, then the full Release suite. Record limitations and native acceptance steps.

## Native acceptance (required)

On Windows and macOS, compare a visible moving counter in the selected screen/custom region with the preview, with each of the four audio selections. Check cursor visibility, negative display origins and mixed DPI. Confirm preview stays near the newest picture during UI delays, and stopping/rechecking leaves no capture process. Press trial: startup message first, recording/progress only after actual start, finalization then replay. Check replay video/audio and subsequent formal output.

Synthetic/headless results cannot establish native screen latency or native audio correctness. No fixed latency or 30/60 FPS claim; preview is for confirming the range.

## Implementation

- The production live factory's explicit `livePreviewOutput` mode now requests the platform's direct preview command. It keeps the selected GDI/Desktop Duplication/AVFoundation input, crop, cursor settings and enabled input sources. FFmpeg remains the native capture host but emits raw RGBA, without H.264, MKV or a second decoder.
- Existing mapped audio is consumed by a null output (no saved file), so native input-level sampling remains available. Trial/formal output still uses the original recording builder and selected encoder. Encoder selection/probing is retained before input capture; a direct picture alone is not a full encoder recording test.
- Raw pictures are 960×540, aspect-fitted, at 10 FPS. The sink holds the latest complete immutable picture and a partial assembly buffer. It does not wait for UI readers or queue older complete pictures. UI keeps one request in flight and polls at 100 ms; transport/OS/load can reduce the actual display rate.
- Legacy encoded-stream adapters still use the existing decoder fallback; the production preflight factory explicitly selects direct mode.
- Appended `TrialStarting` and `TrialFinalizing` enum values preserve existing numeric values. Coordinator and IPC propagate the session's actual trial phase while preserving stop/failure precedence, operation identity and revision checks. The timer starts after the recording engine's startup acknowledgement.
- The range hint stays in the existing gray 12px bounds label and wraps naturally. Traditional Chinese and English startup, recording, finalization and audio-state text are localized.

## Evidence so far

- Original behavior: 8 real FFmpeg raw-output cases and 2 localized UI cases failed as intended (`/tmp/opencam-direct-preview-red.log`). The old output was encoded MKV; pending trial UI said recording immediately. An initial sandbox invocation could not create MSBuild pipes, was stopped, and was rerun with local-tool permission; that infrastructure failure is not RED evidence.
- First focused build/test: 61 passed, no skips or failures (`/tmp/opencam-direct-preview-green.log`), including four audio selections for both providers, actual decoded/displayable pixels and existing trial/replay/cancellation cases.
- Latest-frame fragmentation/immutability/disposal and real gated trial startup/finalization through coordinator+IPC: 10 passed (`/tmp/opencam-direct-preview-states.log`). The gate delays real FFmpeg startup/stop without inventing media progress or weakening the actual recording assertions.
- Full Release build/test: Core 249 passed, Media 867 passed / 20 skipped / 0 failed, exit 0 (`/tmp/opencam-direct-preview-full.log`, media 4m45s). SDK 8.0.425, FFmpeg 7.1.2 on macOS arm64. Existing xUnit/Avalonia warnings remain; earlier compilation also showed the existing nullable warnings.
- Final macOS filter extraction uses one display snapshot for both input selection and crop. After that small refactor plus the negative-origin/pinned-input test, final targeted build/test passed 69 cases / 0 skipped / 0 failed, exit 0 (`/tmp/opencam-direct-preview-final.log`). This is a focused final check, not a second full-suite run.
- `git diff --check` passed. Main checkout's pre-existing spec hash remains `25780034e07095597e0d8b6af9d8f3280b88d34e`. No version bump, push, tag or Release was performed.
