# 0.3.9 Windows heartbeat fault-delay investigation

## Failed hosted run

- Source: `e7ff136c947d2079e46eaa7b140bb67c5c1e60eb`, Actions `39313494701`.
- Portable and macOS verification, macOS packaging and same-source Pages passed. Windows packaging was skipped; no installer from this run is eligible for publication.
- Windows Core: 246 passed, 3 skipped. Media: 795 passed, 71 skipped, **1 failed**: `SessionHeartbeat_TransientStoreFailure_RetriesOnNextTick(failureDelayMs: 3000)`.
- Full failed-step log: `/tmp/opencam-v039-snapshot-windows-failed.log` (604 lines). Raw Windows TRX/environment ZIP: `/tmp/opencam-v039-snapshot-windows-evidence.zip`, SHA256 `da8c2e85430764aebdc050c3284466fb1ac4a78cb593b25d0e17106f282a3cbb`, matching artifact `12217955980`.

The first watchdog continuation was normal: queued-to-entered 0.1019ms and first 2-second delay 2011.8633ms. The faulting save entered at `19:51:03.6683373Z`; its injected three-second `Task.Delay` did not reach the throw until `19:51:26.9076773Z` (about 23.24 seconds). The 20-second retry wait expired while recording remained active. This is **not** evidence that the retry catch branch stopped working, nor another late first-watchdog-timer failure.

Both managed-stack chunks were read. At observation the pool reported 8 workers, 32763 available and 1 pending. Stacks include test-runner waits/socket polling, an Avalonia headless queue wait, several idle pool workers, and one `RandomAccess.ReadAtOffsetAsync` / `AsyncOverSyncWithIoCancellation` / `ReadFile` worker. The diagnostic reader itself has a separate synchronous `ReadFile` stack. There is no `DriveInfo` stack or stack showing all workers blocked in pipe I/O. A sampled `ReadFile` alone does not establish the cause of the delay, which handle it was reading, or whole-pool starvation.

## Narrow diagnostic change

No product code is changed. The test-only `IndependentDelayProbe` samples the existing fault-delay task on a dedicated thread without awaiting it, scheduling a continuation, completing it, or cancelling it. It reports task status, monotonic elapsed time, tick elapsed time and pool counters at completion or five-second observation boundaries, stopping after 20 seconds or joined disposal. The existing store trace timestamps correlate these samples with the actual `await` resumption. Stack-tool start/end timestamps distinguish collection duration from the delay being investigated. No arguments, environment dump, pixels or audio are collected.

Hypotheses still to distinguish on Windows:

1. The delay promise itself has not completed at the expected observation point.
2. The promise completed, but its awaiting continuation has not resumed.
3. A VM/host scheduling pause or diagnostic-tool perturbation affects the timeline.

The original 2-second watchdog interval, injected three-second fault, 20-second retry deadline, 2500ms continuous-heartbeat checks, durable JSON readback, active-recording assertion and stop cleanup remain unchanged. No `MinThreads`, timeout, test filter or safety gate changes. This is evidence collection, **not a root-cause fix**.

## Local verification

The probe regression detects missing independent observation: an empty probe produced 1 failed / 1 passed (`/tmp/opencam-v039-delay-probe-red.log`), failing with `Independent observer did not report task completion.` The companion case uses joined disposal to establish that a pending observer cannot later emit reports.

Verification results are recorded after commands finish; local macOS results do not constitute Windows timer/helper or native recording acceptance. Existing physical Release Gate and platform limitations remain in the bilingual release notes. No tag, draft or public 0.3.9 Release has been created.

## Independent local Core failure and test-process isolation

The first full command `/tmp/opencam-v039-delay-observation-full.log` reported Core 248 passed / 1 failed: `ProjectBookmarkTests.SavedBookmarksSurviveReopenWithoutChangingManifest`, immediate reopen after the owner was disposed, with `Recovery already in progress`. The five bookmark tests passed in isolation (`/tmp/opencam-v039-bookmark-lock-investigation.log`); that alone was not treated as a fix.

A bounded five-second A/B characterization used the actual `BoundDirectory.Claim` implementation on a task-owned temporary directory, repeatedly acquiring/disposing the lease. Control: **362747 claims, zero failures**. With a separate thread actually starting `/usr/bin/true`: **294444 claims, 340 child starts, 390 rejected claims**. First three errors were the same `Recovery already in progress`. Source and complete output: `/tmp/opencam-v039-lock-probe/` and `/tmp/opencam-v039-lock-characterization.log`. This reproduces cross-thread process-creation interference without bookmarks, async JSON or our new heartbeat diagnostic. It does not establish that locks were permanently leaked.

Core's `ProjectStoreTests` owns the process-spawning probes and previously ran alongside unrelated lease/reopen tests. It now belongs to an xUnit collection with `DisableParallelization = true`, so these process-creation cases cannot overlap the other collections. This is **not** a blanket removal of real process tests or a sleep/retry workaround. Live-writer exclusion, post-close reopen, forced writer kill/recovery, revision checks and all original assertions remain. No native lock, project format, source path safety or product lifetime is modified.

This isolates a confirmed shared-testhost side effect; it does not claim that arbitrary application process creation can never transiently affect a Unix lease. The product continues to fail closed rather than steal an owned lease.

The mechanism is consistent with Apple's documented [inherited `flock` / last-close semantics](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fcntl.2.html) and [close-on-exec behavior](https://developer.apple.com/library/archive/documentation/Security/Conceptual/SecureCodingGuide/Articles/AccessControl.html): close-on-exec does not turn process creation into an atomic boundary relative to another thread closing and reopening its lease. The A/B result establishes interference; the exact native instruction interleaving of the original failure was not captured.

## Completed local checks

- SDK 8.0.425 / macOS arm64 / FFmpeg 7.1.2.
- Probe plus unchanged heartbeat cases: **6 passed**, no skips/failures, 25 seconds (`/tmp/opencam-v039-delay-probe-green.log`).
- The first full command completed with **Core 248 passed / 1 failed**, and **Media 855 passed / 20 skipped / 0 failed**, Media duration 4m27s. Its process exit was **1**, not a full-suite PASS (`/tmp/opencam-v039-delay-observation-full.log`).
- After isolating process-spawning Core cases, fresh full Core build/test: **249 passed / 0 failed / 0 skipped**, 993ms (`/tmp/opencam-v039-core-process-isolation.log`). Media code/tests were unchanged by this Core-only isolation; its completed result above remains applicable. This is not a second all-solution command reporting exit 0.
- Version consistency 0.3.9 and diff whitespace checks passed. New CI must execute the complete solution again; none of these local results substitute for Windows acceptance or authorize packages from the failed run.
