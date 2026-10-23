# Reading settings overlay implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this small follow-up in the existing isolated checkout.

**Goal:** Implement the approved reference design: a closed Reading settings overlay, with language/search outside, no nested settings disclosure, and transcript filling remaining space.

**Architecture:** Keep the embedded single HTML player and existing reading/download handlers. Use a non-modal labelled dialog with explicit close, outside click and Escape, focus return, viewport-bounded fixed positioning and internal scrolling. Retain blue OpenCam styling and video geometry.

**Tech stack:** Existing HTML/CSS/JavaScript; Playwright browser regressions.

**Spec:** Approved proposal in chat on 2026-10-23, including phone/tablet support.

## Constraints and review focus

- No application dependencies, schema, version or release change.
- Test phone portrait/landscape (320/390/844px), tablet (768/1024px), desktop; controls must remain reachable without horizontal overflow.
- Opening settings must not shrink transcript; closing returns focus where appropriate.
- Outside click must preserve its own target focus; Tab leaving dialog closes it without trapping users.
- Viewport resize/scroll must not leave an off-screen overlay.
- Storage denial, absent captions, downloads and keyboard player controls retain existing behavior.

## Task: overlay and responsive layout

- [x] Add failing real-browser tests for toggle, dismissal/focus, unchanged transcript geometry, responsive bounds and reachable downloads.
- [x] Replace nested details with one labelled settings dialog and button, reposition within viewport, close on outside/Escape/focus leave/tab switch.
- [x] Make toolbar intrinsic-height and transcript fill remaining sidebar; preserve >=half sidebar at tested sizes.
- [x] Adapt existing browser cases to the approved single-level overlay without dropping download/contrast/layout coverage.
- [x] Run Node player/CI, complete browser suite, rebuilt embedded-export tests; inspect phone/tablet/desktop screenshots and update bilingual documentation/evidence.
- [x] Leave changes local for user review; no commit/push/release in this request.
