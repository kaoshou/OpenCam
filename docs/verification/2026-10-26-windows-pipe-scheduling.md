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
