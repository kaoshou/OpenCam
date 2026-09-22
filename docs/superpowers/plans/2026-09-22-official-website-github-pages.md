# OpenCam Official Website Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and publish a bilingual official OpenCam website with genuine application screenshots, complete guides, and current GitHub Release download links.

**Architecture:** A small Node build in `website/` converts the repository's two canonical Markdown guides into static HTML and renders two localized homepages. A separate GitHub Pages workflow publishes only the generated output; the existing desktop release workflow remains unchanged.

**Tech Stack:** Node.js 22, `markdown-it` 15.0.2, built-in `node:test`, semantic HTML, CSS, minimal browser JavaScript, GitHub Pages Actions.

**Spec:** `docs/superpowers/specs/2026-09-22-official-website-github-pages-design.md`

## Global Constraints

- The site lives under the project-site base path `/OpenCam/`; all links and assets must work on direct loads beneath that prefix.
- Routes are `/zh-TW/`, `/en-US/`, `/zh-TW/guide/`, and `/en-US/guide/`, plus a root language-choice entry page.
- `docs/USER_GUIDE.zh-TW.md` and `docs/USER_GUIDE.en-US.md` remain the sole sources for the full guide body.
- The site uses the four genuine screenshots in `docs/images/` and the real app icon in `src/ScreenRecorder.UI/Assets/app_icon.png`; existing screenshots are Windows previews and may show an older version.
- Download buttons point to `https://github.com/kaoshou/OpenCam/releases/latest`; do not embed a stale version number or duplicate release binaries.
- No analytics, external fonts, custom domain, blog, account system, or changes to recorder code or binary release packaging.
- Publishing is restricted to `master` and uses a dedicated workflow with `pages: write`, `id-token: write`, and the `github-pages` environment.

## Review Focus

1. A direct load of `/OpenCam/en-US/guide/` must retain correct CSS, icon, and navigation paths instead of assuming the site is hosted at `/` — pinned by Task 1 and Task 4 route tests.
2. A user without JavaScript must still choose a language and move between corresponding pages — pinned by Task 1 and Task 2 output tests.
3. Canonical guide links such as `../README.md` and `USER_GUIDE.en-US.md` must not become broken Pages URLs — pinned by Task 3 link-rewrite tests.
4. Missing screenshots or guide files must fail the build rather than silently produce an incomplete public site — pinned by Task 1 and Task 3 negative tests.
5. A homepage-only change or guide-only change must deploy the site without triggering a software Release — pinned by Task 5 workflow validation and final GitHub Actions inspection.

---

## File structure and interfaces

- `website/package.json` and `website/package-lock.json`: pinned build dependency and `build`/`test` scripts.
- `website/src/paths.mjs`: exports `BASE_PATH`, `routeFor(language, page)`, and `assetPath(name)`; other modules use these for every internal URL.
- `website/src/content.mjs`: localized homepage copy and screenshot metadata; keeps translated copy adjacent and comparable.
- `website/src/layout.mjs`: exports `renderLayout({ language, title, description, page, body })` and `renderEntry()`; shared landmarks, navigation, metadata, and footer.
- `website/src/guide.mjs`: exports `renderGuide(markdown, language)`; converts canonical Markdown, heading IDs, table of contents, and links.
- `website/build.mjs`: reads sources, assembles routes, copies approved assets, validates output, and writes `website/dist/`.
- `website/src/site.css`: responsive site presentation, visible focus, and reduced-motion rules.
- `website/src/site.js`: optional root-language preference and persistent language choice; no content depends on it.
- `website/tests/*.test.mjs`: Node tests for routes, output, Markdown links, assets, and failure cases.
- `.github/workflows/deploy-pages.yml`: isolated build/deploy workflow.
- `README.md`: visible official-site link after the site URL has been verified.

### Task 1: Static build foundation and stable routes

**Files:**
- Create: `website/package.json`, `website/package-lock.json`, `website/src/paths.mjs`, `website/src/layout.mjs`, `website/build.mjs`
- Create: `website/tests/routes.test.mjs`
- Modify: `.gitignore` to exclude `website/node_modules/` and `website/dist/`

