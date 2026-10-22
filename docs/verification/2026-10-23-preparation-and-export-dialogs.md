# Preparation failure and export destination UX

User authorized closing items 3 and 4 of the final-polish review. Version remains
0.3.12; previous native capture, cursor, FPS and wording work is preserved.

## Recording preparation

Known failure now sets both window title and heading to the localized
“Unable to start recording”, changes Cancel to Close, and clears the irrelevant
wait hint. The failure body remains visible and dismissal does not call the
recording/preflight IPC again. Preparing and unconfirmed states retain the
existing close/start protection and disabled dismissal.

Extended the actual headless failure-window test in both languages, including
clicking Close. Before the window fix, the executable regression run had two
failures because the title still said Preparing, with eight other tests passing
(`/tmp/opencam-final-ui-behavior-red.log`). The first attempted run before the
new destination helper existed was a compile failure, not a runtime RED result.

## Export destinations

Whole-project, clip MP4/GIF and legacy MP4 pickers share one destination loop.
The native save picker requests no overwrite prompt and its localized title
asks for a new filename. Existing files, directories and links are refused at
the picker boundary. The user may choose another name or cancel; no export is
started by that conflict. Repeated conflicts remain in the picker flow.

Recorder's exclusive-create/no-overwrite rules remain unchanged, including
protection when a destination appears after the UI check. No existing file is
deleted, replaced or opened for writing by the new UI checks.

Tests cover existing files for both formats, intact original bytes, repeated
directory conflicts, cancellation at each picker/conflict step and actual
bilingual conflict-dialog buttons. Relevant localization, preparation, preflight,
GIF settings, export menus and manual/existing-export checks passed:
Core 11 and Media 68, zero failures/skips (`/tmp/opencam-final-ui-green.log`).
Native Windows/macOS save-panel behaviour still needs physical confirmation;
headless tests use injected picker results and real temporary files.
