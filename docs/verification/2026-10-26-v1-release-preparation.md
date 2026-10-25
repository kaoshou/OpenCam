# OpenCam v1.0.0 release preparation

Status: **NOT RELEASED / NOT MERGED**. Version remains 1.0.0. Preparation does not authorize bypassing source-delivery requirements.

## Completed preparation

- README and generated website/manual replace the 0.4 introductions with OBS/libobs recording, direct teaching pointers, accessible Web player and transcripts.
- Bilingual oCam/EverCam quick starts describe automatic MP4, GIF limitations, segmented recording, editing, multiple subtitles and Web export without implying format compatibility or affiliation.
- Homepage uses actual packaged teaching-pointer artwork and responsive cards. Existing images remain explicitly labeled historical interface illustrations.
- Official packaging paths reuse the validation workflow, enforce NativeOnly, verify runtime inventory/notices, and retain FFmpeg for preflight/preview/edit/export.
- Release staging has distinct production provenance, without enabling old capture fallback. macOS signature/inventory ordering remains unchanged.
- Windows VC runtime prerequisite is now declared with Microsoft's official link and missing DLLs are diagnosed before recording-engine creation. No unsolicited installer runs or system changes.

## Evidence

- Website suite: 43 PASS, zero failures (desktop context).
- Final Node player/scripts/workflow suite: 102 PASS, 2 SKIP, zero failures.
- Browser regression: 23 PASS, one native macOS Accesskey platform SKIP; no failures. Actual keyboard accesskey operation was checked previously; this run does not replace that skipped native activation with synthetic success.
- Responsive onboarding screenshots: `/tmp/opencam-v1-release-browser/screens/onboarding-desktop.png` and `onboarding-mobile.png`; desktop inspected directly.
- Final full managed run after prerequisite guard: Core 358 PASS, Media 1378 PASS/23 SKIP, zero failures (1,736 passed total). Log: `/tmp/opencam-v1-final-full.log`.
- NativeOnly compile and policy probe: PASS on local macOS; no old recording-engine type, no legacy settings path, missing runtime fails closed. This is not a Windows prerequisite installation test.
- Missing-VC diagnostic tests: 2 PASS. Release staging tests: 18 PASS. Actionlint/version consistency: PASS.
- New source-delivery gate rejects pending review, incomplete scope coverage, unsafe archive paths and changed archive bytes.

## Publication blocker

Existing `THIRD-PARTY-NOTICES.md` explicitly leaves Windows standalone FFmpeg corresponding-source coverage unverified. `scripts/install-windows-ffmpeg.ps1` creates `SOURCE-STATUS.txt` marked PENDING. The pinned BtbN release lists executable builds and checksums, not a corresponding-source archive. A source homepage or matching FFmpeg commit alone does not demonstrate coverage for all enabled GPL dependencies and applied patches.

The OBS runtime source notice also calls for preserving matching full dependency sources, upstream patches and build material alongside binaries. Those distribution assets have not been assembled/reviewed in this task. Do not mark the pending coverage JSON reviewed without real delivered archives and a source audit. The publication gate prevents accidental binary-only release.

Resolving the Windows tool source gap may require obtaining exact source/build inputs upstream, or changing to a controlled self-built FFmpeg distribution and repeating compatibility tests. The latter changes the packaged media dependency and needs a deliberate user decision; it must not silently remove codecs or features.

## Independent review and deferred items

Read-only review found two Important gaps: undeclared VC runtime (addressed with declaration/diagnosis) and unfinished source delivery (publication remains blocked). Four Minor items are deferred: immutable partial-upload resumption, official Windows notice's inherited test heading, inner macOS helper plist identity still 0.4.1, and removing the known obsolete capture helper during installer upgrade. NativeOnly routing cannot execute that old helper.

Not claimed tested: fresh Windows installation/upgrade, physical audio-device acceptance or long-duration A/V synchronization for the new official profile, downloaded-app Gatekeeper/notarization acceptance, or screen-reader operation. Browser layout/axe evidence is not accessibility certification or proof of media-content correctness.
