# Native-only Windows acceptance build

User explicitly requests a new Windows test build with only the new recording core and safety checks. Version0.4.1, development branch only, no Release/master integration. Existing worktree and unrelated changes are retained; user has waived intermediate questions.

The supplied log proves four native startup failures with zero output bytes and subsequent GDI fallback. One30FPS native run reached steady29.84–30.09 outputFPS, so FPS/load alone is not an established cause. The adapter discards native failure detail by presenting `capture_failed`; the missing stage/geometry/audio readiness facts prevent determining whether video or either audio source failed to initialize.

## Approach

A runtime-only flag could be lost with a damaged/missing manifest, accidentally restoring fallback. Globally deleting the existing engine would affect normal builds and legacy diagnostics. Use an explicit build profile `OpenCamNativeOnlyTest=true`: omit the legacy screen-recorder implementation from the test assembly, require the native runtime, reject compatibility fallback at router and orchestrator boundaries, and omit the old native helper from the ZIP. Normal builds retain their existing behavior. FFmpeg remains for audio-only preflight, editing, playback and exports, not as an alternative formal screen recorder in this profile.

Windows settings expose only Automatic in this profile and normalize old stored choices in memory. Pinned legacy sessions cannot resume in this build; existing source files/projects remain readable/editable/exportable. No change to macOS normal builds.

## Safety and diagnosis

Keep real source geometry, requested audio callback, encoded-frame/first-byte startup gates, existing10s native/15s managed deadlines, owned output, confirmed cleanup and crash-safe MKV. Missing/corrupt runtime or unsupported source fails before recording; source/runtime loss never switches core or overwrites media. Hardware→CPU encoder fallback may remain inside libobs only before first media and with confirmed cleanup.

Preserve only allowlisted native error codes. Attach bounded startup diagnostics as optional fields to existing ready/error protocol events (protocol1 backward-compatible): fixed stage, elapsed time, actual/expected dimensions, selected-source/readiness booleans, frame/byte counters. No paths, endpoint names, titles or raw upstream logs. Add numeric compositor total/lag/frame-time observations to existing progress events. These counters do not prove unique captured images or physical A/V sync.

This build prevents fallback, not the as-yet-unproven physical startup fault. New failures must be explicit/actionable; never claim black-screen or physical-sync acceptance from generated sources. Deliver an immutable independently built portable ZIP with source/hash/license checks and clear short-recording instructions.
