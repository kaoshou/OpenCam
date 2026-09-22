// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { buildSite } from '../build.mjs';

test('localized homepages include genuine previews, guide, and current download', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-home-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    for (const [lang, suffix] of [['zh-TW', 'zhtw'], ['en-US', 'enus']]) {
      const html = await readFile(join(out, lang, 'index.html'), 'utf8');
      assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam\/releases\/tag\/v0\.2\.0/);
      assert.match(html, new RegExp(`preview_main_${suffix}\\.png`));
      assert.match(html, new RegExp(`preview_settings_${suffix}\\.png`));
      assert.match(html, /<img[^>]+alt="[^"]+"/);
      assert.match(html, new RegExp(`/OpenCam/${lang}/guide/`));
      assert.match(html, /Apple Silicon/);
      assert.match(html, /AGPL-3\.0-or-later/);
      assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam\/blob\/master\/LICENSE/);
      assert.match(html, /0\.2\.0/);
      assert.doesNotMatch(html, /download still points to v0\.1\.5|下載連結仍指向 v0\.1\.5/);
      assert.doesNotMatch(html, /fonts\.googleapis|google-analytics|gtag\(/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
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

test('Chinese hero keeps each slogan phrase together', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-slogan-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    const html = await readFile(join(out, 'zh-TW', 'index.html'), 'utf8');
    assert.match(html, /<h1><span class="hero-line">把重要的畫面，<\/span><em class="hero-line">好好錄下來。<\/em><\/h1>/);
  } finally { await rm(out, { recursive: true, force: true }); }
});
