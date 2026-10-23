# Web reading accessibility implementation plan

> Execute inline with executing-plans and test-driven-development. The user requested implementation and screenshots after the scoped proposal; no further design questionnaire is needed.

**Goal:** Improve exported player reading/downloads and public website accessibility without changing the default visual identity, desktop editor, version, or release.
**Architecture:** Extend the existing embedded player template; no application/runtime dependency or export schema change. Reuse CC appearance controls. Add optional transcript preferences and safe HTML/TXT downloads from the full selected track. Website changes remain CSS/semantic and keyboard improvements.
**Spec:** The preceding user-approved conversational scope: captions, reading preferences, keyboard support, W3C-informed transcripts, website/guide, preserve layout, show actual screenshots.
**Stack:** Existing vanilla JS/HTML/CSS, Node tests; real Chromium/axe checks using existing local test tools, synthetic media only.

## Constraints and decisions
- No push, merge, version bump or release in this task. Existing unrelated main changes stay untouched; reuse clean focused-editor-revision worktree.
- Download HTML and UTF-8 TXT of the complete selected language, independent of search/pagination and CC visibility. Optional sparse timestamps; chapter headings, safe literal content, no fabricated speaker/visual descriptions. Document that captions alone are not a complete descriptive transcript.
- Reading controls: 100–200% text, 1.5/1.8/2 line height, default/dark/warm high-contrast backgrounds, reset. Store bounded validated preferences where available; denial is harmless.
- Keep default caption appearance and layout; enhance CC explicit close, keyboard focus return, Home/End tabs, skip links and help. Playback follow must not replace focused transcript rows.
- Aim at WCAG 2.2 AA, not a certification claim. Check normal text contrast 4.5:1, UI contrast 3:1, zoom/reflow, focus, keyboard and forced/reduced modes.

## Task 1: complete — Player reading and keyboard (interfaces: existing subtitle tracks → safe Blob HTML/TXT and transcript CSS preferences)
- [ ] Add real-runtime tests for preference validation/reset, entire multilingual download under filtering, escaping, no-subtitle disabling, focus-safe follow and keyboard close/tab navigation. Run RED.
- [ ] Extend player.html controls, styles and runtime; no new export files. Run full player tests GREEN.

## Task 2: complete — Website and documentation (independent player/site boundary)
- [ ] Add real-browser failing checks for keyboard skip focus, language attributes, contrast and narrow/zoom overflow on home and guide. Run RED.
- [ ] Preserve design; improve small-text/focus colors, navigation reflow, content word wrapping, target sizes, forced/reduced modes. Document reading/download and source-content limits in bilingual guides and WEB_PLAYER_EXPORT.
- [ ] Run complete website tests and build GREEN.

## Task 3: complete — Verification and screenshots
- [ ] Browser tests: safe downloads, settings persistence/storage denial, keyboard/focus, 1000-cue full download, no cues, long multilingual text, 320px/200%/400% reflow, forced colors and axe checks. Compare default layout before/after and inspect screenshots.
- [ ] Run full Node player/CI/site tests and .NET solution Release build/test, report actual outcomes including failures/skips. No hardware PASS claims.
- [ ] Self-review source diff, record evidence and deliver screenshots of default player, reading settings, downloaded document and website. Leave work unmerged for user inspection.

## Review focus
Untrusted imported text/HTML; entire-track download not just filtered rows; keyboard focus across dynamic paging; 200% text and tiny viewports; localStorage/file:// denial and offline assets. All are covered by runtime or browser tests above.

Completion ledger: see docs/verification/2026-10-23-web-reading-accessibility.md for RED→GREEN evidence, independent review, clipping fix, final suites and decisions. User requested local implementation/screenshots, so changes deliberately remain uncommitted/unmerged rather than taking a new merge/push action.
