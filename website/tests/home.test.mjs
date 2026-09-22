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
      assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam\/releases\/latest/);
      assert.match(html, new RegExp(`preview_main_${suffix}\\.png`));
      assert.match(html, new RegExp(`preview_settings_${suffix}\\.png`));
      assert.match(html, /<img[^>]+alt="[^"]+"/);
      assert.match(html, new RegExp(`/OpenCam/${lang}/guide/`));
      assert.match(html, /Apple Silicon/);
      assert.doesNotMatch(html, /v0\.1\.5|fonts\.googleapis|google-analytics|gtag\(/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});
