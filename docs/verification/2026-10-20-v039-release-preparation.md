# OpenCam 0.3.9 release preparation

## Scope and preservation

User requested build and GitHub Release. VERSION is canonical at 0.3.9. Includes the previously approved optional preflight setting, explicit live-preview-only low-buffering output, bookmark hover names and CI evidence collection/testing plan. No new application packages or project schema. Formal recording and five-second trial encoding settings remain unchanged. README, bilingual guides, website and release notes are synchronized; existing screenshots retain their actual 0.3.5 labels.

The existing development worktree is retained. The main checkout's original modified editor specification and five untracked plans are excluded. No private desktop screenshots or workaround for prior file-browser restrictions is included.

## Local verification

macOS arm64, .NET SDK 8.0.425 and FFmpeg 7.1.2:

- Final solution Release restore/build/test exited 0: Core 249 passed, Media 851 passed / 20 skipped / 0 failed (1,100 passed). Media took 4m28s. `/tmp/opencam-v039-full.log`.
- CI collector 5 plus web player 23: 28 passed, 0 failed. `/tmp/opencam-v039-node.log`.
- Website 39 passed, 0 failed, using isolated Swift/Clang caches for existing native icon tests. Website build exited 0. `/tmp/opencam-v039-website.log`, `/tmp/opencam-v039-website-build.log`.
- Version consistency and diff whitespace checks passed. Workflow YAML parses and retains always-uploaded evidence for all three verification jobs. Existing nullable, xUnit and preflight-window Avalonia warnings remain; this is not a claim that all warnings or online audits are cleared.
- Feature-level RED/GREEN evidence and real pointer-input tests are recorded in the adjacent preflight and bookmark verification reports. Local synthetic/headless coverage does not establish native Windows/GPU/audio acceptance.

## Publication requirements

Push the verified source, then require all three verify jobs and both package builds to succeed, with publication job skipped and Pages successful at the same SHA. Use only those Actions packages, verifying raw artifact digests/provenance, PE, ZIP integrity/content, DMG integrity/version and source/license notices. Preserve complete bilingual notes plus build provenance. Create a draft, verify three uploaded asset names/sizes/digests, then publish a non-prerelease at the exact source SHA and recheck the tag and assets. Do not overwrite a conflicting tag or publish missing assets.

Full physical Release Gate, native/long A/V and device-specific visual/interaction checks, capture exclusion, trusted signing/notarization and Windows FFmpeg complete corresponding-source verification remain incomplete. Historical Windows intermittent heartbeat/fast-export causes are not claimed all resolved. Publication is user-authorized, not a blanket acceptance claim.
