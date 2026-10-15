# Recording Countdown and Bookmarks Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. User requests no extra review agents or repeated approval steps.

**Goal:** Add cancelable recording countdown and configurable Windows/macOS global bookmarks, durable per-project and visible in the editor.

**Architecture:** Recorder owns bookmark timing and sidecar persistence. UI owns countdown before capture and platform hotkey registration. Source-relative bookmark projection follows retained clips without changing video or manifest schema.

**Tech Stack:** .NET 8, Avalonia, existing authenticated IPC and bounded project storage, OS hotkey APIs.

**Spec:** `docs/superpowers/specs/2026-10-15-recording-countdown-and-bookmarks-design.md`

## Global Constraints

- Countdown off/3/5 seconds, default3; no countdown for paused resume.
- Bookmark default Ctrl+Shift+M; conflicts with both existing shortcuts rejected.
- Sidecar `opencam-bookmarks.json`, schema1, 10000 records/4MiB maximum; recorder sole writer.
- No media rewrites, project schema/version bump, merge, push or release.
- Do not touch original main dirty files; no independent review agents.

## Review Focus

- Cancel races with countdown expiry must not create capture/session.
- OS registration failures must not appear as successful shortcut settings.
- Pauses and source trims must not shift markers to unrelated content.
- Corrupt sidecar must not be overwritten or prevent media recovery.
- Replayed IPC or key repeat must not duplicate bookmarks.

### Task 1: Durable bookmark model and projection

Files: Core/Projects/ProjectBookmarks.cs, Infrastructure/Projects/ProjectBookmarkStore.cs, Core.Tests/ProjectBookmarkTests.cs.
Produces validated bookmark documents and source/session offset projection; store ReadAsync/WriteAsync for IProjectHandle.
- [x] Write tests for roundtrip, invalid documents, trim/reorder and media-safe persistence. Initial restricted run did not provide observed RED; see verification record.
- [x] Implement bounded validated atomic storage and rational timeline projection.
- [x] Run Core bookmark tests GREEN; include in the final local commit.

### Task 2: Recorder and authenticated IPC

Files: Recorder/Services/ProjectRecordingBookmarks.cs, ProjectIpcDispatcher.cs, Infrastructure/IPC/ProjectMessages.cs, Media.Tests/ProjectBookmarkIntegrationTests.cs.
Consumes Task1; produces AddProjectBookmark, GetProjectBookmarks, RenameProjectBookmark, DeleteProjectBookmark and paged projected results.
- [x] Test recording-only creation, actual recorder time, operation deduplication, corruption and edit projection; missing-command RED observed.
- [x] Add serialized recorder mutations/read commands with project/instance checks; persist before acknowledgement.
- [x] Run relevant tests GREEN; include in the final local commit.

### Task 3: Countdown and settings

Files: Core/Models/UserSettings.cs; UI/ViewModels/SettingsViewModel.cs; UI/Views/SettingsWindow.axaml; UI/Recording/RecordingCountdown.cs and window; MainViewModel recording flow; Media.Tests/RecordingCountdownTests.cs.
Produces persisted countdown/shortcut options and cancelable pre-capture flow.
- [x] Test countdown cancellation/elapsed-time expiry, defaults/settings conflict and no capture before completion. Settings-write failure RED observed.
- [x] Implement settings and monotonic countdown; wire after successful preflight, before any capture operation.
- [x] Run relevant tests GREEN; include in the final local commit.

### Task 4: Native hotkeys and editor

Files: Windows/Hotkey/WindowsGlobalHotkeyService.cs; macOS/MacOsGlobalHotkeyService.cs; MainViewModel; MainWindow; UI/Projects/ProjectWorkspaceBookmarks.cs; ProjectTimelineControl.cs; workspace view; localization.
Consumes Tasks2/3; produces real registration feedback, marker command, editor list and timeline flags.
- [x] Test key mapping/conflict/failure and editor seek/rename/delete; native key repeat/delivery remain manual checks. Pending marker IO blocking stop was RED then GREEN.
- [x] Implement native lifecycle and editor integration; verify SDK ABI and cleanup code. Physical native lifecycle still requires manual validation.
- [x] Run relevant tests GREEN; include in the final local commit.

### Task 5: Verify and document

Files: user guides and docs/verification/2026-10-15-recording-countdown-and-bookmarks.md.
- [x] Run Release solution tests/build, existing Node tests, version/diff checks. Final Core242 + Media786 pass,20 skip,0 fail; Node23/website30 pass.
- [x] Record actual results and manual native validation gaps; self-check integration only, no agent review.
- [x] Commit local changes, leave worktree available; no merge/push/package.
