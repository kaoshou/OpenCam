# Audio recheck identity initialization race

## Evidence and cause

The user's diagnostic Windows log (2026-10-21, latest attachment ending in
`954314d3-3d4e-4b21-8598-d6b36e5573ad/app-20261021.log`) records microphone-only
trial and playback, followed by recheck at 23:44:32.293. The first failure is
`Preflight audio endpoint identity changed` at 23:44:32.295. New attempts at
23:44:36.581 and 23:44:44.080 produce the same warning within 1–2 ms.
The subsequent cancellation exceptions follow EndPreflight and are not the
first failure. There is no unexpected-progress-EOF diagnostic in this run.

PreflightCoordinator deliberately publishes the operation before starting the
session, allowing responsive status polling during initialization.
PreflightAudioSession published its configuration before the initial native
endpoint identity query returned. A concurrent ReadStatus could compare the
still-null baseline against the unchanged endpoint and permanently latch
PreflightAudioUnavailable. Once latched, the session suppresses meter reads
even if capture starts successfully. This explains how the preflight can
remain unavailable while a separate formal recording still captures audio.

The new test holds the first identity query at a barrier and polls the real
session from another execution path, with every query returning the same
device. Before the fix, microphone-only, system-only and combined sources all
fail with PreflightAudioUnavailable (3 failed, 1 passed). Thus an actual device
change is not required to reproduce this failure. The user log identifies the
same failure branch; Windows hardware confirmation remains required.

## Fix and verification plan

Publish a volatile initialized flag only after the identity baseline has been
assigned; ReadStatus compares identities only after acquiring that publication.
Do not wait for capture acknowledgement to begin detecting real device changes.
Keep cancellation, capture acknowledgement, endpoint-change invalidation,
recording leases and optional trial behavior intact.

Regression coverage includes unchanged endpoints during initial enumeration,
real endpoint changes after enumeration but before capture acknowledgement,
and existing changes after startup. The focused suite passed 32 tests with no
failures or skips (`/tmp/opencam-recheck-identity-green.log`). Initial RED is
`/tmp/opencam-recheck-identity-red.log`. A first sandboxed test invocation could
not bind the test-host socket; RED results are from the successful test-host
launch with the required permission.

Full solution Release build/test exited 0: Core 255 passed; Media 995 passed,
20 skipped, zero failures; total 1,250 passed. Media duration 5m13s.
Log: `/tmp/opencam-recheck-identity-full.log`; SDK 8.0.425, FFmpeg 7.1.2.

Windows verification package is locally cross-compiled with SDK 8.0.425 from
base f8c1bc92ea1dac4baa69650bddb93f00661fa3be plus the supplied patch.
It is not an Actions-built release. Version remains 0.3.12; public release and
release notes have not been modified. Existing diagnostic logging is retained.
Windows publish exited 0; PE x64 and bundled source/license notices verified;
ZIP integrity test passed. ZIP SHA256:
`bbbeb3b2dd1067dc63443bb77406309c520c3df6f08b2305a00da8e3748d86819`.
Executable SHA256:
`4bdc5775233824a16be4090a5f2852fbe231b85793695d2480ea876247e28da7`.
Build log: `/private/tmp/opencam-recheck-fixed.twxiIQ/publish.log`.

## Manual Windows check — requires validation

2026-10-22: the user tested the fix package and explicitly confirmed that
recheck now receives audio normally. This confirms the reported Windows
recheck scenario, not every hardware configuration in the checklist below.

Prerequisites: no active recording; exit the installed application and extract
the verification ZIP into a separate folder. Run its OpenCam.exe.

Select microphone only. Trial for five seconds, play it, and recheck three
times, including rechecking while playback is active. Expect preparation to
finish, microphone levels to return, and another trial to be available.
Repeat with both sources if available. Audio testing remains optional and
the explicit start-recording action remains available. A real device change
must still invalidate the old audio check.

Failure: persistent failed check, waiting meters after startup, or unavailable
trial. Collect the latest application log and reproduction time, without audio
or video files. No native Windows result is claimed from macOS automated tests.
