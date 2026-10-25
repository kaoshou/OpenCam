# OpenCam 1.0.0 publication plan

User specification: publish 1.0.0, update README/home/manual with the OBS recording migration, teaching pointers, accessible player and oCam/EverCam quick starts.

## Constraints

- Preserve recording safety and NativeOnly/libobs; FFmpeg remains for preflight, editing and export, not recording fallback.
- Preserve existing dirty checkouts. Work in the existing isolated validation clone.
- Do not claim accessibility certification, universal FPS or unperformed physical validation.
- Publish only verified immutable source and matching Windows portable/installer and macOS packages.

## Task 1: Documentation

Add failing onboarding assertions; update bilingual README/manual/home, remove obsolete 0.4 introductions and capture settings, use actual pointer styles. Run website/player suites and inspect rendered layouts.

## Task 2: Official packaging

Audit release profiles against successful validation workflow. Add regression checks before promoting the tested OBS staging path into official packaging. Verify licenses, native-only policy and package identity on both systems.

## Task 3: Integrate and publish

Run version consistency, regression suites and workflow lint; review complete changes. Merge with current master without overwriting unrelated work, push, build both platforms, inspect artifacts and publish v1.0.0 release with current changes only. Update public downloads only after assets exist, then verify website deployment.

## Progress

- Inspection: latest validation run 40209101177 completed successfully on all jobs; official release workflow still builds legacy recording profile and must be corrected before publication.
- Authorization: user explicitly requested merge, push and Release; no additional confirmation needed for these scoped actions.
- Task 1 complete: bilingual README/manual/home, actual pointer artwork, OBS migration, familiar workflows. Website 43/43 passed; browser 23 PASS, one platform-specific Accesskey SKIP.
- Task 2 in progress: shared official OBS packaging implemented; staging tests prove explicit release provenance and native-only enforcement. Independent review found undeclared VC runtime dependency and incomplete corresponding-source delivery.
- Review fix: explicitly document VC prerequisite and diagnose missing SystemDirectory DLLs before native recording. Two new managed tests pass. No silent core fallback or installer mutation introduced.
- Publication BLOCKED: standalone Windows FFmpeg source coverage is explicitly pending in existing notices; OBS dependency source archives/patches have not been supplied as release assets. Added a fail-closed publication gate and pending coverage record. Do not bypass it by relabeling provenance links as completed delivery.
- Deferred minors from review: resumable immutable asset upload; Windows notice test-heading cleanup; owned Mac helper's internal 0.4.1 plist identity; installer removal of known obsolete helper on upgrades (NativeOnly cannot execute it).
- Review limits: no new physical device/5-minute sync, Windows installer upgrade, Gatekeeper download acceptance or screen-reader validation claimed. Browser layout/axe checks performed separately.
- Final evidence: 1,736 managed tests PASS, 23 SKIP; website 43 PASS; Node 102 PASS/2 SKIP; browser 23 PASS/1 SKIP; NativeOnly build and macOS policy probe PASS. No test failures. Source coverage gate intentionally blocks publication; task 3 merge/release not performed.
