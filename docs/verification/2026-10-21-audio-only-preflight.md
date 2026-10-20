# Audio-only preflight verification — 2026-10-21

Scope: approved audio-only check/trial and separate preparation status. Local
development branch only; VERSION remains 0.3.10. No push, merge, release or new
application dependency. Historical release notes are unchanged.

## Evidence

macOS arm64, .NET SDK 8.0.425, FFmpeg 7.1.2. Synthetic audio and headless UI
results are not native-device acceptance.

- Arguments RED→GREEN: missing audio-provider contract, then 33 passing cases,
  including actual different-rate PCM mixing and one silent input.
- Capture/session RED→GREEN: 6 cases covering acknowledgement, selected-only
  sources, partial initialization cleanup, stopped reads and identity changes.
- Trial/probe/replay RED→GREEN: 29 cases including real synthetic audio-only
  capture/probe/decode, seven invalid-input rejection cases and controlled output.
- UI RED→GREEN: five initial failures, then 36 passing audio UI/IPC/lease cases.
  Both languages cover phases; no image/frame callback. Button bounds are checked
  at minimum window size with long text.
- Preparation RED→GREEN: new state/confirmation contract, then 138 related
  preflight/start/home cases. Cancellation during handoff initially returned a
  configuration; the fix returns null before any formal start. Final focused run:
  22 passed, zero failures/skips (`/tmp/opencam-audio-preparation-final.log`).
- Final complete Release build/test: `dotnet test ScreenRecorder.sln -c Release
  --no-restore`, exit 0, Core 249 passed, Media 909 passed / 20 skipped / zero
  failed (1,158 passed total; Media 5m04s), `/tmp/opencam-audio-final-full.log`.
  Uses existing restored dependencies; no source changes during this run.
- Earlier Task 4 full run had one obsolete diagnostic assertion that video
  HasFrame authorizes the new audio UI. Its real video decoding checks remain;
  it now explicitly asserts HasFrame true, AudioReady false and CanStart false.
- Player/CI scripts: 29 passed, zero failures/skips,
  `/tmp/opencam-audio-final-node.log`.
- Website: final 39 passed, zero failures/skips,
  `/tmp/opencam-audio-final-website-allowed.log`; build exit 0.
  Initial failures: independent version literal in new website copy (corrected)
  and native icon-tool sandbox/cache restrictions. Identical native-tool tests
  passed with permission and isolated temporary caches; assertions unchanged.
- Canonical version check passes at 0.3.10; `git diff --check` passes.
  No csproj/package lock/version changes against c25680b. Main checkout's
  pre-existing dirty spec hash remains
  `25780034e07095597e0d8b6af9d8f3280b88d34e`.

## Review and decisions

Author self-review, as requested: no additional agents. This is weaker than an
independent review and is not a release gate PASS.

1. Retain legacy video preflight components/tests as valid regression coverage,
   but production wiring uses the audio session. Cost: unused legacy surface.
2. Preserve platform mixing semantics (Windows amix, macOS resampling). Cost:
   platform-relative levels may differ; real device checks remain required.
3. Reuse bound-file process wrappers; determine duration from streamed PCM rather
   than unreliable pipe-Matroska metadata. Cost if wrong: rejected/trimmed trials.
4. Combine real recording/replay and timer/readiness tests where appropriate;
   silent-source coverage comes from the existing mixed-rate test. Cost: less
   granular failure localization.
5. Use the actual shared LocalizationService, not a second resource table.
   Cost if wrong: stale/missing UI translation.
6. Keep the optional unused frame callback temporarily for source compatibility;
   production omits it and tests reject its use. Cost: an unused parameter.
7. Disabling the final source in an open dialog gives no-device readiness, with
   trial disabled. Initial None skips the dialog. Cost: these paths differ visually.
8. Trial review requires rechecking before recording to preserve freshness.
   Cost: one additional check.
9. Owned non-modal status window retains access to main Stop/safety controls.
   Busy/ownership guards still protect editing and repeated starts. Cost if an
   uncovered action lacks a guard: overlap during preparation.
10. Reuse acknowledgement coordinator for legacy Resume; still-paused telemetry
    is not successful resume. Remove success guessed from an error containing
    “Recording”. Cost: undocumented old error conventions no longer appear successful.
11. Tasks 4/5 share one final full regression with Task 6, after their separate
    focused runs. This is not multiple independent full-suite passes.
12. Documentation explicitly labels the new behavior unreleased. Cost: temporary
    development notices until a separately authorized release.

Review focus checked: selected inputs/mixed rates, partial startup cleanup,
trial cancellation and storage identity, ACK/unknown-state lifetime, and
minimum-size action reachability. Formal encoder/source selection remains on
the existing pipeline; leases, identity/revision and shutdown confirmation remain.

Deferred minor: known-failure preparation keeps the general preparation heading
and Cancel label; detailed error remains on the main window. The failure body and
enabled dismissal are explicit. No correctness blocker identified by self-review.

## Physical acceptance — not performed

Prerequisites: dedicated test project, current Windows/macOS build, microphone and
system output available, screen/audio permissions, both languages. Keep logs and
record expected source selection; do not use private desktop captures.

| Case | Steps | Expected / failure evidence | Status |
| --- | --- | --- | --- |
| Four source choices | None, mic, system, both; start each | None bypasses check but shows preparation; others open only selected sources. Save log on mismatch. | Not verified on either OS |
| Mixed rates/silence | Use 44.1/48 kHz devices; silence one source | Other source remains audible; both independent meters correct. Save test audio/log if not. | Not verified on either OS |
| Startup/permissions | Deny permission or disconnect second input during startup | No false readiness; already opened source closes; retry possible after recovery. | Not verified on either OS |
| Trial/replay | Start trial under load, wait for ready, replay | Startup does not consume five seconds; audio only; no live input during replay. Save timing/log evidence. | Not verified on either OS |
| Cancellation | Cancel during checking, trial, finalizing and handoff | Owned resources stop; no stale playback or formal start. Unconfirmed stop remains blocked. | Not verified on either OS |
| Formal preparation | Checks off/on; new, resume, editor insertion; slow startup | Preparation stays until ACK, unknown state remains tracked; Stop/safety reachable. | Not verified on either OS |
| Formal A/V regression | Record configured region/cursor/encoder and selected audio | Normal video/audio preserved, including long-recording synchronization. | Not verified on either OS |
| Native layout | Minimum window/high DPI/long text, zh/en | Status readable, main safety and dialog actions accessible. | Headless geometry only; native not verified |

No claim of completed GPU, long A/V, device-unplug, signing/notarization or
complete physical ReleaseGate acceptance.
