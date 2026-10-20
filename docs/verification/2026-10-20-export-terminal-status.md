# Export terminal status and worker completion

## CI evidence

Run 39441514353, commit `7d7ad7f55cb9ea002b692fc471fd583e8f4d35b8`:
macOS `ProjectIpc_NewContentReplayAndExportStatusRemainQueryable` failed at
ProjectIpcTests.cs:333: CloseAsync returned unsuccessful immediately after
polling export status Canceled. The old assertion did not include ErrorCode,
so the exact CI error code cannot be recovered from that assertion.

The job assigned its terminal status inside Task.Run before that task completed.
Coordinator source-owner operations correctly reject while Completion is pending.
An independent-thread regression exercises the real job 2,000 times per outcome:
old code exposed cancellation early 1,997 times and failure early 1,940 times.
Both assertions failed in `/tmp/opencam-v0310-export-terminal-red.log`.

The status getter now publishes a terminal result only after Completion is
completed, matching the existing coordinator gate. No close/lease protection,
export cancellation or cleanup is bypassed. The IPC assertion now includes its
error code without changing its expected success.

The two regressions and four existing lifecycle/IPC/receipt tests passed in
`/tmp/opencam-v0310-export-terminal-green.log` (6 passed, 0 skipped/failed).

Full local `dotnet test ScreenRecorder.sln -c Release --no-restore` including
build exited 0: Core 249 passed, Media 870 passed / 20 skipped / 0 failed
(1,119 passed total, Media 4m45s), `/tmp/opencam-v0310-terminal-full.log`.
Local macOS arm64 SDK8.0.425 and FFmpeg7.1.2; not Windows-native acceptance.
Version consistency and diff checks passed. Existing build warnings remain.

## Separate Windows failures, not claimed fixed

The same run failed both heartbeat retry theory cases (0 and 3000 ms fault).
The first 2-second watchdog delay resumed at 21,632 / 21,640 ms respectively,
after the 20-second retry deadline. This is different from the earlier run's
3-second injected fault delay being late. The zero-delay fault resumed in 0.4 ms;
the other fault was only created near the end, after the watchdog resumed.
Both were still Recording.

The bounded stacks include idle workers, runner/socket/headless waits and an
async-over-sync ReadFile worker. They do not establish pool exhaustion or a
specific timer/continuation root cause. Stack collection finished at roughly
14:49:08 and 14:49:31 UTC, well before resumed watchdogs at 14:49:23 and 14:49:46.
Do not claim the collection tool caused the entire delay or that this export fix
repairs heartbeat scheduling. No timeout, MinThreads, fault or assertion change.

Full failed-job output: `/tmp/opencam-v0310-current-failed.log`.
Evidence ZIPs and extracted TRX/environment reports:

- macOS `/tmp/opencam-v0310-macos-evidence.zip`, artifact 12273812754,
  SHA256 `82eb80b59f65b9b73500e1bbe96a4020c792d77afd63efeee5b3309f6da4bd00`.
- Windows `/tmp/opencam-v0310-windows-evidence.zip`, artifact 12275238707,
  SHA256 `be4f5d2b015af40498dfdcb2187fa41ce1b4092410c87f390a20c98f47d335f5`.

Both environment collectors succeeded with the targeted SDK/runtime queries.
macOS selected SDK10.0.400/FFmpeg9.0.1; Windows SDK10.0.402/FFmpeg n8.1.3.
These differences alone do not establish the failure cause.
Portable verification and same-SHA Pages succeeded; package builds were skipped.
No release may use this run.
