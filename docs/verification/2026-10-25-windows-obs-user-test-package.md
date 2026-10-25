# Windows user-test package — 2026-10-25

User explicitly requested a Windows package to perform physical acceptance of the new recording core. Version remains0.4.1; no master merge, tag or Release was created.

## Scope

The normal assembler still emits a disabled runtime. A separate explicit `--enable-user-test-only` tool verifies its entire inventory before copying it to the user-test application and enables only that copy. Existing source runtime is not modified. Production C#/C++ recording implementation is unchanged from the preceding validated snapshot. Manual GDI/Desktop Duplication choices remain available; select Automatic to test the new core on a single monitor.

Build snapshot64a4e4dcc70a6d21a55ba08e28e1e146c3564d11 was pushed only to `codex/libobs-core-validation-20261025` from the separate validation checkout. [Run40145994003](https://github.com/kaoshou/OpenCam/actions/runs/40145994003) completed successfully on that exact SHA, all3 jobs green.

## Executed evidence

- New staging safeguards: RED before implementation, local8/8 green. Complete local packaging-script suite41 passed/1 gated skip. Windows staging safeguards7 passed/1 symlink-permission skip.
- Local rebuilt managed runtime tests11 passed/1 Windows package gate skip.
- Windows native helper/minimal DLL compiled, endpoint identity passed, packaging30 passed/3 Mac-only skips, native generated-media9 passed/1 Apple-only skip. Managed contracts39 passed/26 gated skips; portable macOS74 passed/1 Windows-package skip; Ubuntu66 passed/9 gated skips.
- Complete Windows self-contained application and existing compatibility helper compiled. Third-party notices and exact source/version checks passed.
- Crucially, **the actual enabled packaged runtime** was accepted by `ObsRuntime.TryLoad`, and actual packaged H.264/AAC was streamed to the existing owned output and decoded:2/2 passed,0 skips. Full TRX at `/tmp/opencam-obs-user-download.4ym7Vh/evidence/obs-results/packaged-managed/runneradmin_runnervmdaln5_2026-10-25_00_19_12.trx`.

## Download/integrity

Exact user-test artifact12581066515, size205297124 bytes, nonexpired and workflow_run.head_sha64a4e4dcc70a6d21a55ba08e28e1e146c3564d11. Raw Actions ZIP SHA256 matched GitHub API: `701dc46b51028a442e425c989d94fad1c711f21ad3af3b2b6e055a475c2d4907`.

Same-run evidence artifact12580856770 matched API SHA256 `a293c4b3c80affd452833c700a0bc56c3e1fe9b67702cf940c9942eb343c7312`. Its recorded inner ZIP SHA256 matched the downloaded inner ZIP: `1734a1dbcd4fbb4339e6c9ec3117227e4541655bf35d445e4b25bb11dad755fe`.

The inner ZIP was fully tested and extracted:653 safe entries, no compressed-data errors. All503 runtime files matched their SHA256 inventory; six critical executables/DLLs had valid AMD64 PE32+ headers. Enabled manifest and original disabled-manifest digests matched test metadata; source/version/notices passed the existing release-notice checker. This does not label the package as a Release.

Delivered file: `/Users/yhcheng/Documents/Codex/OpenCam-test-packages/OpenCam_v0.4.1_Windows_x64_OBS_Test_64a4e4d.zip`.

Full logs/raw artifacts/extraction: `/tmp/opencam-obs-user-download.4ym7Vh`. `README-TEST.zh-TW.txt` inside the package documents setup, short→5-minute testing and log collection. Physical desktop/audio synchronization, hardware-specific fluency and long-duration safety remain **REQUIRES MANUAL VALIDATION**; do not use this initial acceptance package for the only copy of an important recording.