**Interfaces:**
- Consumes: repository root and site base path `/OpenCam/`.
- Produces: `BASE_PATH`, `routeFor(language, page)`, `assetPath(name)`, `renderLayout(...)`, `renderEntry()`, and a build CLI that writes `website/dist`.

- [ ] **Step 1: Write failing route/output tests.** Use `node:test` to assert the four exact routes, a root entry page containing normal links to both languages, a guide deep link whose CSS path begins `/OpenCam/`, and a build error when a required asset is missing. The key assertions are:

  ```js
  assert.equal(routeFor('zh-TW', 'home'), '/OpenCam/zh-TW/');
  assert.equal(routeFor('en-US', 'guide'), '/OpenCam/en-US/guide/');
  assert.match(renderEntry(), /href="\/OpenCam\/zh-TW\/"/);
  assert.match(renderEntry(), /href="\/OpenCam\/en-US\/"/);
  assert.match(renderLayout({ language: 'en-US', title: 'Guide', description: 'Guide', page: 'guide', body: '' }), /href="\/OpenCam\/assets\/site\.css"/);
  ```

- [ ] **Step 2: Confirm red.** Run `node --test website/tests/routes.test.mjs`; expect module-not-found or assertion failures because the route/build modules do not exist yet.
- [ ] **Step 3: Implement the minimal route and layout functions.** `routeFor` accepts only `zh-TW`/`en-US` and `home`/`guide`, throwing on any other value. `assetPath(name)` prepends `/OpenCam/assets/` after rejecting `..` and absolute paths. `renderEntry()` contains both static links and a small script that chooses the saved language or `navigator.language`, defaulting to `zh-TW`.

  ```js
  export const BASE_PATH = '/OpenCam/';
  export function routeFor(language, page) {
    if (!['zh-TW', 'en-US'].includes(language) || !['home', 'guide'].includes(page)) {
      throw new TypeError('Unsupported site route');
    }
    return `${BASE_PATH}${language}/${page === 'guide' ? 'guide/' : ''}`;
  }
  export function assetPath(name) {
    if (!name || name.startsWith('/') || name.includes('..') || name.includes('\\')) {
      throw new TypeError('Unsafe asset name');
    }
    return `${BASE_PATH}assets/${name}`;
  }
  ```
- [ ] **Step 4: Add build scaffolding.** Set `package.json` scripts to `node build.mjs` and `node --test tests/*.test.mjs`; pin `markdown-it` to `15.0.2` and generate/commit the lockfile with `npm install --package-lock-only`. Make `build.mjs` export `buildSite({ repositoryRoot, outputRoot })` and only run automatically when invoked as its CLI. Create each route's `index.html` and `assets/`, and fail with a named missing-file error.

  ```json
  {
    "private": true,
    "type": "module",
    "scripts": {
      "build": "node build.mjs",
      "test": "node --test tests/*.test.mjs"
    },
    "dependencies": { "markdown-it": "15.0.2" },
    "engines": { "node": ">=22 <23" }
  }
  ```

  `buildSite` writes `index.html`, `zh-TW/index.html`, `en-US/index.html`, both `guide/index.html` files, `assets/site.css`, `assets/site.js`, the icon, and four screenshots. Only the final `if (process.argv[1] === fileURLToPath(import.meta.url))` block invokes it as a CLI.
- [ ] **Step 5: Confirm green.** Run `npm ci --prefix website`, `npm test --prefix website`, and `npm run build --prefix website`; inspect `website/dist/index.html` and the four route files.
- [ ] **Step 6: Commit.** Stage only the foundation, tests, lockfile, and `.gitignore`; commit `feat(web): add static site build foundation`.

### Task 2: Bilingual homepage and real screenshot presentation

**Files:**
- Create: `website/src/content.mjs`, `website/src/site.css`, `website/src/site.js`
- Modify: `website/src/layout.mjs`, `website/build.mjs`
- Create: `website/tests/home.test.mjs`

