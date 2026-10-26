# Windows startup and testhost isolation investigation

## First controlled matrix — completed

Actions [40255175052](https://github.com/kaoshou/OpenCam/actions/runs/40255175052),
source `7d9e3e77bd4b0e6f5b30e45a7948f95a1ec25046`. Same code and selected .NET 8
SDK policy; each hosted machine runs three fresh focused testhosts followed by
the complete solution. Every result is retained; a subsequent pass cannot erase
an earlier failure. No timeout, assertion, recording backend or source selection
was relaxed. No Release was created.

| Host | Tools | Focused rounds | Full Core | Full Media |
| --- | --- | --- | --- | --- |
| Windows 2022 | Owned distribution build | 25/25, three times | 355 passed, 3 skipped | 1229 passed, 132 skipped, 0 failed |
| Windows 2025 | Owned distribution build | 25/25, three times | 355 passed, 3 skipped | 1229 passed, 132 skipped, 0 failed |
| Windows 2022 | Old CI prerequisite build | 25/25, three times | 355 passed, 3 skipped | 1227 passed, 132 skipped, 2 failed |
| Windows 2025 | Old CI prerequisite build | 25/25, three times | 355 passed, 3 skipped | 1226 passed, 132 skipped, 3 failed |

The distribution build is the seven-input static build from the successful
producer job in run 40218510519, with PNG, MP4, GIF and complete decoding checks.
The comparison is the checksum-pinned BtbN September 30 prerequisite selected
by `install-windows-ffmpeg.ps1`; it is not the planned distribution binary.
Artifacts include exact executable hashes and SDK/runtime inventories.

All 40 separate-process synthetic audio controls completed with exit zero and
positive progress, approximately 23–65 ms total per invocation. These very short
tones are fresh-process controls after filesystem caches have warmed, not a real-time throughput or first-ack latency
measurement, and do not prove that a loaded testhost reads progress promptly.

Local macOS full solution, same source: Core 358 passed; Media 1378 passed,
23 skipped, zero failed, Media duration 6m28s. Evidence:
`/tmp/opencam-startup-diagnostics-macos-full.log` and TRX directory
`/tmp/opencam-startup-diagnostics-macos-evidence`.

## Failures and what is actually established

- Both prerequisite jobs: a hidden-pointer export test failed while deleting
  its synthetic `video.mkv` fixture (`IOException`, sharing violation). Existing
  `finally` cleanup can mask a preceding exception. It is not yet known whether
  export assertions passed, which process retained the handle, or whether this
  is an exporter, test-helper or external file-lock issue.
- Windows 2022 prerequisite: continuous heartbeat did not complete a durable
  save before the test deadline. An independent two-second canary remained
  pending after 5.047 and 10.039 seconds. Stopwatch, native GetTickCount64 and
  QueryUnbiasedInterruptTime agreed. The watchdog's first two-second wait resumed
  after 11.310 seconds. This is evidence of delayed timer completion/dispatch in
  the testhost, not evidence that only the awaiting test continuation was late.
- Windows 2025 prerequisite: preflight setup did not publish a preview frame
  before cancellation. Three clock observations agreed through 20 seconds.
  The phase case's canary completed normally at 2.026 seconds, but its engine
  startup/phase transition took about 20.787 seconds and exceeded the existing
  combined 25-second phase deadline later. A normal canary in that case does not
  establish normal progress-pipe delivery.

The original `StoppedCaptureCannotReadLevels` audio-only failure did not recur
in this matrix. All three focused rounds passed with both tool builds. The
matrix establishes CI/distribution tool drift and an observed association with
the prerequisite full-suite failures; it does **not** prove the old tools are
the sole cause. Earlier owned-tool runs also had timing failures and remain
part of the record. No clock defect, whole-pool starvation, or real OBS recording
failure is claimed as a proven root cause.

## Next controlled experiment

The diagnostic workflow can set the minimum worker count to 20 in generated
testhost runtimeconfig files only. This is an A/B observation, not a production
fix or a release-gate replacement. Runtime logs report the effective minimum.
The default control and modified diagnostic must use the same source revision;
all assertions, timeouts, three focused rounds and full-suite checks remain.
No packaged app configuration is changed.

The export fixture cleanup now records whether its assertions completed and
preserves both primary and cleanup exceptions instead of allowing cleanup to
hide the original error. The source file-lock owner is not yet identified.

Evidence downloaded under `/tmp/opencam-startup-matrix-evidence`; full comparison
job logs are `/tmp/opencam-startup-win2022-prerequisite.log` and
`/tmp/opencam-startup-win2025-prerequisite.log`. These are automated synthetic
tests, not physical WGC/WASAPI, device-removal or long-duration sync acceptance.

Reference: Microsoft documents both [thread-pool scheduling considerations](https://microsoft.github.io/vs-threading/docs/threadpool_starvation.html)
and [the redirected process-pipe improvement in .NET 11](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/).
They support investigating scheduling/pipe behavior; they do not prove this
incident's root cause or justify an untested runtime upgrade.

## Same-revision worker A/B — completed

Source `37aa85dba4cbb90204176f2a649b554f513d6015`:
[default control 40256806830](https://github.com/kaoshou/OpenCam/actions/runs/40256806830),
[minimum-20 diagnostic 40256816786](https://github.com/kaoshou/OpenCam/actions/runs/40256816786).
All 24 focused rounds passed 25/25. All Core runs passed 355 with 3 skips.
Full Media results (132 skipped in every case):

| Host / tools | Default workers | Diagnostic minimum 20 |
| --- | --- | --- |
| Windows 2022 / owned | 1227 passed, 2 failed (synthetic native pipeline, direct preview) | 1228 passed, 1 failed (export cleanup) |
| Windows 2025 / owned | 1229 passed, 0 failed | 1229 passed, 0 failed |
| Windows 2022 / prerequisite | 1227 passed, 2 failed (export cleanup) | 1229 passed, 0 failed |
| Windows 2025 / prerequisite | 1228 passed, 1 failed (trial) | 1229 passed, 0 failed |

The effective minimum of 20 is recorded by the running testhost. No timing case
failed in the minimum-20 runs, which supports further scheduling investigation;
this does not identify the exact blocked operation or prove a safe production
configuration change. The owned default Windows 2022 failure disproves any claim
that changing tool builds alone has fixed all timing failures. No production
minimum-thread setting was changed.

### Previously masked export defect now localized

The Windows 2022 owned/minimum-20 run retained both errors. The primary error is
`Win32Exception` from `WindowsProjectMediaProcess.BoundChild.StopAndJoin`, called
by `Dispose`; its reported native code was zero ("operation completed
successfully"). The subsequent fixture deletion fails because `video.mkv` is
still in use. Export assertions had **not** completed. This is not merely an
assertion-passing test whose final temporary-directory deletion was unlucky.

Source inspection identifies two concrete defects in that path:

1. After `TerminateProcess` fails, the next `Exited` poll invokes another native
   API before `Marshal.GetLastPInvokeError()` is read. The termination error can
   therefore be overwritten; the observed zero is not reliable evidence of the
   original native error. Its exact code remains unknown.
2. `BoundChild.Dispose` calls `StopAndJoin` before releasing streams, monitoring
   source handles and the process handle, with no `finally`. If joining throws,
   those owned handles are not disposed by that invocation. This explains why
   a cleanup failure can leave source access retained; the record does not
   identify every remaining handle owner or establish the precise native race.

Microsoft documents that [TerminateProcess is asynchronous and requires a process wait](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess).
A correct fix must capture the error immediately, retain a bounded exit wait,
always release owned resources, and still fail if termination is unconfirmed.
It must not swallow process errors or add an arbitrary sleep. No such fix is
claimed by these diagnostic commits; this is an identified Windows export
lifetime defect, separate from the unresolved preflight scheduling question.

Latest local same-revision verification: Core 358 passed; Media 1378 passed,
23 skipped, zero failed, 6m23s (`/tmp/opencam-startup-ab-macos-full.log`).
Script/player/CI tests: 115 passed, 2 skipped, zero failed
(`/tmp/opencam-startup-ab-node.log`); workflow actionlint passed. Read-only review
found no Critical/Important issue in the diagnostic changes and did not certify
the root cause or release readiness.

The diagnostic workflow is now manual-only to avoid automatically repeating
eight large jobs on future documentation pushes. Evidence remains in the linked
runs and `/tmp/opencam-ab-default-evidence` / `/tmp/opencam-ab-min20-evidence`.
No merge, tag or Release has occurred. Windows export lifetime repair and its
regression checks are required before claiming release readiness.
