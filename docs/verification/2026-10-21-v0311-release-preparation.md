# OpenCam v0.3.11 release preparation

User explicitly authorized building and publishing the completed audio-only
preflight work. Increment the canonical patch version from 0.3.10 to 0.3.11;
do not overwrite v0.3.10. No further functionality or dependency changes.

## Scope

See [implementation verification](2026-10-21-audio-only-preflight.md) and
[ADR 0009](../adr/0009-audio-only-preflight.md). Audio-only checking/trials,
separate phase status, formal-start preparation and selected-source semantics
replace the preflight video preview. Formal video recording remains unchanged.
README, both guides and website now describe v0.3.11; historical release notes
and the earlier implementation evidence remain unchanged.

## Release verification

Local macOS arm64, SDK 8.0.425 (`/tmp/opencam-sdk8-20261020`) and FFmpeg 7.1.2
(`/tmp/opencam-task2-ffmpeg.GOzyFj`).

- Fresh restore and Release build succeeded; 0 errors, 4 existing nullable,
  xUnit and Avalonia warnings. Logs `/tmp/opencam-v0311-restore.log` and
  `/tmp/opencam-v0311-build.log`. Restore used RestoreIgnoreFailedSources;
  this is not a claim of complete online vulnerability audit.
- Player/CI tooling: 29 passed; website: 39 passed; website build exit 0.
  Logs `/tmp/opencam-v0311-{node,website,website-build}.log`.
- Native helper synthetic/argument tests passed,
  `/tmp/opencam-v0311-native.log`; not native audio-device acceptance.
- Canonical version and whitespace checks passed.
- Fresh full Release test run after restore/build: exit 0, Core249 passed,
  Media909 passed / 20 skipped / 0 failed (1,158 total passed; Media5m04s).
  Command `dotnet test ScreenRecorder.sln -c Release --no-build --no-restore`,
  log `/tmp/opencam-v0311-full.log`. No source changes during this run.

## Publication gate

Only exact-commit successful Actions outputs are eligible. All three verify jobs,
both package jobs and same-commit website deployment must succeed. Verify raw
artifact ZIP SHA256 against API metadata before extraction; check Windows PE,
complete portable contents, DMG integrity and read-only-mounted bundle version,
architecture, corresponding-source SHA and license notices.

Recheck tag/release absence. Create a draft with complete bilingual release notes
and build provenance; upload three exact-name assets, verify API size/digest/state,
then publish non-prerelease and recheck public state/tag/attachments. Do not reuse
local test installers, old-run artifacts or overwrite an existing different tag.

Physical Release Gate, four native audio combinations, unplug/slow-start/stop,
playback, long A/V, GPU/multi-DPI/cursor, physical disk exhaustion, browser
offline/touch/fullscreen, advisory exclusion and background shortcuts remain
incomplete. No trusted signing/notarization; complete Windows FFmpeg
corresponding-source verification remains pending. Bilingual release notes retain
these limitations; CI success does not imply Stable or physical acceptance.

Main checkout's unrelated dirty spec hash remains
`25780034e07095597e0d8b6af9d8f3280b88d34e`; its five untracked plans are untouched.
Retain the development worktree. No additional review agents.
