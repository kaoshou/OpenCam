# Selectable teaching pointers — verification

## Scope and environment

- Version remains 0.4.1. Existing isolated `codex/focused-editor-revision` checkout, base HEAD `313d28040dbdfe250e6947d1f444073cff3796b1`; combined uncommitted safety changes preserved. No merge, push or Release in this task.
- macOS host, .NET SDK 8.0.425 at `/tmp/opencam-direct-sdk8-verified`, FFmpeg at `/tmp/opencam-task2-ffmpeg.GOzyFj`. Native helper compiled with the local Swift SDK; no private desktop capture performed.
- Four original shared transparent PNGs, B Warm Ivory/Orange default, three sizes. Cursor enum values unchanged; display order only moves Presentation Stick to second. New recordings retain ordinary capture/audio paths, with no editable pointer track.

## Automated evidence

- Initial contract RED5, artwork/options GREEN11: `/tmp/opencam-teaching-options-red.log`, `/tmp/opencam-teaching-artwork-green.log`. Checks all twelve style/size combinations, transparent bounds, front-tip metadata, distinct designs, JSON normalization and paused copies.
- Platform/lifecycle RED6, GREEN231: `/tmp/opencam-teaching-overlay-red.log`, `.superpowers/sdd/2026-10-25-teaching-pointer-styles/task-2-tests.log`. Selected renderer pixels, strict macOS selected-option ACK, stale helper rejection, cancellation, readiness, fallback, pause/resume and terminal cleanup are covered without weakening A/V assertions.
- UI initially RED4. Final focused UI/settings/foolproof GREEN75: `.superpowers/sdd/2026-10-25-teaching-pointer-styles/task-3-tests.log`. Covers three languages, menu order, selection persistence, unrelated settings saves, recording locks and paused re-enable.
- Fresh whole-patch review found one important optional-thumbnail initialization exception boundary, plus stale guide wording. Injected failed thumbnail reproduced the constructor failure (RED1 `/tmp/opencam-teaching-thumbnail-red.log`); guarded nullable thumbnails keep all named options and ordinary recording configuration available (included in GREEN75). Both guides now describe automatic property application.
- Before that final thumbnail guard, full solution passed Core356 and Media1230, skipped21, failed0 (`/tmp/opencam-teaching-full.log`). The post-fix full run is recorded separately in `task-4-tests.log`; do not substitute the earlier run for its result.
- Website tests41 passed, build exit0: `/tmp/opencam-teaching-website-tests.log`, `/tmp/opencam-teaching-website-build.log`. Initial restricted run failed solely because the Swift icon test could not write its compiler cache; rerun with cache permission passed. Version consistency and `git diff --check` passed.
- Swift helper compile exit0: `/tmp/opencam-teaching-swift.log`. This is compilation evidence, not a physical overlay/capture result.
- Three-language opt-in real-Skia headless rendering produced 1x main-window crops; new selectors remain visible and selected. A trial raster-DPI 2x image was an invalid display-DPI simulation and is not acceptance evidence. The test now captures 1x only; physical mixed-DPI/Retina remains unverified.

## REQUIRES MANUAL VALIDATION

Prerequisites: Windows/macOS test build containing all four assets and the matching macOS helper; a saved disposable recording project, known moving content and audible source. Preserve the original MKVs and existing test ZIPs.

1. In each UI language, choose the second cursor mode. Verify four designs and Small/Standard/Large; default is Warm Ivory/Orange and Standard. Restart and change language; selected style/size must remain unchanged.
2. Record each design over light and dark teaching material while pointing at small targets. Confirm the front tip matches the target, no duplicate captured system cursor appears, the shape remains clear, and the resulting MKV, preview and MP4 agree.
3. Repeat on monitors with 100/125/150/200% scaling, negative-origin layouts, screen edges and macOS Retina. Confirm no displaced tip, clipping or orphan overlay; test fullscreen only where supported.
4. Record selected system/microphone audio with moving content; pause, change style/size, resume, stop. Controls must lock while recording, changes affect the next segment only, and previously captured A/V remains intact and seekable.
5. With the macOS helper/assets unavailable, verify ordinary cursor fallback and continued capture of the selected A/V. If an overlay exits during recording, verify a warning without a recording restart; stop must clean up owned helpers/windows.

Failure conditions: missing/black video, lost selected audio, unseekable finalized output, false recording status, shifted tip, duplicate cursor or leaked overlay. Collect application log around start/pause/resume/stop, selected style/size/capture/encoder, OS and monitor scaling, and ffprobe stream/duration output. Do not send private audio/video unless needed and explicitly agreed.

## Distribution boundary

No new test ZIP or public Release is implied by these source changes. The previous DirectPointer ZIP is unchanged and does not contain the new selectable artwork. Windows cross-publish and final full-suite results are added below when observed.

## Final observed results

- Post-fix full solution: Core356 and Media1231 passed (1587 total),21 skipped,0 failed, exit0. Media duration5m47s; full log `.superpowers/sdd/2026-10-25-teaching-pointer-styles/task-4-tests.log`. This supersedes the earlier pre-guard full run.
- Windows x64 self-contained single-file cross-publish exit0 to `/tmp/opencam-teaching-win-publish.DJAMgr`; log `/tmp/opencam-teaching-win-publish.log`. Existing Avalonia AVLN3001 preflight-window warning remains; no warning-free claim. This output is not an assembled portable distribution (FFmpeg/notices/source packaging not repeated).
- Windows executable PE signature/AMD64 machine and nonempty payload verified; exactly four published teaching PNGs match source bytes, each280x240. Legacy PNG also matches unchanged source.
- Final version consistency remains0.4.1; `git diff --check` passed. Review findings addressed; physical-device checklist above remains REQUIRES MANUAL VALIDATION.