**Interfaces:**
- Consumes: `routeFor`, `assetPath`, `renderLayout`, and the four screenshots plus icon.
- Produces: both localized homepage HTML documents and copied assets under `website/dist/assets/`.

- [ ] **Step 1: Write failing homepage tests.** For both languages, assert a prominent GitHub Releases latest link, the matching main and settings screenshot filenames, nonempty descriptive `alt` attributes, Apple Silicon-only macOS wording, and a guide link to the same-language guide route. Assert the output contains no `v0.1.5` hard-code and does not load external fonts or trackers.
- [ ] **Step 2: Confirm red.** Run `npm test --prefix website`; expect the homepage content assertions to fail.
- [ ] **Step 3: Implement localized content.** Add `content['zh-TW']` and `content['en-US']` values for hero, navigation, six concise feature cards, MKV → MP4/recovery sequence, Windows and macOS requirements, screenshot captions, download CTA, and footer. Keep factual wording consistent with `README.md` and the guides. Use `assetPath('preview_main_zhtw.png')`/`assetPath('preview_settings_zhtw.png')` and their English equivalents.

  ```js
  export const content = {
    'zh-TW': {
      heroTitle: '簡單錄影，安心保存。',
      heroText: '選好螢幕或範圍，一鍵開始。OpenCam 先安全寫入 MKV，完成後封裝成 MP4。',
      downloadLabel: '下載 OpenCam',
      guideLabel: '閱讀使用說明',
      featuresTitle: '好上手，也為重要錄影做好準備',
      features: [
        ['指定螢幕與自訂區域', '錄製整個指定顯示器，或用透明選取框精準指定範圍。'],
        ['系統聲音與麥克風', '依需求分別開關系統聲音與麥克風。'],
        ['游標效果', '原始游標、黃色光暈、點擊漣漪或隱藏游標。'],
        ['暫停後調整', '暫停時可調整音訊開關與游標樣式。'],
        ['自動選擇編碼器', '優先使用可用的硬體編碼，必要時回退 CPU。'],
        ['修復救援', '意外中斷時，嘗試救回已寫入的 MKV 分段。']
      ],
      reliabilityTitle: '先保留錄影，再交付 MP4',
      reliabilityText: '錄影期間建立 MKV 工作檔；正常停止後驗證並無損封裝為 MP4。',
      windowsRequirement: 'Windows 10／11，x64',
      previewCaption: 'Windows 介面預覽',
      macRequirement: 'macOS 13 以上，Apple Silicon（arm64）',
      mainScreenshot: 'preview_main_zhtw.png',
      settingsScreenshot: 'preview_settings_zhtw.png'
    },
    'en-US': {
      heroTitle: 'Record simply. Keep it safely.',
      heroText: 'Pick a screen or region and start. OpenCam records to MKV first, then packages the result as MP4.',
      downloadLabel: 'Download OpenCam',
      guideLabel: 'Read the user guide',
      featuresTitle: 'Easy to use, ready for important recordings',
      features: [
        ['Monitor and region capture', 'Record a selected display or define an exact area with a transparent frame.'],
        ['System audio and microphone', 'Switch computer audio and microphone recording independently.'],
        ['Cursor effects', 'Use the native pointer, yellow halo, click ripple, or hidden cursor.'],
        ['Adjust while paused', 'Change audio switches and cursor style during a pause.'],
        ['Automatic encoder choice', 'Prefer available hardware encoding and fall back to CPU when needed.'],
        ['Crash Recovery', 'After interruption, attempt to salvage MKV segments already written.']
      ],
      reliabilityTitle: 'Preserve the recording first. Deliver MP4 afterward.',
      reliabilityText: 'OpenCam writes MKV working files, then validates and remuxes them to MP4 after a normal stop.',
      windowsRequirement: 'Windows 10/11, x64',
      previewCaption: 'Windows interface preview',
      macRequirement: 'macOS 13 or later, Apple Silicon (arm64)',
      mainScreenshot: 'preview_main_enus.png',
      settingsScreenshot: 'preview_settings_enus.png'
    }
  };
  ```
