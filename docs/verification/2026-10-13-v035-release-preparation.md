# OpenCam 0.3.5 release preparation

User requested +0.0.1 and formal publication after approving progressive preview, compact controls and explicit range markers. VERSION is 0.3.5. No recording/export algorithm or project schema change; prior device/license limitations remain in bilingual notes.

Final local verification: SDK 8.0.425 / FFmpeg 7.1.2, Release solution tests Core230 passed, Media733 passed /20 platform-specific skipped /0 failed, `/tmp/opencam-v035-full.log`, exit0. Player Node23 passed `/tmp/opencam-v035-player.log`; website30 passed `/tmp/opencam-v035-website-permitted.log`; website build and version consistency/diff whitespace checks passed. Related scrubbing/range RED→GREEN evidence is in `2026-10-13-preview-scrubbing.md`.

Website's initial icon test was blocked by sandbox Swift cache permissions, then iconutil failed in the restricted environment; the unchanged test passed with permitted native tool execution and an isolated Swift cache. No cases skipped or product changes made for this environment failure. NU1900 online-audit warnings remain; no online vulnerability-audit pass is claimed. The initial Node invocation used a nonexistent test glob; corrected to the actual `tests/web-player/player.test.mjs`, all23 passed.

README/Pages/guides and eight offline genuine UI renders updated using original demonstration media. Editor shows compact controls and IN/OUT selection. Inspected the Chinese editor render; these are not native capture acceptance. No Web screenshot/personal desktop published, in accordance with the user's earlier choice.

Original main-repository dirty spec and five untracked plans remain untouched/uncommitted. Development worktree is preserved for local artifacts. Release only from matching successful Actions artifacts: all verify/build jobs success, publish-release skipped, matching website deployment; ZIP/app/SOURCE notices, PE and DMG integrity checks; draft upload three assets, verify digests then publish and recheck tag target/public state. Never overwrite a different commit's tag/Release.

Remaining: native high-resolution scrubbing responsiveness/fullscreen, complete physical Release Gate, Windows native/long A/V, macOS visuals, physical disk exhaustion, browser offline/touch/fullscreen, trusted signing/notarization and Windows FFmpeg complete corresponding-source verification. Publication does not establish these gates or resolve all earlier intermittent Windows heartbeat/fast-export causes.
