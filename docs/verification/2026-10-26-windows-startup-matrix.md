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
