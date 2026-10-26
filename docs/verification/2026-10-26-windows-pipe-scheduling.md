# Windows synchronous process-pipe scheduling mitigation

## Observed mechanism, not a claimed unique cause

Two independent owned-tool full regressions at `4af6ad1` failed trial/heartbeat
timing cases after the export child-lifetime repair; the separate prerequisite
gate passed. See `2026-10-26-windows-child-lifetime-fix.md`. The later Windows
run logged a checking engine producing 39 frames / 3.7 seconds while its preview
had no frame and a setup task remained pending for 20 seconds. Native clocks
advanced normally. These facts do not uniquely establish starvation or a GPU
fault.

[Isolated run 40260812356](https://github.com/kaoshou/OpenCam/actions/runs/40260812356)
characterized actual .NET 8 redirected anonymous pipes in separate diagnostic
processes. Only those children intentionally capped their pools at two workers;
the testhost and shipped application were not tuned. No private recording or
microphone was used.

| Windows image | Read strategy | Available workers | 100 ms timer completed before pipe release |
| --- | --- | --- | --- |
| 2022 | Stream.ReadAsync | 0 | No, after 1507 ms observation |
| 2022 | Dedicated synchronous readers | 2 | Yes, about 100 ms |
| 2025 | Stream.ReadAsync | 0 | No, after 1510 ms observation |
| 2025 | Dedicated synchronous readers | 2 | Yes, about 100 ms |

The elapsed JSON includes child shutdown, so 131/121 ms is not a precise timer
latency benchmark. This controlled experiment proves the blocking-pipe risk in
.NET 8, not that a normally configured user's computer has only two workers or
that every historical failure has the same cause. Microsoft documents the
[blocking .NET process-pipe implementation and later overlapped-I/O improvement](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/).

## Narrow mitigation

- Dedicated background threads drain Windows synchronous stdout/stderr for
  preflight audio and the legacy preview/trial engine and preview decoder.
- Preserve bounded buffers, complete-frame publication, async-only destination
  writes, write timing, native progress acknowledgement and error propagation.
- Readers do not own/close caller streams or terminate children. Existing owners
  stop/kill and join their own children before awaiting drain completion. Reader
  tasks complete only after their operation stops; no fire-and-forget copy is
  introduced.
- Async-only progress streams retain asynchronous reading. macOS and Linux
  continue their existing asynchronous I/O paths.
- No recording fallback, FPS/codec/audio/timestamp changes, timeout extensions,
  global ThreadPool tuning, test skips or physical-hardware PASS claims.

Regression-first: the actual anonymous-pipe test failed with a pool-backed reader
(1 failed / 1 passed). Dedicated-thread regression checks retain byte accuracy,
EOF completion, error identity, and caller handle/async-destination ownership.
Full Windows native/owned-tool and multi-image repetitions remain required before
accepting the scheduling mitigation. A green unit test alone is insufficient.

## Integrated results at `5ad9080d1c3c44a9fecf284eb36454935cd7d704`

[Default-setting matrix 40261755643](https://github.com/kaoshou/OpenCam/actions/runs/40261755643)
passed all four Windows 2022/2025 × owned/prerequisite groups. Each group passed
three focused rounds (34 each), Core 355 / 3 skipped, and Media 1238 / 132 skipped;
all failures were zero. In total 408 focused executions passed. The preceding
equivalent matrix `40260472537` failed in all four groups; its failures remain
recorded, not replaced by this result.

The actual native-helper/owned-tool Windows run `40261755652` also passed Core
355 / 3 skipped and Media 1239 / 131 skipped with zero failures, including the
synthetic native adapter enabled in that environment. Local macOS passed Core
358 and Media 1387 / 23 skipped. The formal Windows/Linux/macOS prerequisite
gates in `40261785957` passed. Script/player/CI tests passed 115 / 2 skipped.
Independent read-only review found no confirmed Critical/Important regression.
These results support the mitigation; they do not prove unique causation for
every prior delay or replace physical multi-minute recording acceptance.

The native Windows job subsequently failed **packaging**, not managed tests:
`assemble-third-party-notices.mjs` rejected the legitimate compiler notice paths
`licenses/ucrt64/share/licenses/libstdc++/COPYING.RUNTIME` and `COPYING3`.
The follow-up accepts literal `+` in the narrow filename whitelist without
allowing traversal, absolute paths, drive colons, backslashes or percent escapes.
Collector/verifier regression reproduced 5 passed / 1 failed before the change;
afterward the targeted collector/profile suite passed 10/10 and the complete
website suite passed 44/44. Notice bytes and hashes are preserved. Actual
corresponding-source assembly passed for the downloaded Windows/macOS/OBS
archives; full official packaging must still rerun successfully before release.
