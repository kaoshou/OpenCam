# Recording project ownership and persistence

## Decision

The Recorder process owns one writer lease and serializes project operations. UI communicates through existing authenticated, size-bounded IPC; it does not write manifests. Ordinary quick-recording configuration cannot select the internal project completion capability.

`project.opencam` contains immutable source records and non-destructive clip references. Sources remain under `sessions/Sessions/<sessionId>/segment_NNN.mkv` to preserve existing session-path rules; `sources` is reserved for later imports. Paths are relative and opened through pinned directory handles with link/hardlink rejection. Session metadata stores source leaves so moving the entire project is supported.

Saving writes a flushed sibling temporary file and atomically replaces the manifest, retaining a validated previous version. An OS lease blocks another cooperating writer. A killed-process test verifies readable committed state; **this is not a claim of directory-fsync durability or guaranteed recovery after power loss/storage-controller failure**. Backup restore requires confirmation and retains the damaged bytes in a uniquely named file.

The coordinator commits only finalized sources, with a journal intent before manifest update and completion after it. Source identity is stable per project-relative path. Reopening reconciles intents and project-owned session metadata, including a pending working file not yet recorded in the segment list. Invalid/empty/missing material blocks progress; nothing is silently deleted.

Actual MKV packet PTS/duration bounds and the stream time base are obtained by feeding a safely opened source stream to ffprobe stdin, with only the pipe protocol enabled. This avoids passing untrusted manifest paths to an external process. It uses constant packet-summary memory, but reads the source and has a five-minute cancellable probe limit. Hash validation currently re-reads sources at reopen/resume: long-project latency remains a performance acceptance item.

Undo/redo stores immutable clip snapshots, not media copies. Appending a recording extends existing history snapshots so undoing an earlier rename cannot remove the new recording. Saving leaves history intact; restarting/reopening intentionally starts empty history. Cross-launch rollback is backup recovery, not an edit undo stack.

## Compatibility and UI

`QuickMp4 = 0` is the old-session default. `KeepProjectSources` bypasses automatic remux and cleanup for all shared recorder stop paths. Existing recovery already retains originals and skips completed sessions. Project and quick capture controls are mutually exclusive; the existing capture settings remain available through the parent window.

The Phase B workspace is an owned native window, not the discarded editing-preview branch and not a browser wrapper. It follows OS theme without changing the main application's theme. Precise timeline editing, preview and MP4 export are separate Phase C work.

IPC retries retain the original operation ID and payload. A missing response causes status lookup; a reachable unknown operation can be retried with the same ID after the serialized status query. Requests are bound to a recorder instance so process restart cannot replay an old command. Unknown/unreachable status blocks further edits/close until resolved. Clip lists are paged in batches of at most 100; request metadata is additionally limited to 64 KiB and the authenticated transport remains capped at 1 MiB.

Project session directories are allocated through pinned handles; metadata saves retain those handles and rebase paths after a whole-project move. FFmpeg project output goes through stdout directly into an exclusively-created bound stream. This adds a pipe/copy stage, not a second whole-file copy. MKV may lack a seek-back duration header; final acceptance uses packet PTS. Quick mode retains its path-based output. High-throughput and hardware acceptance remain pending.

Status queries reconcile background recorder completion/failure under the coordinator lock. The workspace polls while recording, retains the actual stop reason and refreshes saved clips. Failed finalization has a project-ownership-checked retry path, including engine shutdown before metadata persistence; it does not relax quick-mode state-machine transitions.

Display snapshots are compared on same-session resume. Changed names/indices/geometry block resume and require a new session with a confirmed target. This is deliberately conservative; truly indistinguishable platform display identities cannot be detected by this layer alone.

## Acceptance limits

See [local acceptance](../acceptance/2026-10-08-project-phase-b.md). macOS interactive launch is currently blocked by native RenderTimer `-6661`; Windows and real-device recording are unverified. Do not promote this development branch or describe the editor as complete based on mock/synthetic tests alone.
