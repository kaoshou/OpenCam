# Window height and normalized-audio export

Local branch: `codex/normalized-audio-export`, based on `dc56f3456aa55ff123b28e6f2e5bc615b9192a0a`.
No merge, push, version change, CI dispatch or release is part of this work.

## Changes

- Home default client size is 840 × 820 logical pixels. Initial height is bounded by the current screen work area, scale and frame decorations. Existing settings scrolling remains available; subsequent manual resizing is not overridden.
- Compatible, complete multi-segment recordings needing audio normalization copy the encoded H.264 video. Each clip's audio is normalized into exact PCM samples, then AAC is encoded once. This avoids re-encoding video; it does not promise instantaneous export.
- Original MKVs and saved project contents are unchanged. Temporary PCM and video staging require free disk space and are removed when finished or cancelled. This is transient export work, not persistent pre-processing of edited sources.
- Publication follows verification. Packet payload hashes and source-relative presentation offsets are checked per segment using bounded-memory streaming. Existing packet counts, monotonic DTS, boundaries, decode checkpoints and audio coverage checks remain.
- Only recognized remux incompatibility triggers rendering fallback. Cancellation and ordinary I/O failures do not silently retry by rendering.
- Home/editor progress distinguishes inspection, copying, audio conversion, rendering and verification in Chinese and English.

## Evidence

- Baseline Release: Core 205 passed; Media 658 passed, 19 skipped.
- TDD: old window height was 720 (expected 820); old render route changed encoded video payloads; old status showed one label for five phases. Each failed before its implementation.
- Review-driven negative test: changed video timing evidence initially passed verification; it now must be rejected.
- Real FFmpeg fixtures cover 2/3/10/100 segments, B-frames, variable frame timing, delayed audio, non-frame-aligned audio endings, impulses at both sides of joins, cancellation and injected final-probe failures. Tests compare encoded payloads, per-packet PTS/DTS, decoded audio sample coverage and impulse positions, not only container duration.
- Cancellation and injected I/O failure preserve original hashes, leave no published output or owned intermediate files, and do not invoke rendering. Injected incompatibility exercises verified full-render fallback.
- Final `dotnet test ScreenRecorder.sln -c Release --no-restore`: Core **205 passed**, Media **677 passed / 19 skipped**, **0 failed**, exit 0. Media elapsed 2m14s. Logs: `/tmp/opencam-final-regression2.log`.
- Final `dotnet build ScreenRecorder.sln -c Release --no-restore`: exit 0, **0 warnings / 0 errors**. Log: `/tmp/opencam-final-build.log`. `git diff --check` passed.
- Headless Chinese and English home tests confirm the bottom recording preference fits the settings viewport at the new default height. This does not replace native DPI/visual acceptance.

## Remaining manual acceptance

REQUIRES MANUAL VALIDATION:

- Native macOS screenshot launch failed at Avalonia `RenderTimer -6661`, before displaying the window. No visual acceptance is claimed.
- Windows native UI/DPI and real pause/resume recordings have not been tested in this local Mac run.
- Actual disk exhaustion during PCM writing was not induced; the injected I/O test is not equivalent to a disk-full hardware/environment test.
- No performance benchmark or long-duration real-device A/V acceptance is claimed.

On Windows/macOS: check project-empty/open home at normal and high DPI, verify bottom settings are visible or reachable by scrolling, and compare single recording plus repeated pause/resume exports against the saved MKVs. Confirm output phase, sound around every join, cancellation cleanup, and edited/incompatible material still rendering safely.
