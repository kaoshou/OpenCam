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
