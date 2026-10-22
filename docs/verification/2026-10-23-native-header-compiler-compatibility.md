# Native header compiler compatibility, 0.4.0

During the explicitly authorized direct-release attempt, run39832881935 on
aa4d4a4f05f78521169d22f9ca2673a3644275d9 passed portable verification, website
deployment and macOS .NET tests. The final native compile/argument step failed;
macOS packaging was consequently skipped. This is not an application recording
assertion failure and no failed/old package is eligible for publication.

The complete completed-job log was retrieved from jobs/120013385172/logs and
retained at /private/tmp/opencam-v040-release.i2m6y2/macos-failed.log. Its full
exception at main.swift40 reports that the compiler cannot type-check the
eight-term Data concatenation expression in reasonable time. No exact Swift
compiler version was logged, so no particular compiler-version cause is claimed.

Minimal fix: build the AVC TrackEntry using explicit Data.append calls in the
same element order. Element IDs, values, codec private data, default duration,
dimensions, framing, timing, capture/encoder settings and safety bounds are
unchanged. No timeout, retry, test assertion or workflow gate is relaxed.

RED is the existing CI native compile test above. Local GREEN uses the same
helper test command with --synthetic: compilation/argument rejection, 60 actual
hardware-encoded generated frames, timestamp-gap preservation, AAC video-copy
muxing and complete decoding pass, exit0, in swift-track-green.log beside the
failed log. Version remains0.4.0 and diff/version checks pass. The fresh CI run
must confirm this compiler compatibility fix. Earlier full .NET1376-pass evidence
is unchanged C# source, not a new full-suite run for this Swift-only change.

The user explicitly said 「直接RELEASE」 after being told about unresolved
Windows FFmpeg source coverage. Proceed without asserting that review passed;
retain all existing notices/PENDING records. Public release notes remain the
same feature-only bilingual text. Use the new CI revision's original artifacts
and verify complete assets in a draft before publication. No schedule or extra
review agents are introduced.

## Follow-up: test runner utility portability

Run39834632086 on c46d49a passed the native Swift compile after the TrackEntry
change, then failed at tests/native/OpenCamScreenCaptureTests.sh15 with
`rg: command not found` / exit127. The complete job120019575097 log is retained
as macos-second-failed.log in the same release staging directory. This is a test
script utility dependency, not another compiler or application capture failure.

Reproduced locally before fixing with PATH=/usr/bin:/bin:/usr/sbin:/sbin,
running the real existing helper compile/argument command: exit127 in
native-portable-path-red.log. Replace only rg -q with the system grep -q,
keeping the exact anchored invalid-argument output check. The same no-ripgrep
PATH command then passes (native-portable-path-green.log). Initial synthetic
fixture attempts also excluded Node, which CI explicitly installs, and failed
with node: command not found; these remain failed in native-portable-synthetic
logs, not GREEN evidence. The corrected isolated PATH adds only a symlink to the
actual /usr/local/bin/node plus the FFmpeg tool directory, explicitly checks
that rg is still absent, and passes the complete --synthetic command with exit0
(native-no-rg-with-node-green.log). No assertion, compiler flag or workflow
gate is removed, and no new application/CI utility dependency is installed.

The earlier aa4d4a4 Windows run also failed one legacy video trial case:
PreflightTrialTests.TrialUsesCurrentEngineSamplesOnlyForSelectedInputs,
SystemAndMicrophone. Before-cleanup evidence: elapsed21254.5ms, checkingReady
4669.6ms, last20ms poll13685.3ms, TrialStarting, reads0, runningtrue, frames134,
seconds13.199. Both full managed-stack chunks and observer records were read.
The stack tool finished18:44:54, while continuation was observed18:45:05;
workers/idle threads do not establish thread-pool exhaustion or another root
cause. No Windows timing/MinThreads/threshold/assertion change is made or claimed
fixed by these Mac-only fixes. Core281pass3skip; Media1013pass82skip1fail.
Original evidence artifact12449908329 (445133 bytes) SHA256
5c1e3f0fe17bae7d14dfdfff47ff3e38488facdd2f9f664f2aaa8b923aa6c3e3
matches its API digest and aa4d4a4 source. Complete log, TRX/environment and
headless trace are retained in the release staging directory.
