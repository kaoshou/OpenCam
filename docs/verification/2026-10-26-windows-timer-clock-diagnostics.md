# Windows managed timer investigation

Formal run `40219435575`, source `9db7511b63a4f2587009df9eebf512b9e4e48b09`,
failed Windows Media verification: 1,227 passed, 132 skipped, two failed. Core
passed 355 with three skipped. This run does not authorize a Windows release.

The failed cases were:

- `PreflightTrialPhaseTests.IpcReportsStartingUntilEngineAcknowledgesAndFinalizingUntilStopCompletes`:
  cancellation in `Until(Reviewing)` at the existing 25-second deadline.
- `ActualRecordingIntegrationTests.SessionHeartbeat_TransientStoreFailure_RetriesOnNextTick(0)`:
  the first two-second watchdog await resumed after 23,175.9 ms. Only then did
  the zero-delay injected store failure execute; the 20-second retry wait expired.

The stack sample shows the timer thread waiting and multiple idle pool workers.
Synthetic FFmpeg continues to report approximately 30 fps. These samples do not
prove whole-pool starvation, a blocked UI context, a clock defect, or which moment
the watchdog delay promise actually completed. There is no known root cause yet.

Evidence: `/tmp/opencam-formal-windows-gate2.log` and
`/tmp/opencam-final9db-windows-evidence/environment.json`. The formal runner is
Windows Server 2025, image `win25-vs2026`, version `20261021.278.1`. The selected
CLI is SDK 10.0.402; installed SDKs include 8.0.426 and installed .NET 8 runtimes
include 8.0.6, 8.0.22 and 8.0.32. SDK selection alone does not identify a timer
cause. The next diagnostic logs the actual running framework from testhost.

## Hypothesis and diagnostic scope

One hypothesis is disagreement between the clock driving managed timers and
the clock measuring elapsed time. Official
[.NET 8.0.32 Windows timer source](https://github.com/dotnet/runtime/blob/v8.0.32/src/libraries/System.Private.CoreLib/src/System/Threading/TimerQueue.Windows.cs)
uses `QueryUnbiasedInterruptTime`. Its
[portable timer loop](https://github.com/dotnet/runtime/blob/v8.0.32/src/libraries/System.Private.CoreLib/src/System/Threading/TimerQueue.Portable.cs)
compares deadlines using that clock and queues expired work to the pool.
[dotnet/runtime#82944](https://github.com/dotnet/runtime/issues/82944)
documents that this clock can be affected by timer-resolution changes; it does
not establish the cause of the observed 23-second delay.

The test-only independent observer now compares Stopwatch, native
`GetTickCount64` and `QueryUnbiasedInterruptTime` elapsed values. On Windows,
when the existing stack observer is enabled, one explicitly labeled two-second
canary reports its own task status without awaiting it in the test. A completed
canary with normal clock deltas argues against a global timer-clock stall at
that point; a pending canary with divergent clocks supports further clock
investigation. A pending canary with agreeing clocks still does not distinguish
timer dispatch from pool dispatch. The canary is not the watchdog's delay, and
adding one timer can perturb the queue being investigated.

The phase test records clock deltas at its setup, start gate, recording, stop
gate, finalization and review boundaries, including failure before cleanup.
Existing bounded logging and independent-thread disposal remain in place.
The 25-second phase deadline, 20-second retry deadline, fault injection and all
assertions remain unchanged. No production, workflow, platform or thread-pool
configuration is changed by this diagnostic patch.

## Local verification

Focused Release build/test passed all six cases, zero skipped/failed, in 22.3
seconds: both heartbeat fault-delay cases, the phase test, two independent-probe
cases and the bounded collector case. Output is recorded in
`/tmp/opencam-timer-clock-diagnostics-focused.log`. Native Windows clock calls
and the CI-only canary still require the next Windows run; macOS cannot verify
those paths or resolve the Windows root cause.
