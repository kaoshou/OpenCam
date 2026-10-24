# Web player interface languages

## Confirmed scope

Replace bilingual player interface text with a single selected language: Traditional Chinese (zh-TW), English (en), or Japanese (ja). This affects newly exported web players, not existing export folders. Application version remains 0.4.1; no merge, push, release, website deployment, or automation is included.

## Application settings

Add a Web player languages section, distinct from application display language and subtitle language. Initially all three languages are enabled and the default is Auto-detect. Show three checkboxes and a default-language dropdown containing Auto-detect plus enabled languages. Prevent deselecting the last enabled language, with accessible explanatory text. Disabling the fixed default resets it to Auto-detect.

Persist settings using the existing settings service and save/cancel behavior. Old settings without this section get the initial defaults. Normalize malformed stored options: discard unknown/duplicate codes; an empty result restores all three; invalid or disabled default becomes Auto-detect. Validate again at the export boundary. Snapshot options when starting an export, including them in operation identity, so later settings edits cannot change an active export.

## Player selection

The compact language selector is at the top right and lists enabled languages by native name. Hide it when only one language is enabled and force that language. Otherwise prefer a valid remembered manual choice, then the configured fixed default or Auto-detect result. Store the manual choice safely; denied local storage must not break playback.

Auto-detect uses the browser's primary language preference (normally reflecting browser/OS settings): any Chinese variant selects zh-TW; Japanese selects ja; English and unsupported non-Chinese languages select en; missing/unreadable language selects zh-TW. If the result is disabled, choose the first enabled language in en, zh-TW, ja order. An obsolete remembered choice is ignored and normal default resolution resumes.

## Localization and behavior

Translate static controls, dynamic status/error messages, keyboard help, accessibility labels, tooltips, menus, and transcript-download interface/boilerplate. Set document lang to the active UI language. Keep language names recognizable and translate subtitle-language descriptions appropriately.

Switch in place without reload, playback seek/pause, search clearing, subtitle/transcript track changes, or reading preference resets. User titles, chapter names, captions, and transcript text are never translated. Transcript content retains its own language metadata even when generated download headings use another UI language.

All resources remain embedded/local: no translation service, CDN, or network requirement. Preserve existing compact reading menu, keyboard navigation, focus visibility, mobile/tablet layout, and offline/file URL behavior.

## Acceptance

Exercise every enabled-language subset, all defaults, remembered choices, invalid settings, browser-language mappings, denied storage, missing locale APIs, three-language catalog parity, and text/attribute escaping. Browser checks cover keyboard and screen-reader names, in-place state preservation, single-language hidden selector, 390/768/1280 widths, downloads, and offline export. Provide screenshots of representative desktop/mobile states. Automated checks are not a claim of complete accessibility certification.
