# Self-contained 1.0.0 publication implementation plan

> Use test-driven development and verification-before-completion. User approved autonomous completion through publication; no further routine confirmation.

Goal: verified, easy-to-use Windows/macOS Release packages with actual corresponding sources and accurate notices.

## Global constraints

- Keep OBS/libobs NativeOnly recording, audio correctness and original recording preservation. FFmpeg remains a media tool, not capture fallback.
- Version 1.0.0. Preserve existing dirty checkouts; continue isolated validation clone.
- Portable must carry its own required runtime; installer must not require users to find DLLs. macOS system privacy consent remains unavoidable.
- Source archives must describe actual binaries, not header pins. Do not hide skips or claim physical hardware acceptance from synthetic CI.

## Tasks

1. Owned Windows FFmpeg: hash-pinned source inputs, static x264/AV1 decode and retained GPU encoder probes, built-in editing/audio/GIF filters. Preserve source archives, build configuration, runtime license exceptions and real executable capability/output checks. Test missing capabilities and wrong hashes before implementation.
2. App-local Microsoft CRT: authenticated x64 VS redistributables, complete PE closure beside helper/probes, recorded licenses and source versions. Manifest includes all bytes; system DLLs cannot mask missing packaged CRT. Remove mandatory SystemDirectory prerequisite. Regression tests cover missing/tampered files.
3. OBS source delivery (independent source domain): verified recipe revisions, archive hashes, recursive gitlink coverage, full patches/recipes and actual binary/source version inventory corrections. Agent owns collector/lock/tests/OBS source notices; main owns workflow integration.
4. Publication: source delivery manifest and source artifact hashes, resumable immutable upload, formal metadata/installer upgrade cleanup, final review and full suites, merge/push, build both platforms, inspect official artifacts, publish immutable v1.0.0 and then update website downloads.

## Review focus

- Clean Windows must not depend on the hosted runner's system VC runtime.
- Owned media tools must retain NVENC/QSV/AMF probing, imported-media decode, edit filters, MP4/GIF, private pipes and audio trials.
- Missing/corrupt dependency/source artifact must block distribution; source manifests must not silently omit git submodules.
- macOS signing must not alter inventoried private runtime files.
- Existing Release assets must be accepted only on matching digest, never silently replaced; website links advance only after all assets exist.

## Progress

- User explicitly approved controlled Windows FFmpeg distribution and all necessary release completion work.
- Main implements Windows tools/runtime/publication; independent source agent implements collector/OBS inventory. No overlapping files.
