# Add videos to the editor

Approved scope: the user asked to try the proposed import flow. Keep version 1.1.0 and prepare a reviewable implementation without publishing.

1. Add a local-file import job using existing bound-file media backends. Probe input, preserve H.264 when compatible, convert other SDR video / non-AAC audio as necessary, and produce project-owned MKV. Reject unsupported HDR and ambiguous audio tracks clearly rather than choosing silently.
2. Stage and verify media before adding one clip at the timeline end. Keep the external original unchanged. Retain the source through Undo and project reopen. Use a background job with progress/cancel so IPC polling remains responsive.
3. Add a localized Add video button and file drag/drop in the editor, serial multi-file import, progress and cancellation. Preserve the existing canvas and use current preview/export composition.
4. Test real MP4/MOV/MKV imports, silent media, conversion, corrupt input, cancellation, source ownership, Undo/Redo and reopening; test mixed-source export and decoded content. Build both platform projects and record physical Windows/UI scenarios separately.

No recording capture implementation changes are required. Imported subtitle streams are not automatically attached; existing subtitle export synchronization checks remain in force.
