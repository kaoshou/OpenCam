# Web accessibility regression checks

From this folder: `npm ci`, then `npx playwright install chromium`.
Run `FFMPEG_PATH=/path/to/ffmpeg npm test` (or use an ffmpeg on PATH).
The test builds the real website and opens the shipped embedded player in Chromium with locally generated, silent synthetic media. It never captures the screen, microphone, or private user media. It creates a temporary evidence directory and prints its path; set `OPENCAM_A11Y_OUTPUT` to an empty directory to retain screenshots/results at a known location. Browser installation can be redirected with `PLAYWRIGHT_BROWSERS_PATH`.

The default-layout comparison reads baseline commit `018aee5` from local Git history; use a full checkout containing the v0.4.0 source. No production dependency or exported package depends on Playwright/axe-core. Tests are intentionally opt-in; the normal recording CI and its gates are unchanged.

Checks include complete language-specific HTML/TXT downloads despite filtering/pagination, literal untrusted text, preferences/storage denial, keyboard/CC/fullscreen, focus-safe follow, file:// operation, 320px and enlarged-text reflow, expanded settings plus >100 cues in a short viewport, and WCAG-tagged axe scans of the player/reading document and bilingual website/guide. Viewport and text-size tests are not a substitute for actual OS/browser zoom or screen-reader testing. A clean axe scan is not a complete WCAG conformance assessment.
