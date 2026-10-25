# Windows NativeOnly system-audio stability candidate — 1.0.0

Baseline: `3fc5ef89c7a5b978abd78da4e7a608fed7fb2c85`. Development branch only; no Release.

## Evidence and cause boundaries

The supplied app log shows three source-wait failures with correct display geometry, microphone samples present, and system samples absent. The user's additional physical audit reports silence failing, microphone-only succeeding, and a currently playing test tone allowing both sources to start. Its Windows `native-audit` directory is not mounted on this Mac: those additional measurements are attributed to the user, not independently re-executed here.

Confirmed code defect: startup requires the first callback from every selected audio source; the running warning treats a callback older than two seconds as a stall. This measures packet arrival, not amplitude. Pinned OBS `win-wasapi.cpp` handles a zero packet count by returning without producing source audio; its `ClearBuffer` writes one silent render buffer without starting a persistent renderer. Thus absence of a render stream can prevent this readiness contract from completing. This is consistent with the reported physical A/B test; it does not establish all driver/device failures have the same cause.

Another confirmed difference: NAudio 2.2.1 preflight defaults to eRender/eMultimedia, while OBS defaults to eRender/eConsole. Whether these pointed to different endpoints on the user's laptop is unverified. References: [NAudio pinned source](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiLoopbackCapture.cs), [Microsoft capture packet semantics](https://learn.microsoft.com/en-us/windows/win32/coreaudio/capturing-a-stream), [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording).

No evidence here establishes an NVENC throughput defect, missing packet duration, a damaged MKV solely from absent format.duration, or a recorder crash from command-response broken pipes.

## Changes

- Windows: an owned MTA thread initializes and continuously feeds a silent shared-mode renderer on the exact selected endpoint. Its resolved ID is also supplied to OBS. Default selection uses eMultimedia to match preflight. OBS must still provide real audio callbacks, video geometry, muxed video and output bytes before recording is acknowledged. No synthesized capture timestamps, substituted capture samples or recording fallback.
- Invalid/missing endpoint, renderer failure and default endpoint changes are detected. Device loss stops the segment and retains bytes. The next start resolves the new default; the current segment does not silently switch devices. Endpoint names/IDs are excluded from transport diagnostics. Privacy-filtered upstream WASAPI errors retain numeric HRESULT only.
- A native fatal device-loss error publishes failure before the legacy device-loss observer is notified, preventing its implicit silent-recovery recording path from taking over. The native source attempts to seal the active output before reporting that fatal error.
- Empty failed startup returns to Ready only after engine, capture lease and attempt cleanup are confirmed and no MKV bytes/confirmed segments exist. Nonempty/unreadable attempts and unconfirmed cleanup retain SaveFailed. Failed resume retains saved sources and exposes SaveFailed. Empty content does not auto-export.
- Disposable telemetry/audio-level requests receive RecorderBusy when lifecycle owns the command gate. Polling is suppressed during home-window lifecycle work. Request ID, command, queue wait and handler timing are logged; response IOExceptions remain visible.
- Saved source bytes are included in project snapshots. Idle disk availability refreshes independently; sessionless telemetry no longer overwrites a valid size with zero. Busy banners identify capture preparation, segment saving and export instead of sharing an initialization label.
- Test-package README version corrected to 1.0.0.

## Tradeoffs

The silent renderer keeps the selected audio engine active while system audio is selected, with a small ongoing audio/power cost. It does not alter output volume or play an audible tone. Exclusive-use/device/driver failures remain failures. A successful renderer is not proof of successful OBS capture: the original actual-sample readiness guard remains. The same first-sample guard still bounds a source that never produces packets after this fix.

## Verification matrix and limits

| Scenario | Automated coverage / required physical follow-up |
|---|---|
| Empty failed startup / retry | Coordinator regression, fails against baseline SaveFailed then passes candidate |
| Nonempty attempt / cleanup failure | Retention regression; remains SaveFailed |
| Failed resume with saved sources | Original bytes/sources preserved; Finish remains possible |
| Poll behind slow lifecycle | Real authenticated named-pipe regression: immediate RecorderBusy, later successful poll |
| Renderer initialized without capture | Engine protocol regression cannot acknowledge recording |
| Real Windows invalid endpoint / silent loopback | Native Windows executable rejects invalid endpoint, tests three seconds of actual packet timestamps when a render endpoint exists; exit 77 explicitly means SKIP |
| Windows dual-source silent start / system-only silence | Requires physical WGC/WASAPI confirmation on target laptop |
| Playback start / stop / restart, mic-only, no audio | Synthetic core mode coverage plus target physical confirmation required |
| Preflight cancel/retry; repeated F10 | Existing preflight/lifecycle automated coverage; physical confirmation required |
| Default endpoint change / removal | Detection implemented; physical hot-plug/default-switch acceptance required |
| Five-minute 30 FPS with pauses; 20 FPS comparison | Not claimed: target physical recording, subjective playback, packet monotonicity, duration, decode and AV drift remain required |

The CI artifact's SOURCE.txt and OBS-TEST-INFO.json identify the precise candidate commit. Existing manual-validation markers remain. Build/synthetic success is not a physical recording acceptance result. Test logs and any failed/ skipped checks are reported with the delivery, rather than being interpreted as physical passes.

## Local runs during implementation

- Source-wait empty-session and telemetry queue regressions were observed failing before their fixes and passing after them. Native device-loss notification ordering also failed before the ordering fix.
- macOS rebuilt libobs helper: 12/12 synthetic acceptance tests passed (not physical microphone/display tests).
- Runtime staging tests: initial sandboxed AppKit fixture aborted; the same 24 tests passed under normal desktop access. This is an execution-environment distinction, not a change to package validation.
- First broad managed run: Core 358 passed; Media 1352 passed, 23 skipped, one failure in `StrictStartupReportsSafeReasonAndSourceReadinessWithoutLeakingHelperText` (managed two-second fixture timeout instead of expected helper error). Targeted rerun passed. Parallel compilation/load was present but is not established as the cause. A fresh full run is retained separately; no production timeout was increased to address this fixture.
- Latest complete managed regression: Core 358 passed; Media 1360 passed, 23 skipped, zero failures (1718 passed total). Process-diagnostic fixtures that replace the global logger now run without parallel media fixtures; their existing timeout remains unchanged. A subsequent short-badge text adjustment passed eight targeted home/controller tests.
- Windows Actions run `40200534731` successfully built the helper and test package. Its native synthetic acceptance had 11 passes, one Apple-only skip. Physical WASAPI output was `SKIP physical WASAPI: runner has no render endpoint; invalid endpoint rejection passed`. This does not verify silence capture on the target laptop. The final delivery identifies the later commit if it contains only the concise badge/report follow-up.
