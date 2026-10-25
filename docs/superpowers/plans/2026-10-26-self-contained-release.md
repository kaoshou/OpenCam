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
- Implemented owned media tools, app-local CRT, recursive source collection and release archive validation. Local website tests: 43 passed; script/player tests: 113 passed, 2 skipped. Windows owned-tool compilation and full managed verification are still pending; no release or merge has occurred.
- Final read-only review requested before integration. Official packages and actual source assembly must pass before publication; source policy approval alone does not satisfy this gate.
- Final read-only review found no confirmed Critical/Important issue. Fresh local full managed result: Core 358 passed, Media 1378 passed / 23 skipped / 0 failed. NuGet audit reported no known vulnerable packages. AppKit relocation test requires normal desktop execution and passed outside the filesystem sandbox; its sandbox SIGABRT was retained in the run log, not hidden by a test change.
- CI run 40214099390 compiled FFmpeg/ffprobe successfully but failed copying the incorrectly located `config.log`; the corrected source path is `ffbuild/config.log`. GCC 16 runtime license package names were verified against MSYS2's package recipe and captured via installed package manifests. Ubuntu prerequisite download exceeded its job budget while fetching optional speech data; bounded retries and `--no-install-recommends` were added without changing test selection.
- Fresh browser run: 23 checks passed (responsive layout, keyboard/accessibility checks, reading/transcript and offline output). CI run 40215712456 collected 23 exact Git sources and 9 pinned archives; the downloaded OBS bundle SHA-256 was independently verified. Its macOS package and managed checks passed. Windows results and official release assembly remain pending.
- Actual Windows native compilation, private CRT module-path check and native synthetic acceptance passed after CMake cache path normalization. Full owned Windows regression then caught missing PNG support (2 GIF export failures); static checksum-pinned zlib 1.3.2 and actual PNG encode/decode checks were added. Updated 7-input tools and packages must pass CI before release; the earlier 6-input source assembly is not the final release source result.
- Windows CI also exposed a heartbeat test reading JSON before a newly scheduled asynchronous durable save completed. Test-only event-based persistence checks replace fixed sleeps; preflight setup and trial each retain a distinct 20-second budget and bounded diagnostics. The underlying 21-second scheduling delay is not proven fixed. Local full Media: 1378 passed / 23 skipped / 0 failed; final focused rebuild: 8/8 passed. No production timing logic or recording fallback changed.
