# OpenCam0.3.4 release preparation

User authorized version increment, bilingual README/Pages refresh, regenerated screenshots, push and Release. Canonical VERSION changed0.3.3→0.3.4. Existing original repository dirty spec and five untracked plans were preserved; feature work stayed in focused-editor-revision.

## Evidence

- Final local Release tests: Core230 passed, Media725 passed/20 skipped/0 failed (`/tmp/opencam-v034-full-final.log`), .NET SDK8.0.425, FFmpeg7.1.2.
- Initial full run had one stale ExportLabelAndIconUseWhiteAgainstBlueBackground locator using Path.Single after adding the arrow. Changed to require at least one icon and assert every icon remains white; label-color assertion unchanged.37 related UI tests passed, then full regression passed. No production behavior or acceptance tolerance was relaxed.
- Website30/30 and player23/23 passed; static site build, version consistency and diff whitespace checks passed. Website's initial Swift icon test hit a sandbox cache permission error; rerun used isolated temporary cache without skipping it. Guide's unsupported relative Web-player-doc link was replaced with the public repository URL.
- Regenerated bilingual home840×820, editor1440×900, settings650×820 and subtitles780×620 using latest UI assemblies, actual App/Fluent styles, original Field Notes tutorial media, synthetic audio and bilingual captions. Screenshot fixture: `/tmp/opencam-v034-preview.CXfJFi`. Settings renders can be byte-identical because the shown preferences view is unchanged. Metadata explicitly marks illustrative state and no device-acceptance evidence.
- Browser policy rejected local file:// Web screenshot access. User explicitly chose not to include Web-player pictures this release. No alternate browser/screenshot workaround or private desktop/chat capture was used. Web features are documented normally.

## Publishing procedure

Use master Actions artifacts only after all five verify/build jobs succeed, publish-release is skipped and matching website deployment succeeds. Verify run/artifact head SHA, nonexpired/nonempty files, portable ZIP/app/source data, Setup PE and macOS DMG integrity. Draft first, upload and verify three expected0.3.4 assets, then publish and recheck target tag, release state, sizes/digests. Do not overwrite another commit's existing tag/Release; do not substitute local DMGs.

## Pending limitations

Preserve schema4 subtitle-project warning (v0.3.3 and earlier cannot open saved subtitles), original media retention and no automatic cue retiming. Actual offline-browser/touch/fullscreen, Windows-native and long-A/V, macOS visuals, physical disk exhaustion, complete physical Release Gate, trusted signing/notarization and Windows FFmpeg corresponding-source verification remain incomplete. Intermittent Windows heartbeat/fast-export fallback root causes are not claimed fully fixed. User-authorized publication is not a declaration that these gates passed.