- [ ] **Step 4: Implement homepage layout and approved asset copying.** The build copies exactly four PNG previews and the app icon. Screenshot captions say “Windows interface preview” in each language unless genuinely current screenshots have been captured. Render a responsive hero, screenshot gallery, features, reliability explanation, platform cards, guide CTA, and footer. Render all navigation as ordinary links; JavaScript only stores language preference and improves the root landing behavior.
- [ ] **Step 5: Confirm green and visually inspect.** Run `npm test --prefix website` and `npm run build --prefix website`; serve `website/dist` under `/OpenCam/` using a local static server and inspect desktop and narrow/mobile widths. Check keyboard tab order and visible focus.
- [ ] **Step 6: Commit.** Commit `feat(web): add bilingual OpenCam homepage`.

### Task 3: Render canonical guides as complete website pages

**Files:**
- Create: `website/src/guide.mjs`, `website/tests/guide.test.mjs`
- Modify: `website/build.mjs`, `website/src/layout.mjs`, `website/src/site.css`

**Interfaces:**
- Consumes: the two `docs/USER_GUIDE.*.md` files, `renderLayout`, `routeFor`, and `markdown-it`.
- Produces: `renderGuide(markdown, language)` returning `{ html, toc }` and two complete guide routes.

- [ ] **Step 1: Write failing guide tests.** A short fixture with a heading, code fence, `../README.md`, opposite-language guide link, and an external link must produce an anchorable heading, escaped code, a same-site opposite-language guide URL, a repository README URL, and an unchanged external URL. Test duplicate headings get distinct IDs and missing guide paths fail the build. In the full build, assert headings for recording modes, settings, file locations, and recovery exist in both rendered guides.
- [ ] **Step 2: Confirm red.** Run `npm test --prefix website`; expect guide-specific failures.
- [ ] **Step 3: Implement Markdown rendering.** Initialize `markdown-it` with `{ html: false, linkify: true, typographer: false }`. Use a heading renderer that assigns deterministic IDs, adding `-2`, `-3` for repeats, and collects levels 2–3 into the table of contents. Rewrite only known canonical relative links: `../README.md` to `https://github.com/kaoshou/OpenCam#readme`, and `USER_GUIDE.<other-language>.md` to the matching Pages guide route; reject any other unresolved local Markdown link instead of generating a dead URL. Escape heading text and attributes.

  ```js
  import MarkdownIt from 'markdown-it';
  import { routeFor } from './paths.mjs';

  const md = new MarkdownIt({ html: false, linkify: true, typographer: false });
  export function rewriteGuideHref(href, language) {
    if (href === '../README.md') return 'https://github.com/kaoshou/OpenCam#readme';
    if (href === 'USER_GUIDE.zh-TW.md') return routeFor('zh-TW', 'guide');
    if (href === 'USER_GUIDE.en-US.md') return routeFor('en-US', 'guide');
    if (/^(https?:|#)/.test(href)) return href;
    throw new Error(`Unresolved guide link in ${language}: ${href}`);
  }
  ```

  Parse to Markdown-it tokens, rewrite each `link_open` token's `href`, and assign each `heading_open` token an ID from a slug based on its following inline token; use a collision counter for repeated headings before calling `md.renderer.render(tokens, md.options, {})`.
- [ ] **Step 4: Add guide layout.** Render the full Markdown body inside an `<article>` with a skip link and labeled table of contents. Avoid a second hand-maintained guide copy. Keep the language switch on the guide page pointed at the other guide, not its homepage.
- [ ] **Step 5: Confirm green.** Run `npm test --prefix website` and `npm run build --prefix website`; verify both generated HTML files contain the full guide, no `USER_GUIDE.*.md` links, and no unresolved local links.
- [ ] **Step 6: Commit.** Commit `feat(web): publish bilingual user guides`.

### Task 4: Accessibility, screenshots, and site-integrity acceptance

