# OpenCam 0.3.10 release preparation

User explicitly authorized version +0.0.1, push and Release. This integrates the locally implemented direct preflight preview and accurate trial phases; it does not implement the later performance ideas.

## Scope and evidence

- Canonical VERSION is 0.3.10. Bilingual README, guides, website and new release notes describe direct raw-frame preview, quiet range guidance and startup/recording/finalization phases.
- Implementation and RED/GREEN evidence: [direct preflight verification](2026-10-20-direct-preflight-preview.md); architecture: [ADR 0008](../adr/0008-direct-preflight-preview.md).
- Release verification uses local macOS arm64, SDK 8.0.425 and FFmpeg 7.1.2. Player/CI tooling: 28 passed; website: 39 passed; website build succeeded; native audio helper synthetic/argument tests passed. Logs: `/tmp/opencam-v0310-{node,website,website-build,native}.log`.
- Fresh `dotnet test ScreenRecorder.sln -c Release --no-restore` completed with exit 0: Core 249 passed; Media 868 passed, 20 skipped, 0 failed (4m45s). Total 1,117 passed. Log: `/tmp/opencam-v0310-full.log`. This includes Release compilation; existing nullable, xUnit2031 and Avalonia AVLN3001 warnings remain. It does not establish online NuGet audit or native-device acceptance.
- Version consistency and whitespace checks passed.

## Publication procedure

Only publish installers from the exact pushed commit's successful Actions run, after all five verification/package jobs and the same-commit website deployment succeed. Validate artifact metadata, raw ZIP SHA256, contents, PE headers, portable extraction, DMG integrity, read-only-mounted bundle version/architecture and corresponding-source/license notices.

Recheck tag/release absence before creation. Use the committed bilingual notes plus immutable Actions provenance. Upload all three assets to a draft, verify API names/sizes/digests/uploaded state, then publish a non-prerelease and recheck tag target and assets.

No local development installer substitutes for Actions output. Native device latency/audio/GPU/multi-DPI and the complete physical Release Gate remain unverified; the release notes preserve these limits. No new screenshots, private desktop capture, application packages or project schema changes.

The existing development worktree is retained. Main checkout's unrelated modified spec (hash `25780034e07095597e0d8b6af9d8f3280b88d34e`) and five untracked plans remain untouched.

## First Actions run: environment inventory failure

Run 39440022641 at `2c03348e7007fbf258e3f7cea07852b9936416f4` failed on Linux and Windows before restore/test: the environment collector killed `dotnet --info` at its existing 10-second bound. Both saved stdout streams contain only the SDK version and commit; no stderr. Linux selected SDK 10.0.402 and Windows 10.0.401. FFmpeg/ffprobe inventory succeeded. macOS verification and same-commit Pages 39440022638 succeeded; both package jobs were skipped, so no release was created.

Full job logs: `/tmp/opencam-v0310-portable.log`, `/tmp/opencam-v0310-windows.log`. Evidence ZIP digests match the uploaded job logs: portable `35a9f9fd330e3b9a108d37021b8dcaf1cdc881e6ec995e0c1b5bca65e4995eaa`, Windows `22a7d3799d472a7624b064cf6368200c20ea6d8a4f5800c8e41917c21ab487d1`. Both complete environment reports were inspected.

Replace the broad information query with separate documented `dotnet --version`, `--list-sdks` and `--list-runtimes` commands, preserving each command's 10-second/16KiB bounds and mandatory nonzero/error failure handling. This obtains the required version inventories without coupling them to the additional SDK information walk. It does not identify the internal cause of the hosted SDK stall or pin/change the build SDK. No application code, workflow gates, timeout, retry, test assertions or vulnerability checks are relaxed.

A real-tool regression failed against the previous collector (1 failed/5 passed, `/tmp/opencam-v0310-env-red.log`), then all CI-tool/player tests passed (29/0 failures, `/tmp/opencam-v0310-env-green.log`). The actual CLI also saved all inventories successfully at `/tmp/opencam-v0310-environment.json`. Missing-command, noisy-output, timeout, evidence persistence and exit-status tests remain, including all three required dotnet queries. Version/diff checks passed. The application baseline above is unchanged; this is not a new full .NET-suite execution. Hosted confirmation awaits the next run.
