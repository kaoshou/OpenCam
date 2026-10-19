# Timeline bookmark hover names

## Scope

- Show the full existing bookmark name when the pointer rests over a visible purple timeline flag.
- Close the tooltip when leaving the flag/control, clicking, fitting/zooming/resizing the timeline, or replacing/detaching its model.
- Keep bookmark click-to-select/seek unchanged; hovering must not select or seek.
- Reuse the platform-themed Avalonia tooltip, without new dependencies, localized fixed text, recorder changes, or project-schema changes.

## Verification

Environment: local macOS arm64, .NET SDK 8.0.425, FFmpeg 7.1.2.

- Two headless UI tests exercise real mouse movement, switching flags, full Chinese/English names, leaving the flag and control, clicking to seek, fitting, and replacing the model.
- The test fixture must process the headless window's initial client-size event before computing marker coordinates. Event tracing confirmed that its pre-input bounds otherwise differed from the actual input bounds; the name/open/seek assertions were not weakened.
- After correcting initialization, suppressing the production tooltip name causes both tests to fail with an actual null name: `/tmp/opencam-bookmark-tooltip-confirmed-red.log`.
- Restored implementation: eight bookmark-related tests passed with no failures: `/tmp/opencam-bookmark-tooltip-green2.log`.
- Final solution-wide Release build/test exited 0: Core 249 passed, Media 851 passed / 20 skipped / 0 failed (1,100 passed total). Media duration was 4m31s: `/tmp/opencam-bookmark-tooltip-full.log`. The existing preflight-window Avalonia AVLN3001 warning remains; no new warning was introduced by the tooltip.
- Version consistency remains 0.3.8; `git diff --check` passed.

Native Windows/macOS visual appearance remains a manual check. This change does not produce an installer, push, or release.
