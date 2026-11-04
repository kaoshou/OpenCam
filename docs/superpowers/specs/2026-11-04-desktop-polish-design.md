# Desktop polish: approved scope

The user approved local, incremental editor responsiveness, teaching UX and support improvements, including the proposed Settings → About support section. No recording-core replacement, project-format change, automatic updater, release, or upload is authorized.

## Approach

Keep existing preview/cache implementations rather than replace the editor. Defer background derived-media work during interactive use, preserve caches for metadata-only edits, and measure operations without private media data. Keep the existing annotation overlay and add accurate configured-shortcut hints plus regression coverage for repeated visibility changes. Add an independent support service: environment summary, log folder opening and opt-in diagnostic export from Settings → About. Update action opens official Releases in a browser; no download/install/background network service.

## Safety and platforms

Do not modify original media. No recordings, audio, subtitles, user settings, project files or credentials in diagnostic bundles. Export recent bounded app logs only after an explicit save action; explain that paths/device names may remain and sharing is separate. Tests must cover symlinks and oversized logs. No upload tool. Localized copy in Traditional Chinese, English and Japanese. Existing Wayland restrictions remain.

## Alternatives

A new editor pipeline and automatic updater would broaden regression/security scope; deferred. Reuse platform folder-opening and settings UI patterns. The existing development checkout contains unrelated GNOME prototypes; preserve them without staging or committing them.

## Acceptance

Playback/scrubbing do not dispatch background waveform or thumbnail requests; visible clips take priority, bounded caches remain. Configured shortcuts appear in tooltip/accessibility text, F10 disabling preserves marks and Finish clears marks. Diagnostic actions are accessible from About with localized status/error feedback, cancel does nothing, export never overwrites the selected existing file, and public update navigation never changes app installation. Automated tests do not substitute for physical Windows/Linux mixed-DPI tests.
