# GNOME Wayland teaching tools

## Status and intent

Design approved by the user, 2026-11-01. The user accepted an optional GNOME
desktop extension as the first Wayland teaching-tools target. This document
details that approach; implementation evidence is tracked separately in
`docs/testing/wayland-teaching-tools-20261101.md`. Product base: `07c2d82aed56a6faac2abb94d32e473c71a08698`, Linux
development branch `codex/linux-app-test-20261030`, version 1.2.0 Beta.

Provide a visible teaching pointer and desktop drawing on Ubuntu 24.04 GNOME
46 Wayland, recorded directly through the existing authorized monitor portal
and OBS/libobs pipeline. Preserve recording reliability and the Windows/macOS
and X11 implementations. No postproduction pointer substitution.

## Approaches

The accepted approach is an explicitly enabled, removable GNOME Shell extension
with a narrow local OpenCam bridge. The compositor owns desktop actors and
drawing input; OpenCam retains recording/project ownership.

An ordinary XWayland/Avalonia overlay cannot be assumed to have global Wayland
placement, pointer access, or correct input routing. A layer-shell-only solution
does not target GNOME. An OBS-only overlay would appear in recordings but not
on the live desktop, contrary to the required teaching experience. Those are
not fallback implementations.

## Scope and user experience

- Initially support GNOME 46 on Ubuntu 24.04 Wayland only. Other versions and
  compositors remain unavailable unless independently validated; never disable
  GNOME extension-version checks to force compatibility.
- Offer a separately packaged user extension and clear install/enable/disable
  instructions. No automatic installation, enabling, root input access,
  arbitrary input injection, or unrelated desktop shortcut/theme changes.
- Show capability, connection, and failure status. Without a healthy compatible
  extension, ordinary authorized Wayland recording remains available; do not
  advertise teaching pointer/annotations as working.
- Match the existing recording-only experience: idle/preflight may inspect
  capabilities but never hide the cursor, intercept input or open a drawing
  layer. Activate teaching only for active recording and its acknowledged
  source. On resume, establish a fresh validated teaching lease. No independent
  idle whiteboard or additional portal prompt solely for teaching tools.
- Retain current pointer artwork, sizes and exact tip hotspot. During drawing
  the same pointer remains visible; pointer movement alone never draws.
- Pen, line, arrow, rectangle, ellipse; existing colors and widths; drag eraser
  deletes whole intersected strokes; undo/redo; clear. No text or area eraser.
- F10 toggles drawing without deleting strokes, F11 toggles toolbar, Esc exits
  drawing and clears. Existing configurable tool/recording shortcuts retain
  their semantics. Register only owned bindings, detect conflicts; never
  overwrite another application's or desktop's bindings. If a shortcut cannot
  be registered, report it and retain toolbar actions.
- Toolbar stays draggable, usable while drawing, and bounded to a visible
  monitor. Normal desktop click-through when drawing is off. Drawing captures
  only the configured region, not unrelated desktop interaction.
- Keep the existing behavior that the teaching UI may be visible in capture;
  do not promise capture exclusion or hide evidence by postprocessing.

## Architecture and ownership

Keep extension JavaScript modules, packaging, tests, and the Linux-only bridge
separate. Reuse artwork and annotation geometry/semantics where practical;
GNOME renders its own compositor actors, not a second Avalonia full-screen
window. Only a small capability dispatch hook may touch shared UI/recorder
composition; Windows/macOS branches and native builds must remain unchanged.

The extension owns bounded rendering, input handlers, drawing history and
cleanup. The Linux client owns a renewable session lease and recording-state
coordination. Use a same-user session-bus bridge with sender identity pinned
to the unique OpenCam owner, explicit extension-side connection consent, a
fresh per-session secret/nonce, strict protocol version and validated bounded
commands. Do not expose evaluation, shell commands, file reading, raw portal
tokens, or remote networking. Secret/identity data must not appear in logs or
projects. Same-user bus access alone is not treated as authorization.

