# Audio confirmation timing preference

User specified a three-choice dropdown: Off, recording start only (default),
and recording start plus resume. This is a UI preference, not a project schema
change. Version remains 0.3.12.

## Behaviour

| Mode | Start a recording | Resume from pause |
| --- | --- | --- |
| Disabled (0) | Skip audio confirmation | Skip audio confirmation |
| StartOnly (1, default) | Show audio confirmation | Skip audio confirmation |
| StartAndResume (2) | Show audio confirmation | Show audio confirmation every time |

New/append/insert recording actions count as new starts; only the existing
paused-resume path is classified as resuming. With no selected audio sources,
all modes skip the optional audio screen. All accepted paths still report
recording preparation and retain permissions, project/output validation,
capture ownership and actual recorder start confirmation. Trial audio remains
optional and the existing recorder/media pipelines are unchanged.

## Persistence and localization

UserSettings stores RecordingPreflightMode. Missing or unknown modes default to
StartOnly. The setter-only legacy RecordingPreflightEnabled JSON alias reads
old settings: false maps to Disabled, true maps to StartOnly. Explicit new mode
wins in either JSON property order. Saves emit the new mode without rewriting
the old toggle. Main-window automatic settings saves preserve all three modes.

Settings loads/saves/resets the dropdown and preserves the selected mode when
language changes. Reset selects StartOnly. Both languages, README and user
guides describe the same timing rules; no release notes were changed.

## Verification evidence

Before implementation, real executable tests failed with Core 9 / Media 5
failures (Media 6 passing), exposing missing mode persistence, missing dropdown
and wrong start/resume behaviour (`/tmp/opencam-preflight-mode-red.log`).
After implementation, focused checks passed Core 20 / Media 11 with no
failures/skips (`/tmp/opencam-preflight-mode-green.log`). Tests use real settings
file IO and the real project/recorder coordinator with a synthetic engine,
asserting audio confirmation calls, recording state and created segment count.
The final suite additionally exercises a second pause/resume and language
switches while retaining the selected dropdown value.

Full-suite command and result are retained in `/tmp/opencam-preflight-mode-full.log`.
Website checks/build are retained in `/tmp/opencam-preflight-mode-website.log`
and `/tmp/opencam-preflight-mode-website-build.log`. Final package verification
is recorded beside the delivered Windows archive.