**Files:**
- Modify: `website/src/site.css`, `website/src/layout.mjs`, `website/build.mjs`
- Modify/Create: `website/tests/site-integrity.test.mjs`
- Optionally update: `docs/images/preview_main_*.png`, `docs/images/preview_settings_*.png` only with authentic newly captured app screenshots

**Interfaces:**
- Consumes: all generated routes and copied assets from Tasks 1–3.
- Produces: `validateOutput(outputRoot)` in `website/build.mjs` and a verified `website/dist` suitable for Pages upload.

- [ ] **Step 1: Write failing integrity tests.** Import a new `validateOutput(outputRoot)` function that must parse or scan generated HTML to assert every local `href`/`src` resolves under `/OpenCam/`, every `<img>` has nonempty `alt`, every page has a single `<main>`, guide pages have a table-of-contents nav, and screenshot files exist and are nonzero. Add a negative test where one screenshot is removed from a temporary fixture source and `buildSite` rejects it.
- [ ] **Step 2: Confirm red.** Run `npm test --prefix website`; expect at least one integrity assertion to fail before improvements.
- [ ] **Step 3: Implement `validateOutput` and make CSS and markup accessible.** Resolve each local URL by removing the `/OpenCam/` prefix and mapping it to `outputRoot`; reject paths outside the output tree. Add `:focus-visible` styling, a skip-to-content link, reasonable responsive breakpoints for 320 px and 768 px widths, and `@media (prefers-reduced-motion: reduce)` disabling nonessential animation. Ensure screenshot captions explicitly say preview if old versions remain visible.

  ```css
  .skip-link { position: absolute; transform: translateY(-150%); }
  .skip-link:focus { transform: translateY(0); }
  :focus-visible { outline: 3px solid #059669; outline-offset: 3px; }
  img { max-width: 100%; height: auto; }
  @media (max-width: 48rem) { .feature-grid { grid-template-columns: 1fr; } }
  @media (prefers-reduced-motion: reduce) {
    *, *::before, *::after { scroll-behavior: auto !important; transition-duration: 0.01ms !important; }
  }
  ```
- [ ] **Step 4: Attempt current-version screenshot capture.** If the Mac can launch and capture the current OpenCam UI without exposing personal files or screen content, recapture both language variants and replace the four source screenshots. Otherwise retain the existing genuine screenshots and their preview captions. Do not edit version text into an image.
- [ ] **Step 5: Confirm green and inspect in browser.** Run `npm test --prefix website`, `npm run build --prefix website`, `git diff --check`, and inspect homepage and guide in both languages at desktop/mobile sizes with keyboard navigation. If using old screenshots, verify no text calls them current-version screenshots.
- [ ] **Step 6: Commit.** Commit `test(web): verify responsive and accessible Pages output`, including screenshot changes only when captured and reviewed.

### Task 5: Dedicated GitHub Pages workflow and repository entry point

**Files:**
- Create: `.github/workflows/deploy-pages.yml`
- Modify: `README.md`
- Create: `website/tests/workflow.test.mjs`

**Interfaces:**
- Consumes: `npm ci --prefix website`, `npm test --prefix website`, `npm run build --prefix website`, and `website/dist`.
- Produces: a Pages artifact and deployment on `master`, without invoking `.github/workflows/build-and-release.yml` directly.

