# ADR 0009: Audio-only preflight and continuous startup status

Status: accepted for v0.3.11 (2026-10-21).

## Decision

Preflight uses a separate audio capture contract and platform input builder. It
does not start screen capture, map a video stream, encode video or request frames.
Only selected inputs are opened. Native helpers required for system audio may
select a display internally; no image is returned to the preflight pipeline.
Formal recording remains on the existing video/audio pipeline.

Checking consumes audio without saving it. Trial capture writes PCM audio into an
owned temporary Matroska file, starts its five-second timer only after audio
acknowledgement, then stops, seals and probes before enabling replay. A bound
stream decoder validates samples/duration and feeds the existing audio output
without video playback. The final five seconds are replayed. These checks do not
validate the formal video encoder or screen capture.

The two source cards are followed by a dedicated status area. Confirmation ends
audio capture before returning configuration, then retains the window as a compact
formal-start presenter. No selected audio or the disabled preference bypasses
audio checking, not preparation or safety checks. Actual acknowledgement closes
the presenter; unknown startup retains locks and tracking.

The presenter is owned but non-modal so existing main-window Stop and safety
controls remain reachable. View-model ownership/busy protection remains the
authority for settings and duplicate starts. Cancellation before handoff returns
no configuration; closing after a formal command is sent cannot pretend to cancel it.

## Alternatives rejected

- Hiding the video preview while continuing video capture would retain its costs
  and fail the requested audio-only scope.
- Treating an IPC timeout or window close as failure could leave unreported
  background recording.
- Reusing video-frame readiness would accept an unrelated signal; AudioReady is
  explicit and combined with fresh selected-source status and identity checks.

## Compatibility and costs

No package, project-schema or version change. Existing lifecycle IDs, revisions,
leases, shutdown confirmation and time limits remain. Legacy video preflight
components remain for existing regression coverage but are not production wired.
The optional unused frame callback is temporarily retained for caller compatibility.
Trial review requires a fresh check before formal confirmation. Native device,
mixed-source, slow-start and shutdown behavior still needs physical acceptance.
