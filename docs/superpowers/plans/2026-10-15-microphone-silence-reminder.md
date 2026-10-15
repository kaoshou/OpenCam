# Microphone silence reminder implementation plan

> Implement inline with test-driven development; keep verification focused as requested by the user.

**Goal:** Warn about continuous microphone silence without interrupting recording.

**Architecture:** Reuse classified recorder audio telemetry. A bounded Core detector feeds a UI-only advisory. Settings default on; Windows floating presentation requires confirmed native capture-exclusion affinity. Other platforms or affinity failures use an in-app advisory, never an unprotected floating window.

**Constraints:** 30 seconds of continuously observed silence; gaps longer than 1 second, live audio, unavailable data, pause, stop, disabled microphone and disabled preference reset the interval. No audio storage, extra capture device, dependency, schema/version change, recording command, automatic pause or stop.

## Tasks

- [x] Add detector tests, observe RED, implement monotonic bounded detector.
- [x] Add UI/settings tests, observe RED, wire load/save/reset and lifecycle.
- [x] Add fail-closed floating presentation tests and Windows native exclusion helper; localize both presentation paths.
- [x] Run related and complete Release tests; record manual capture-exclusion limitations and user-guide behavior.

Final verification: Core 248 and Media 792 passed, 20 hardware/platform tests skipped, no failures (`/tmp/opencam-silence-final.log`). Player 23 and website 30 passed. Build, version consistency and diff check passed. Author integration check only, as requested; no extra review agents. Keep the existing local branch/worktree; no version bump, merge, push or release.

## Review focus

Check missing/stale IPC data, sound returning, pause/resume and stop, disabled preference/microphone, exclusion failure and cleanup. Native capture-exclusion correctness, placement under mixed DPI, and physical microphone behavior require manual recording on each Windows capture backend; macOS AVFoundation is not assumed to exclude floating windows.
