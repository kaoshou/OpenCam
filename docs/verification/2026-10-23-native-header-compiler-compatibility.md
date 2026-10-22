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
