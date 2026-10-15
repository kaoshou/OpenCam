# Microphone silence reminder verification

The development change adds an advisory only. It does not send recorder commands, change capture configuration, reopen audio devices or store audio. Default-on settings are persisted with existing user preferences. Continuous classified microphone silence for 30 seconds activates the warning; system audio cannot mask microphone silence. Pausing, stopping, sound returning, unavailable data, a polling gap over one second or disabling the feature resets the interval.

## Automated checks

Core detector first failed the threshold assertions with a no-op implementation (`/tmp/opencam-silence-red.log`). UI/settings first failed activation and default-on assertions (`/tmp/opencam-silence-ui-red.log`). Floating presentation first failed ordering/cleanup assertions (`/tmp/opencam-silence-overlay-red.log`). Fallback visibility also failed before implementing mutually exclusive in-app/floating presentation (`/tmp/opencam-silence-fallback-red.log`). Related tests passed after implementation (`/tmp/opencam-silence-focused.log`). Settings verification uses a real temporary settings file, including an old file without the new preference.

Final Release build and complete solution tests: Core 248 passed; Media 792 passed, 20 skipped; zero failures. Environment: macOS arm64, .NET SDK 8.0.425, FFmpeg 7.1.2. Log: `/tmp/opencam-silence-final.log`. Player tests: 23 passed (`/tmp/opencam-silence-player.log`); website tests: 30 passed (`/tmp/opencam-silence-website.log`). Version consistency and diff check passed. Existing NU1900 audit-network and Avalonia preflight-constructor warnings remain; no online vulnerability-audit success is claimed. Physical capture and microphone acceptance below remains unperformed. An author integration check was used without extra review agents, following the user's preference.

## Platform behavior and manual acceptance

Windows floating warnings start with zero opacity. On Windows 10 2004 or later, the platform helper requires DWM composition, successful `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` and a matching readback before revealing the window. Failure or exception closes it without revealing it and leaves the in-app advisory. No repeated window creation occurs during a continuous warning episode. The window does not activate, has no taskbar entry or sound, and binds to the current Traditional Chinese or English UI language.

The current macOS FFmpeg AVFoundation video path cannot confirm floating-window exclusion, so it does not create a floating warning. In-app advisory content can be recorded if OpenCam itself is within the capture region. This limitation is intentional; the feature does not migrate the capture backend. See [Microsoft display affinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) and [Apple legacy window sharing type](https://developer.apple.com/documentation/appkit/nswindow/sharingtype-swift.enum).

REQUIRES MANUAL VALIDATION on actual hardware:

1. Enable microphone capture and reminders. Record at least 35 seconds of quiet audio, then speak. Check that the warning appears after sustained silence, disappears on sound, and recorded video continues without a pause, stop or restart.
2. Repeat with system audio playing, microphone disabled, reminder disabled, pause/resume and a newly started recording. Only microphone silence with the feature enabled may warn; pause time must not count.
3. On supported Windows, record the warning display using both Compatible GDI and Desktop Duplication capture modes, at mixed DPI and negative monitor coordinates. Inspect extracted video frames while the warning is visible: neither warning pixels nor a black replacement rectangle should appear. API readback alone does not establish this acceptance result.
4. On macOS and on Windows when exclusion fails, verify there is no floating warning, no focus change and no audio notification. The in-app advisory is the fallback, not a promise of invisible in-app content.
5. Save/reopen settings in both languages; verify the default-on option, disabling/re-enabling and localized wrapped text. Disconnect the microphone or interrupt telemetry: missing data must not be labeled continuous silence. Collect recording, timestamps, UI screenshots and logs.

Version remains 0.3.6. No installer, merge, push or Release is part of this change.