Concrete interface names, serialization and rendering APIs are builder choices
subject to tests. UI and recorder must share one authoritative session, rather
than racing to own cursor visibility. Do not forward high-rate mouse events
over IPC: pointer tracking and drawing stay within the compositor.

Use compositor monitor geometry and logical coordinates. Map the explicitly
acknowledged portal monitor/crop to the compositor monitor; dimensions alone
are not identity. If that mapping cannot be verified, disable tools for that
source rather than draw on or intercept the wrong monitor. Do not change the
existing portal selection or authorization solely to enable tools.

## Safety and cleanup

The extension starts inert. Enable teaching only after capability and session
handshake, source validation and actual actor readiness. Native cursor hiding
is the final activation step; retain its exact previous ownership/state.
GNOME 46 cursor visibility is a feasibility risk, not a solved problem.

On stop, pause, owner loss, lease expiry, disable, lock, unsupported session,
source removal, or rendering/input failure: release owned input and shortcuts,
restore normal cursor, remove overlay actors/timers/signals and invalidate the
session. Pause/stop clears transient drawing; ordinary F10-off retains strokes.
Lock/unlock never auto-reactivates teaching tools without a fresh valid session.
Bounded history and redraw coalescing prevent unbounded Shell memory/work.

Extension failure must not kill or stall OBS, modify the media timeline, delete
recording bytes, or invoke an old core. On live failure, restore normal cursor
capture through the existing optional-pointer recovery policy where possible;
if that cannot be confirmed, explicitly warn while preserving video/audio.
Do not misreport that cursor recovery succeeded merely because the desktop
cursor is visible. No feature is enabled in a distributable package until
these transitions are tested.

## First feasibility gate

Before full UI integration, use an isolated disposable GNOME 46 session to
verify a minimal owned pointer actor and stroke are visible in actual OBS
portal recordings, not only screenshots. Inspect decoded frames for expected
pixels, hotspot alignment and no duplicate native cursor, with pointer moving
over another application. Verify actor/input cleanup after disable, client
termination and Shell interruption, without disrupting ongoing media output.

Do not patch Mutter/OBS, synthesize portal authorization, or switch to X11 to
pass this gate. If GNOME 46 cannot reliably replace the cursor or record the
actors, report the evidence and return for a scope decision; no silent
substitution with postprocessing or a pointer halo.

## Verification and delivery

Unit tests cover protocol validation/authentication, capabilities, session
expiry/reconnect, coordinates/crop, bounded stroke history/eraser, settings and
shortcut conflicts. GNOME integration tests exercise actual extension enable,
portal chooser, movement/drawing capture, toolbar access, owner loss/disable,
lock/source-change cleanup where feasible. Use generated test content only.

Regression: X11 tools/recording, Linux media import/preview/export and NativeOnly
factory contracts; Windows/macOS managed/build checks. Distinguish virtual
GNOME evidence from physical mixed-DPI, multi-monitor, real audio and long-run
acceptance. A local macOS machine cannot physically validate GNOME Wayland.

Deliver a development test package plus optional extension, commit/version,
installation/removal instructions, license inventory, test results and explicit
unverified items. No merge, version bump, tag or Release as part of this work.

## Primary references

- https://gjs.guide/extensions/overview/anatomy.html
- https://gjs.guide/extensions/review-guidelines/review-guidelines.html
- https://discourse.gnome.org/t/proper-way-to-hide-cursor-until-manually-un-hidden/29888
- https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.ScreenCast.html

The existing app deliberately gates annotations and presentation pointer on
`LinuxX11InputPolicy.IsX11`. The proposed bridge is additional desktop
integration, not permission granted by the ScreenCast portal itself.

## Builder check

Independent read-only check asked whether teaching works before recording.
Resolved from existing behavior (`CanOpenScreenAnnotations` requires active
recording): this first version is recording-only, as specified above. No other
important planning questions were reported. This check is not implementation
or runtime validation.