- [ ] **Step 1: Write failing workflow checks.** Assert the workflow has a `master` push trigger for `website/**`, both canonical guide files, `docs/images/**`, and the workflow itself; manual dispatch; `npm ci`, test, and build commands; upload path `website/dist`; `github-pages` environment; and deploy-job-only `pages: write`/`id-token: write`. Assert it contains no `gh release` or tag trigger.
- [ ] **Step 2: Confirm red.** Run `npm test --prefix website`; expect missing-workflow assertions to fail.
- [ ] **Step 3: Add the workflow.** Use `actions/checkout@v4`, `actions/setup-node@v7` with Node 22, `actions/configure-pages@v5`, `actions/upload-pages-artifact@v4`, and `actions/deploy-pages@v4`. Split `build` and `deploy` jobs with `needs: build`. Include pull-request build validation, but guard deployment with `github.ref == 'refs/heads/master'`.

  ```yaml
  name: Deploy OpenCam Website
  on:
    push:
      branches: [master]
      paths:
        - 'website/**'
        - 'docs/USER_GUIDE.*.md'
        - 'docs/images/**'
        - 'src/ScreenRecorder.UI/Assets/app_icon.png'
        - '.github/workflows/deploy-pages.yml'
    pull_request:
      paths: ['website/**', 'docs/USER_GUIDE.*.md', 'docs/images/**', 'src/ScreenRecorder.UI/Assets/app_icon.png']
    workflow_dispatch:
  jobs:
    build:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4
        - uses: actions/setup-node@v7
          with:
            node-version: '22'
            cache: npm
            cache-dependency-path: website/package-lock.json
        - run: npm ci --prefix website
        - run: npm test --prefix website
        - run: npm run build --prefix website
        - uses: actions/configure-pages@v5
          if: github.ref == 'refs/heads/master'
        - uses: actions/upload-pages-artifact@v4
          if: github.ref == 'refs/heads/master'
          with:
            path: website/dist
    deploy:
      if: github.ref == 'refs/heads/master'
      needs: build
      runs-on: ubuntu-latest
      permissions:
        contents: read
        pages: write
        id-token: write
      environment:
        name: github-pages
        url: ${{ steps.deployment.outputs.page_url }}
      steps:
        - uses: actions/deploy-pages@v4
          id: deployment
  ```
- [ ] **Step 4: Add README navigation.** Link both README language sections to `https://kaoshou.github.io/OpenCam/`, describing it as the official website. Keep existing GitHub guide links intact so documentation remains readable before Pages activates.
- [ ] **Step 5: Confirm green.** Run `npm test --prefix website`, `npm run build --prefix website`, `git diff --check`, and `git diff -- .github/workflows/build-and-release.yml` (expected empty). Validate workflow syntax with `ruby -e 'require "yaml"; YAML.load_file(ARGV.fetch(0))' .github/workflows/deploy-pages.yml`; the live Action run in Task 6 validates GitHub-specific semantics.
- [ ] **Step 6: Commit.** Commit `ci: deploy official website with GitHub Pages`.

### Task 6: Push, configure Pages, and verify the live site

**Files:**
- No source changes expected; only repository Pages settings may change.

**Interfaces:**
- Consumes: a green local site build and `master` commits from Tasks 1–5.
- Produces: a reachable public Pages URL with both localized homepages and guides.

- [ ] **Step 1: Run final local verification.** Run `npm ci --prefix website`, `npm test --prefix website`, `npm run build --prefix website`, `git diff --check`, and `git status --short --branch`. Review `git log origin/master..HEAD` so the pushed commit range is understood.
- [ ] **Step 2: Configure Pages.** Read `gh api repos/kaoshou/OpenCam/pages`. If no site exists, create/configure it for workflow publishing using the documented Pages API or the repository Settings UI, checking the exact current API contract before mutation. If access is denied, report the `Settings → Pages → Build and deployment → Source: GitHub Actions` action needed and stop short of calling the site live.
- [ ] **Step 3: Push the approved commits.** Push `master` without creating a version tag. Confirm no repository changes outside the planned website/docs paths are included.
- [ ] **Step 4: Watch the Pages run.** Use `gh run list` and `gh run watch` for `.github/workflows/deploy-pages.yml`. A successful `build-and-release.yml` run alone is not evidence of Pages publication.
- [ ] **Step 5: Read back the deployment.** Confirm the Pages URL returned by GitHub and fetch the root, both localized homepages, both guide routes, stylesheet, and screenshots. Assert HTTP 200 and recognizable expected text. If propagation is delayed, retry for a bounded period; report an unverified state rather than claiming completion.
- [ ] **Step 6: Report outcome.** Provide the verified live website link, both guide links, Pages Action link, and any screenshot caveat. State explicitly that no desktop application Release was created.
