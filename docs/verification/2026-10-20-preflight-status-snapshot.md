# Preflight status snapshot during failed-start cleanup

## Evidence and scope

Run 39311386860 at 8bb7a95981c3c2f9d638f3de5a7d7df7ac566703: portable verification failed only `FailedInitializationLogsOriginalExceptionAndKeepsStartBlocked` at the initial `response.Success` assertion (line 128), before its 3-second failure-state wait. Core passed 249, Media passed 770 / skipped 73 / failed 1. The response error was not included by the old assertion, so its exact hosted text is unavailable. `/tmp/opencam-v039-portable-diagnostics-ci.log` records the individual failure. macOS verification passed; Windows verification was still running during investigation. No installer from this run is eligible for publication.

Code tracing found a confirmed race on this response path: `ReadCurrentStatus` reads `_current`, then calls `ReadStatus`, which rereads `_current` through `Require`. Failed initialization can finish cleanup and clear `_current` between those two reads. This can turn an internal current-status snapshot into `Preflight identity changed`, which the dispatcher returns as `Success=false`. A new concurrent polling regression with 2,000 real coordinator failed-start lifecycles reproduced that exception against the old implementation, retaining up to 16 failures in 244ms: `/tmp/opencam-v039-status-snapshot-red.log`. This demonstrates the race without claiming that the missing hosted response text proves it was the only possible cause.

## Minimal fix

Build current-status snapshots from the same captured operation reference. Explicit operation/revision reads still call `Require` before using the shared snapshot builder, and explicit reads after cleanup remain rejected. Capture leases, start/stop, cancellation, IPC identity checks, media access and confirmation readiness are unchanged. Do not catch or hide session read errors. Add the response's error message to the original assertion without changing its expected success.

This fix is separate from the prior Windows trial-level timeout; it does not claim to fix that timeout or historical heartbeat scheduling issues. Preserve the existing bounded trial diagnostics and original deadlines.

## Verification

- Targeted Release build/tests: 13 passed, no skips/failures, covering the new race plus existing diagnostic, IPC identity and lease cases: `/tmp/opencam-v039-status-snapshot-green.log`.
- Final solution Release build/test exited 0: Core 249 passed; Media 853 passed / 20 skipped / 0 failed, taking 4m31s. Total 1,102 passed. `/tmp/opencam-v039-snapshot-full.log`.
- Version remains 0.3.9; diff whitespace checks passed. No new application dependencies or project format. Prior native/physical Release Gate limitations remain. New hosted verification at the exact final commit is required before release.
