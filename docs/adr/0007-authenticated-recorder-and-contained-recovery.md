# Authenticated recorder and contained recovery

## Context

The recorder accepts sensitive commands from local processes. A random pipe name is
a locator, not authentication. Recovery metadata may come from shared storage.

## Implementation plan

1. Require a matching UI application executable and a 256-bit launch key delivered
   through redirected stdin; never put keys in argv. Verify the actual IPC peer PID
   in both directions, not just the caller-supplied parent argument.
2. Authenticate server challenges, requests and responses with HMAC-SHA256.
   Fresh server nonces prevent replay across connections. Bound frame size,
   unauthenticated read duration and pending connections, and acquire the command
   gate only after authentication.
3. Bind loaded sessions to their actual directory, reject external/link inputs,
   generate recovery output names, and stage recovered media privately before
   publishing without overwrite. Preserve legacy MKVs and partial recovery.
4. Add adversarial and legitimate-control tests; run Release build, focused tests,
   complete test suites and available macOS recording acceptance.

## Constraints

This does not defend against process injection, a writable/replaced application
installation, a compromised OS or an attacker who can read another process's
memory. macOS TCC attribution requires runtime testing. Windows hardware behavior
must be checked on Windows. Private staging does not isolate a malicious process
with the same user identity. User-created ancestor symlinks are rejected; macOS
system aliases /var and /tmp are normalized to /private.

## Candidate status / unresolved boundary

This is not yet a verified release. The candidate rejects external metadata paths,
snapshots regular singly linked media through an opened handle before probing,
atomically replaces metadata entries, and publishes MP4s without overwrite.
Snapshots require additional free space on the system temporary volume; failures
leave original MKVs untouched.

Directory operations now use BoundDirectory: openat/no-follow, renameat and
exclusive output creation on Unix; retained ancestor handles denying delete
sharing on Windows. Metadata, source reads, publication and lock acquisition share
this boundary. macOS tests replace the session, Sessions and recordings root after
opening and verify that writes remain in the original directory. Hard-linked lock
files are rejected without modifying their targets or throwing out of recovery.

macOS native helpers write PCM to redirected stdout. Only duplicated anonymous
read descriptors are inherited by FFmpeg, then closed in the parent; originals
remain owned by the capture service for encoder retries. Metering stays on stderr,
and FFmpeg stdin remains available for graceful stop. Real FFmpeg tests cover two
independent inputs, mixing, nonzero output and rejection of a later reader without
the inherited descriptor. This does not establish TCC or hardware correctness.

Windows and Linux native boundary tests have not been executed on those platforms.
The current automated results do not substitute for real UI/child recorder
acceptance, macOS TCC testing, or Windows/Linux runtime validation.
