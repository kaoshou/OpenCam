# Web player interface languages — verification

## Scope and environment

- Version: 0.4.1; development branch only. No merge, push, Release, website deployment, or scheduling.
- macOS arm64, .NET SDK 8.0.425, FFmpeg 7.1.2.
- Three enabled interface languages with auto/fixed defaults; export-time policy snapshot; independent caption/transcript language.
- Local catalogs only; no application dependency changes.

## Evidence

- Baseline player tests: 30 passed.
- Language resolver and in-place switching: observed 2 new failures before implementation, then 32 passed.
- Toggle accessible actions: observed Wide screen instead of Restore layout; corrected labels and fullscreen titles.
- Caption language metadata: observed missing lang before correction; now independent of interface language.
- Latest Node player tests: 35 passed, 0 failed; /tmp/opencam-languages-player-green.log.
- Browser integration: 22 groups passed, 0 failed; /tmp/opencam-languages-browser2.log.
- Browser evidence: /var/folders/95/1sbw_nz57fschyb8czpsmxp40000gn/T/opencam-reading-m0j9gO/results.json.
- Screenshots copied to /Users/yhcheng/Documents/Codex/OpenCam-test-packages/0.4.1-web-player-languages/.
- Settings/export initial targeted tests: Core 2 and Media 16 passed. The Core initial failure was missing new API at compilation; the settings/export cases were written first but first executed after implementation, not an observed behavioral RED.
- Full .NET regression and final integration correction: in progress; do not treat this record as complete until results below are updated.

## Browser coverage

Traditional Chinese, English and Japanese control text, document language, dynamic names, retained playback position/search/caption choices, remembered selection, hidden single-language selector, Chinese browser variants, unsupported locales, denied storage, absent Intl.DisplayNames, HTML/TXT full transcript downloads, local files and offline behavior. Desktop 1280, tablet 768 and phone 390 screenshots inspected; existing additional 320 px, rotation, touch emulation, keyboard, menu and axe checks retained.

Browser fixture video is synthetic. Screenshots contain no personal desktop recording. Automated axe and Chromium emulation do not certify complete accessibility conformance or replace physical device testing.

## Manual checks

On Windows and macOS, open Settings, select one/two/three web interface languages, save, change FPS, reopen Settings and export. The choices must persist; one enabled language must hide the web selector. Existing exported folders must remain unchanged.

On physical iOS Safari and Android Chrome, open a new export, switch interface language during playback, search the transcript, open reading settings and download HTML/TXT. Expect no playback reset, horizontal overflow or lost keyboard/touch focus. These physical-device and native desktop interaction checks require manual validation.
