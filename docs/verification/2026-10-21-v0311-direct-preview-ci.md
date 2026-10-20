# v0.3.11 Windows direct-preview regression investigation

## Observed CI failure

Build run `39463526666`, commit `4c872fd8c13315f58c1e49d97591d81d1c2e7b12`:

- Portable verification, macOS verification/build, and same-commit website deployment succeeded.
- Windows Core: 246 passed, 3 skipped. Media: 850 passed, 72 skipped, 1 failed.
- Only failure: `DirectPreflightTests.LiveOutputCanBeDisplayedWithoutAContainerOrDecoder(mac: True, audio: SystemAndMicrophone)`, line 91.
- The full-frame divisibility assertion expected zero but received `753664`. The preceding minimum-one-frame assertion passed. TRX duration: 21.230995 seconds.
- This is a synthetic legacy **video** preview regression on a Windows host using macOS argument construction, not a native macOS capture test or the new audio-only preflight.
- Windows packaging was skipped; this run is not eligible for release.

Evidence:

- `/tmp/opencam-v0311-windows-failed.log`
- `/tmp/opencam-v0311-windows-evidence.zip`, Actions artifact `12284178695`
- ZIP SHA256 `b036ae71c389e648a2246f21e7119312277582fadfb797d2b1abb61605b0db1a`, matched the API before extraction.
- Extracted TRX and complete environment report: `/tmp/opencam-v0311-windows-evidence/`.
- Environment collector succeeded: Windows runner `win25-vs2026`, image `20261007.264.1`, SDK 10.0.401, net8 test target, FFmpeg `n8.1.3-9-g29e619e767-20260930`.

## What is known and unknown

The engine waits for its output-copy task during stop. Its stop path can terminate FFmpeg if the caller deadline or existing six-second shutdown wait expires. A terminated raw-video writer can leave a partial final frame. However, the failing test did not record startup/stop timings, deadline state, or engine diagnostics, so this is a hypothesis, not a demonstrated cause of this particular failure.

The bounded latest-frame sink already ignores partial frames, but the test intentionally checks the raw engine output. We do not replace that assertion with a weaker consumer-only check, truncate the result, retry, or skip the case.

Local unmodified targeted run: all 10 `DirectPreflightTests` passed, exit 0, `/tmp/opencam-v0311-direct-investigation.log`. This does not prove the Windows issue fixed.

## Diagnostic-only change

The original real-engine test now retains failure information before disposal:

- provider/audio selection;
- elapsed time, startup-return time, stop-return time and derived stop duration;
- caller deadline cancellation state;
- final output byte count only after stop/copy completion, avoiding concurrent MemoryStream inspection;
- engine progress, running state, and pool counters;
- the existing bounded preflight diagnostic collector (32 events, 10,000 characters each, 120,000 total), excluding full launch arguments.

All original assertions, eight cases, 15-second deadline, engine stop timeout, providers, output stream, and production code are unchanged. No new runtime instrumentation, stack process, retry, or dependency is introduced.

If it fails again, read the complete `Direct preview diagnostics before cleanup` report before deciding whether startup delay, shutdown, process failure, or output framing is responsible. Do not infer pool starvation from a worker count alone.

## Local verification after instrumentation

- `dotnet test ScreenRecorder.sln -c Release --no-restore` (includes build): exit 0, Core 249 passed; Media 909 passed / 20 skipped / 0 failed, 4m57s. Log: `/tmp/opencam-v0311-direct-diagnostics-full.log`.
- SDK 8.0.425, FFmpeg 7.1.2, macOS arm64. Existing xUnit warning remains; this is not a claim all baseline warnings were resolved.
- Version consistency (`0.3.11`) and `git diff --check` passed.
- The change is evidence collection, **not a confirmed Windows root-cause fix**. Native acceptance limitations from the release notes remain.
