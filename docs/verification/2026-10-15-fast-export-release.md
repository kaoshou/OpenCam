# OpenCam 0.3.6 release preparation

## Changes

Missing H.264 packet durations can use the following strictly increasing PTS. A missing final duration requires the preceding interval to agree with frame rate/time base within one source tick. One pending packet per stream keeps packet memory bounded. Missing audio durations, reordered/duplicate PTS and ambiguous tails retain render fallback; output verification is unchanged. Bilingual export labels clarify the existing phases.

## Executed checks

- macOS arm64, .NET SDK8.0.425, FFmpeg7.1.2.
- `dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false`: Core230 and Media746 passed,20 skipped,0 failures; includes Release compilation. Log: `/tmp/opencam-v036-full.log`.
- Missing-duration regression: original rejection produced6 failures (two parser cases and four real single/multiple, audio/no-audio exports); restored inference passed. Logs: `/tmp/opencam-fast-duration-real-red.log`, `/tmp/opencam-fast-duration-final.log` (62 related tests passed).
- Native export fixtures remove only optional source video duration probe fields; real packet data, PTS/DTS, audio and output verification remain intact. Assert no Rendering phase, equal encoded H.264 bytes, complete frame count, audio sample bounds and unchanged source hashes.
- Website30 and player23 tests passed; website build, version consistency0.3.6 and diff whitespace check passed. Initial website failure was a stale guide version; both guides were updated without changing assertions. Logs: `/tmp/opencam-v036-website-final.log`, `/tmp/opencam-v036-website-build.log`, `/tmp/opencam-v036-player.log`.
- Build retains existing nullable/NU1900 warnings; online NuGet audit is not claimed passed by this local run.

## Remaining acceptance

GitHub Actions cross-platform tests, packaged artifact verification and publication remain pending at this preparation commit. Native Windows/long A/V, macOS visual, real disk exhaustion, browser offline/touch/fullscreen and full physical Release Gate remain incomplete. No trusted signing/notarization or completed Windows FFmpeg corresponding-source verification is claimed. Historical Windows heartbeat/fast-export intermittent causes are not all proven resolved. No device-wide speedup benchmark is claimed.

Keep original unrelated dirty documents and the development worktree. Existing screenshot assets remain explicitly labeled0.3.5; no private user desktop or new Web screenshot is published.
