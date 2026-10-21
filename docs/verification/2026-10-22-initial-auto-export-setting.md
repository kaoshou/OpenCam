# Auto-export setting before the first recording

The home checkbox was disabled until a project existed because its enabled
condition required CanResume and HasRecordingProject. Its setter also required
an existing workspace project, so changing the enabled binding alone would
not preserve the user's choice.

The home view now holds the choice while no project is open (default true).
Creating a project or starting new recording content freezes and sends the
choice through the existing AutoExportOnStop request field. The recorder saves
that policy before starting capture. Existing projects retain their own saved
policy; recording, busy, unknown-status and editor locks remain enforced.
There is no new global preference or project schema change.

Regression uses the actual home view model, project client/dispatcher,
coordinator and disk store with the established test recorder. It covers both
direct first recording and explicit project creation, disabling auto-export,
refusing changes while recording, stopping without an export, and reopening
the saved project with the same policy.

Before the change both cases failed at initial CanChangeProjectExportPolicy
(`/tmp/opencam-autoexport-red.log`). Afterward both cases and related existing
tests passed: 10 passed, zero failed/skipped (`/tmp/opencam-autoexport-green.log`).
Full Release solution build/test exited 0: Core 255 passed; Media 997 passed,
20 skipped, zero failures (1,252 passed total). Media duration 5m14s.
Log: `/tmp/opencam-autoexport-full.log`. SDK 8.0.425, FFmpeg 7.1.2.

Manual UI check: launch with no open project, toggle the home option before
recording, record/stop and confirm that disabled saves the project without
automatic MP4 output. Reopen an existing project and confirm its own setting.
This turn does not claim a native Windows UI run or replace release assets.
