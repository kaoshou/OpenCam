// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, readFile, mkdir, copyFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { buildSite } from '../build.mjs';
import { mainScreenshotVersion, productVersion, downloadVersion } from '../src/content.mjs';

const escapedProductVersion = productVersion.replaceAll('.', '\\.');
const escapedScreenshotVersion = mainScreenshotVersion.replaceAll('.', '\\.');

test('localized homepages include genuine previews, guide, and current download', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-home-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    for (const [lang, suffix] of [['zh-TW', 'zhtw'], ['en-US', 'enus']]) {
      const html = await readFile(join(out, lang, 'index.html'), 'utf8');
      assert.match(html, /href="https:\/\/github\.com\/kaoshou\/OpenCam\/releases"/);
      assert.match(html, new RegExp(`preview_main_${suffix}\\.png`));
      assert.match(html, new RegExp(`preview_settings_${suffix}\\.png`));
      assert.match(html, new RegExp(`preview_editor_${suffix}\\.png`));
      assert.match(html, new RegExp(`preview_subtitles_${suffix}\\.png`));
      assert.match(html, /逐字稿搜尋|Transcript search/);
      assert.match(html, /格式4|format4/);
      assert.match(html, /id="editing"/);
      assert.match(html, /<img[^>]+alt="[^"]+"/);
      assert.match(html, new RegExp(`/OpenCam/${lang}/guide/`));
      assert.match(html, /Apple Silicon/);
      assert.match(html, /AGPL-3\.0-or-later/);
      assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam\/blob\/master\/LICENSE/);
      assert.match(html, new RegExp(`v${escapedProductVersion}`));
      assert.match(html, /目前版本|Current version/);
      assert.match(html, /網頁播放器輸出|Web player export/);
      assert.match(html, /GIF/);
      assert.doesNotMatch(html, /實機驗收|視覺驗收|來源核對仍待完成|publication does not establish complete acceptance|acceptance.*pending/i);
      assert.doesNotMatch(html, new RegExp(`Download v${escapedProductVersion}|下載 v${escapedProductVersion}`));
      assert.doesNotMatch(html, /download still points to v0\.1\.5|下載連結仍指向 v0\.1\.5/);
      assert.doesNotMatch(html, /fonts\.googleapis|google-analytics|gtag\(/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});

test('hero offers three localized published attachment downloads and supporting links', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-downloads-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    for (const [lang, labels] of [
      ['zh-TW', ['下載 Windows 免安裝版', '下載 Windows 安裝版', '下載 macOS 版']],
      ['en-US', ['Download Windows portable', 'Download Windows installer', 'Download macOS']],
    ]) {
      const html = await readFile(join(out, lang, 'index.html'), 'utf8');
      const actions = html.match(/<div class="hero-actions">([\s\S]*?)<\/div>/)?.[1];
      assert(actions, 'Download actions must be present');
      const links = [...actions.matchAll(/<a\b[^>]*href="([^"]+)"[^>]*>([\s\S]*?)<\/a>/g)];
      assert.equal(links.length, 3);
      assert.deepEqual(links.map(m => m[1]), [
        `https://github.com/kaoshou/OpenCam/releases/download/v${downloadVersion}/OpenCam_v${downloadVersion}_Windows_x64_Portable.zip`,
        `https://github.com/kaoshou/OpenCam/releases/download/v${downloadVersion}/OpenCam_v${downloadVersion}_Windows_x64_Setup.exe`,
        `https://github.com/kaoshou/OpenCam/releases/download/v${downloadVersion}/OpenCam_v${downloadVersion}_macOS_arm64.dmg`,
      ]);
      assert.deepEqual(links.map(m => m[2]), labels);
      const notes = html.match(/<div class="download-notes">([\s\S]*?)<\/div>/)?.[1];
      assert(notes, 'Architecture and supporting links must stay below downloads');
      assert.match(notes, /x64/);assert.match(notes, /Apple Silicon/);
      assert.match(notes, /href="https:\/\/github\.com\/kaoshou\/OpenCam\/releases"/);
      assert.match(notes, new RegExp(`href="/OpenCam/${lang}/guide/"`));
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});

test('unpublished development version does not move downloads or the public version label', async () => {
  const root = await mkdtemp(join(tmpdir(), 'opencam-future-version-'));
  try {
    await mkdir(join(root, 'website/src'), { recursive: true });
    await mkdir(join(root, 'docs/images'), { recursive: true });
    for (const name of ['content.mjs', 'onboarding.mjs', 'layout.mjs', 'paths.mjs']) {
      await copyFile(new URL('../src/' + name, import.meta.url), join(root, 'website/src', name));
    }
    await copyFile(new URL('../downloads.json', import.meta.url), join(root, 'website/downloads.json'));
    await writeFile(join(root, 'VERSION'), '9.9.9\n');
    await writeFile(join(root, 'docs/images/metadata.json'), '{"version":"0.3.5"}');
    const future = await import(pathToFileURL(join(root, 'website/src/content.mjs')).href);
    assert.equal(future.productVersion, '9.9.9');
    const html = future.renderHome('en-US');
    assert.ok(html.includes(`Current version v${downloadVersion}`));
    assert.ok(html.includes(`releases/download/v${downloadVersion}/OpenCam_v${downloadVersion}_Windows_x64_Portable.zip`));
    assert.doesNotMatch(html, /releases\/download\/v9\.9\.9/);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('each homepage offers full-size current macOS screenshots', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-preview-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    for (const [lang, suffix] of [['zh-TW', 'zhtw'], ['en-US', 'enus']]) {
      const html = await readFile(join(out, lang, 'index.html'), 'utf8');
      for (const image of [`preview_main_${suffix}.png`, `preview_settings_${suffix}.png`]) {
        assert.match(html, new RegExp(`<a[^>]+href="/OpenCam/assets/${image}"[^>]*><img[^>]+src="/OpenCam/assets/${image}"`));
      }
      assert.match(html, /macOS/);
      assert.doesNotMatch(html, /Windows interface preview|Windows 介面預覽|may differ slightly|可能與最新版本略有差異/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});

test('localized main screenshots describe the current capture and pointer settings', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-recording-preview-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    const expectations = [
      ['zh-TW', '主畫面', '教鞭'],
      ['en-US', 'recording screen', 'teaching pointer']
    ];
    for (const [lang, stateText, waveformText] of expectations) {
      const html = await readFile(join(out, lang, 'index.html'), 'utf8');
      assert.match(html, new RegExp(`alt="[^"]*OpenCam ${escapedScreenshotVersion}[^"]*${stateText}[^"]*${waveformText}[^"]*"`));
      assert.match(html, /<img[^>]+preview_main_(?:zhtw|enus)\.png[^>]+width="1440" height="1000"/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});

test('Chinese hero keeps each slogan phrase together', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-slogan-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    const html = await readFile(join(out, 'zh-TW', 'index.html'), 'utf8');
    assert.match(html, /<h1><span class="hero-line">把重要的畫面，<\/span><em class="hero-line">好好錄下來。<\/em><\/h1>/);
  } finally { await rm(out, { recursive: true, force: true }); }
});
