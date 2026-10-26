# Windows export child lifetime repair

## Evidence and scope

The preceding matrix retained the original `BoundChild.StopAndJoin` exception
before the source-file cleanup error. The termination error could be overwritten
by the following native exit poll, and a thrown join skipped `Dispose`'s stream,
monitoring-handle and process-handle release. See
`2026-10-26-windows-startup-matrix.md` for the actual Windows run and limitations.

This change addresses that export-process lifetime path only. It does not change
OBS recording, capture selection, timestamps, audio mixing, the five-second exit
wait, or ThreadPool configuration. It is not a claim to fix preflight scheduling.

## Regression before implementation

The existing lifetime behavior was moved unchanged behind a small internal OS
boundary, supplied with native-operation delegates by `BoundChild`. This permits
deterministic race/error testing without terminating unrelated real processes.
Real owned FileStreams test cleanup rather than checking mock disposal counters.

Before the fix: **5 failed / 1 passed**, exit 1, recorded at
`/tmp/opencam-child-lifetime-red.log`. Failures demonstrate:

- termination denial rejects a child that subsequently confirms exit;
- another native call overwrites the original termination error with zero;
- a failed wait loses its native error;
- a join failure leaks an owned file handle;
- join/disposal errors prevent release of other resources and hide errors.

The existing successful-termination-but-unconfirmed-exit case still fails closed
and passed before the repair.

## Repair

- Read the termination error immediately, before another native call.
- Always use the existing bounded five-second wait to confirm exit, including
  concurrent natural exit/termination. A failed termination request alone does
  not fail an already-confirmed exit; an unconfirmed exit still fails.
- Preserve native wait errors distinctly from termination errors and timeouts.
- Attempt release of every owned resource even when joining or another release
  fails. Rethrow one original error with its stack, or aggregate multiple errors.

Focused local Release build/test: **15 passed / 0 skipped / 0 failed** (the six
new lifetime cases and nine actual macOS pointer-export cases), exit 0,
`/tmp/opencam-child-lifetime-green.log`. Native Windows and full-solution
regression results are pending at this checkpoint; policy tests on macOS do not
replace Windows native execution. No Release is created by this change.

## Full regression at `4af6ad192d4b926cc686f303a61d063ae0397767`

The build-only workflow [40258814773](https://github.com/kaoshou/OpenCam/actions/runs/40258814773)
completed all three verification jobs successfully:

| Platform | Core passed / skipped | Media passed / skipped | Failed |
| --- | --- | --- | --- |
| Windows | 355 / 3 | 1235 / 132 | 0 |
| macOS | 358 / 0 | 1384 / 23 | 0 |
| Linux | 358 / 0 | 1242 / 111 | 0 |

Local macOS full regression also passed (Core 358; Media 1384, 23 skipped),
and the script/player/CI suite passed 115 with 2 skips. Workflow syntax and
`git diff --check` passed. macOS CI additionally passed native audio-helper
tests and ScreenCaptureKit compilation/argument checks.

This run did not reproduce the preceding preflight scheduling failures. The
lifetime repair is not evidence that their independent cause has been fixed.
Required physical-device and long-duration scenarios are not implied by these
automated results. The separate owned-tool/native Windows regression and final
official package checks remain pending at this checkpoint.

The separate [owned-tool run 40258789624](https://github.com/kaoshou/OpenCam/actions/runs/40258789624)
subsequently failed three `PreflightTrialTests` cases (Media 1233 passed,
131 skipped, 3 failed). The export cleanup failures did not recur. The failing
cases were microphone-only sample polling, portable-pipe replay, and trial
cancellation. The first failure was obscured by an `Assert.ThrowsAny` in its
`finally`: the trial was already complete when cleanup expected cancellation.
The other two timed out while waiting for a checking frame or replay completion.
These failures still block publication; passing the preceding gate does not
erase them. Follow-up test-only diagnostics retain the primary error and report
the active stage; native stack observation is also enabled in the owned-tool
full test step. Local focused trial tests passed 9/9 after these diagnostic-only
changes. No production timeouts, recording paths or pool settings changed.
