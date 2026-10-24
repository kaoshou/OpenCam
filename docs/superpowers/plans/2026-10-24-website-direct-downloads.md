# Website direct downloads implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this small website-only follow-up in the existing isolated worktree.

**Goal:** Implement the user-approved three-button download block on both homepages.

**Architecture:** Replace hero actions with Windows portable/installer on the first row and macOS on the second. Put platform guidance, Releases and guide text links below. Use static version-pinned URLs for the published release, independently of development VERSION, so an unpublished version bump cannot silently point download buttons at missing files. No browser API request is needed.

**Tech stack:** Existing static website HTML/CSS/Node tests and Playwright regression suite.

**Spec:** User-approved chat design2026-10-24: green portable button, outlined other buttons, equal sizes; phone full-width stacked; tablet/desktop two columns; readable grey notes; bilingual and keyboard accessible.

## Global constraints

- Version remains0.4.1. No commit, merge, push, release, automation or desktop changes in this request.
- Download URLs must be the exact existing public Release attachments, verified read-only through GitHub API.
- Preserve unrelated main dirty spec/five plans and existing screenshot assets.

## Review focus

- Windows installers and portable ZIP must not be swapped; macOS is Apple Silicon arm64, not Intel.
- English button text must wrap safely at constrained desktop widths.
- 320px phone and200% text must remain readable without horizontal overflow.
- Replacing hero guide button must retain both local guide and original Releases links.
- Future development VERSION changes must not automatically move downloads to unpublished assets.

## Task1: download block

- [x] Add failing tests asserting three localized direct attachment links and retained supporting links/architecture copy.
- [x] Add a static published-release manifest; build validated explicit-version URLs; change hero markup/translations and responsive CSS only.
- [x] Add browser checks for desktop/tablet two-column order, phone stacked equal-width targets, keyboard focus and200% text; capture screenshots.
- [x] Run complete website tests/build, player/CI Node regression and browser suite; inspect desktop/mobile screenshots and record evidence.
- [x] Independent read-only review, then leave changes local for confirmation.
