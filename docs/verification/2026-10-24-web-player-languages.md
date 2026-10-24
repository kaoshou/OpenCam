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
- Latest Node player tests: 35 passed, 0 failed; /tmp/opencam-languages-player-final.log.
- Browser integration: 22 groups passed, 0 failed; /tmp/opencam-languages-browser-final.log.
- Browser evidence: /var/folders/95/1sbw_nz57fschyb8czpsmxp40000gn/T/opencam-reading-i46GQM/results.json.
- Screenshots copied to /Users/yhcheng/Documents/Codex/OpenCam-test-packages/0.4.1-web-player-languages/.
- Settings/export initial targeted tests: Core 2 and Media 16 passed. The Core initial failure was missing new API at compilation; the settings/export cases were written first but first executed after implementation, not an observed behavioral RED.
- Main-window preference persistence regression: observed ja-only resetting to all three; fixed Load/Apply/Persist snapshots, then targeted settings/export tests 19 passed.
- Export integration confirms generated policy through IPC and rejects reuse of an operation ID with a different policy.
- Final full Release regression: Core 340 passed; Media 1188 passed, 21 skipped, 0 failed (5m44s). SDK 8.0.425, existing restored dependencies; no new online dependency audit claimed.
- Version consistency 0.4.1 and git diff whitespace checks passed. Final independent review is recorded separately.

## Browser coverage

Traditional Chinese, English and Japanese control text, document language, dynamic names, retained playback position/search/caption choices, remembered selection, hidden single-language selector, Chinese browser variants, unsupported locales, denied storage, absent Intl.DisplayNames, HTML/TXT full transcript downloads, local files and offline behavior. Desktop 1280, tablet 768 and phone 390 screenshots inspected; existing additional 320 px, rotation, touch emulation, keyboard, menu and axe checks retained.

Browser fixture video is synthetic. Screenshots contain no personal desktop recording. Automated axe and Chromium emulation do not certify complete accessibility conformance or replace physical device testing.

## Manual checks

## Independent review

Fresh read-only whole-branch review of d7b01e9..6db535f found no Critical/Important issues. Reviewer independently ran 35 Node tests and checked identical 106-key catalogs.

Deferred minor: exiting wide layout through a chapter/transcript skip link leaves the wide button's accessible name as Restore layout until refreshed; visual layout, tooltip and operation are correct.

Coverage limits: new browser switching cases use selectOption with paused video; actual keyboard language selection during playback remains manual. Node tests simulate playing state. Catalog parity was independently inspected, not enforced by a committed parity test.

Unchanged settings I/O fallback/concurrent saves and legacy failed-export retry behavior remain outside this feature; no introduced failure was established. JavaScript-disabled fallback stays Chinese, because policy selection requires JavaScript.

### Remaining manual checks

On Windows and macOS, open Settings, select one/two/three web interface languages, save, change FPS, reopen Settings and export. The choices must persist; one enabled language must hide the web selector. Existing exported folders must remain unchanged.

On physical iOS Safari and Android Chrome, open a new export, switch interface language during playback, search the transcript, open reading settings and download HTML/TXT. Expect no playback reset, horizontal overflow or lost keyboard/touch focus. These physical-device and native desktop interaction checks require manual validation.
