# 0.3.9 Windows trial-level timeout evidence

## Hosted failure

Run 39308108940, source 428d7f81b84bac27af29112d121c098dfbd84e4b: portable/macOS verification, macOS packaging and Pages passed. Windows Core passed 246 with 3 skipped. Windows Media passed 793 with 71 skipped and one failure: `TrialUsesCurrentEngineSamplesOnlyForSelectedInputs(MicrophoneOnly, true, false)`.

The original 20-second cancellation deadline fired in the test's 20ms sample-poll delay (line 41), not a wrong audio-state or RMS assertion. TRX duration was 31.2357176 seconds including cleanup. This deadline covers live-check startup, frame wait, stopping the live check and starting a separate trial encoder. The existing evidence cannot distinguish these stages from delayed timer/ThreadPool continuations. It does not establish a native microphone failure or a root cause in the product. No Windows package was built; no package from this failed run may be released.

Evidence: `/tmp/opencam-v039-windows-failed.log` and `/tmp/opencam-v039-windows-evidence/`. Raw evidence ZIP SHA256 `6f08bd41df565230215a6a66dab25536546493a533b998052bf89dcc76a58e11` matches artifact 12216530019. Environment evidence reports SDK 10.0.401 despite installing .NET 8, and FFmpeg n8.1.3-9-g29e619e767-20260930. The testhost targets .NET 8; the SDK difference is not proof of this timeout's cause and is not changed in this diagnostic patch.

## Diagnostic-only change

Keep the product, job filters, 20-second deadline, 20ms poll interval, real FFmpeg engines, enabled/disabled and exact RMS/stale/missing-source assertions, cancellation and no-post-disposal-read checks unchanged.

The failing theory now records elapsed/check-ready/poll-delay timing, last phase/error, trial task status, engine running/frame/time values and worker/queue counts before cleanup. Reuse the existing Windows-only independent-thread stack observer when no trial sample has been read after five seconds. Its tool is already pinned in CI, bounded by its own ten-second dedicated waiter, joined at disposal and collects managed stacks, not dumps or pixels. The helper's legacy “heartbeat” labels are retained; this caller observes trial reads, not session heartbeat progress.

A test-only logger retains at most 32 messages, each at most 10,000 characters, and renders at most 120,000 characters. It allows failures, relevant FFmpeg diagnostics and stack-observer messages, excluding informational launch arguments. Original exceptions are preserved in the failure report. Test parallelization remains disabled as before.

## Local checks

The empty collector failed its expected evidence assertion: `/tmp/opencam-v039-trial-diagnostics-red2.log`. An earlier collector attempt recursively disposed itself as a Serilog sink; it was corrected to a separate non-disposable sink before validation and is not a product change or evidence of the hosted failure.

Release build plus all ten trial/diagnostic cases passed, zero skipped/failed, in 1m07s: `/tmp/opencam-v039-trial-diagnostics-green.log`. Version/diff checks passed. The full product baseline remains the earlier 1,100 passed / 20 skipped run; this diagnostic patch does not claim another full-suite run or Windows stack-helper acceptance. Hosted verification must still pass before publication; no root-cause fix is claimed.
