# OpenCam Official Website and GitHub Pages Design

## Purpose and success criteria

Create an official website for OpenCam that helps a new visitor understand the recorder, see the real interface, download the current release, and read complete usage instructions without leaving the website. The site supports Traditional Chinese and English with a visible language switch. It is published from the existing `kaoshou/OpenCam` repository to its GitHub Pages project-site URL, without changing the desktop application's release workflow.

Success means that both languages offer equivalent homepage and guide content; all four existing screenshots are available on the site; downloads point to the official GitHub Releases page; the complete guides are rendered from the existing Markdown sources; the site works on desktop and mobile; and the live GitHub Pages deployment can be opened and checked after publication.

## Current project context

- The repository contains `docs/USER_GUIDE.zh-TW.md` and `docs/USER_GUIDE.en-US.md`, with matching guide sections. These remain the canonical guide sources.
- Four genuine Windows UI screenshots are in `docs/images/`: a main-window and settings screenshot for each language. The main-window screenshot visibly shows an older version number, so it must not be presented as a screenshot of the current release without updating it.
- The app icon is in `src/ScreenRecorder.UI/Assets/app_icon.png`.
- The existing `build-and-release.yml` publishes Windows and macOS binaries, not a website.
- The GitHub Pages API currently returns 404 for this repository, indicating no configured Pages site was found during discovery. Deployment may require enabling Pages with GitHub Actions as its publishing source.

## Chosen approach

Keep the website as a small static-site project in a dedicated `website/` directory. Use semantic HTML, local CSS, and minimal JavaScript for the language preference and optional interface behavior. A small build-time Markdown renderer converts the two canonical user guides into website pages. The build selects only website files, icon, screenshots, and guide content; it does not publish the rest of `docs/` or repository internals.

The build process may use a narrowly scoped Markdown package, but it does not require a client-side framework or a runtime content API. A site visitor receives finished HTML. This keeps Pages deployment simple and makes the complete guide usable without JavaScript.

Alternatives considered:

1. **Astro or another full static-site framework:** good for a larger documentation and blog program, but adds dependency and configuration overhead not needed for this launch.
2. **GitHub Pages branch `/docs` with Jekyll:** quick for a basic site, but less controlled for a bilingual presentation and risks publishing unrelated planning documents from the existing `docs/` tree.

## Information architecture and visual direction

The project-site base path is `/OpenCam/`. The deployed output provides:

- A root entry page with an accessible manual choice between Traditional Chinese and English. When JavaScript is available, it redirects based on a saved preference, then browser language; the fallback is Traditional Chinese.
- `/zh-TW/` and `/en-US/` homepage routes.
- `/zh-TW/guide/` and `/en-US/guide/` complete guide routes.

Language switching maps homepage to homepage and guide to guide. Each language route is a normal link and remains usable without JavaScript. Internal links and image paths must work beneath `/OpenCam/`, including after a direct reload of any guide page.

The homepage has a compact navigation header, an above-the-fold value proposition emphasizing simple and reliable recording, an official Releases download call-to-action, a real application screenshot, key capabilities, a concise MKV-to-MP4/recovery explanation, platform requirements, a second screenshot showing settings, guide entry points, and an attribution/footer area. Claims and platform restrictions must match the README and guide; specifically, macOS builds are Apple Silicon only until that changes.

Visual treatment takes cues from the app: a light canvas, slate text, green action color, rounded surfaces, and clear status accents. The design is responsive, keyboard-accessible, legible at mobile widths, and respects reduced-motion preferences. No analytics, external font, or stock screenshot is required. The app icon acts as the brand mark and favicon source.

## Screenshot handling

Use the real screenshots in `docs/images/` as source assets. Before publishing, attempt to capture updated screenshots from the current application where local GUI access and permissions permit. If that cannot be done reliably, retain the existing authentic screenshots with a visible “interface preview” caption; do not claim they depict the newest version. The screenshot and caption language follows the selected site language. Screenshot files must include meaningful alternative text and should be optimized only without misleading edits to the application UI.

## Guide content and synchronization

At build time, render the full body of each `docs/USER_GUIDE.*.md` into its matching guide route. Add a website-level title, table of contents, language switch, and navigation around the rendered content. The Markdown files remain authoritative for the actual instructions; avoid a second manually maintained copy in the website directory. Rewriting relative links from the guide must be deterministic: README and opposite-language links resolve to valid website or repository destinations. Headings receive stable anchors and navigation is usable with keyboard and screen readers.

Changes to either canonical guide should trigger the website workflow and update the site automatically on the next `master` deployment.

## Downloads and version information

Primary buttons link to `https://github.com/kaoshou/OpenCam/releases/latest`, not a hard-coded versioned asset. Explain that the release page contains the Windows installer, Windows portable ZIP, and Apple Silicon macOS DMG. The site may display a current-version label only if generated reliably from repository/release metadata; otherwise omit the number to avoid stale marketing text. Source code and issue/report links point to the official repository.

## Deployment and isolation

Add a dedicated GitHub Pages workflow triggered by pushes to `master` that affect website sources or canonical guides, plus manual dispatch. It builds a static output directory, uploads only that output as a Pages artifact, and deploys through the GitHub-supported Pages Actions. Set `pages: write` and `id-token: write` only on the deployment job and use the `github-pages` environment. Restrict deployment to `master`; pull requests can validate the build without publishing. Keep this workflow separate from `build-and-release.yml`, so website edits cannot create a software Release or modify release assets.

Configure the repository's Pages publishing source to GitHub Actions if it is not already enabled. If repository permissions prevent that setting change, stop and report the exact manual GitHub setting needed rather than claiming the site is live. The expected public address is `https://kaoshou.github.io/OpenCam/`; confirm the actual URL from the deployment result.

## Failure behavior and verification

- Build fails on a missing canonical guide, missing screenshot, broken internal link, or malformed generated output rather than silently omitting content.
- Pages deployment failure leaves the previous live site untouched and is reported from the workflow logs.
- Local verification checks both language routes, both full guides, language mapping, direct deep links, screenshots, download links, and mobile layout.
- Accessibility checks cover semantic landmarks, headings, image descriptions, visible focus, keyboard navigation, and reduced motion.
- After deployment, verify the Pages workflow result and request the public homepage and both guide URLs to confirm live content, not just a successful push.

## Scope boundaries

This work does not change recorder behavior, binary packaging, release tags, licensing, or existing user-guide facts. It does not introduce a custom domain, analytics, account system, blog, or payment flow. GitHub Releases remains the source of downloadable binaries.
