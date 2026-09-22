// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { renderGuide } from '../src/guide.mjs';
import { buildSite } from '../build.mjs';

test('canonical links and duplicate headings resolve safely', () => {
  const markdown = '## Recording\n## Recording\n[README](../README.md) [License](../LICENSE) [中文](USER_GUIDE.zh-TW.md) [External](https://example.com)\n\n```html\n<script>bad</script>\n```';
  const { html, toc } = renderGuide(markdown, 'en-US');
  assert.match(html, /id="recording"/);
  assert.match(html, /id="recording-2"/);
  assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam#readme/);
  assert.match(html, /https:\/\/github\.com\/kaoshou\/OpenCam\/blob\/master\/LICENSE/);
  assert.match(html, /href="\/OpenCam\/zh-TW\/guide\/"/);
  assert.match(html, /href="https:\/\/example\.com"/);
  assert.match(html, /&lt;script&gt;bad&lt;\/script&gt;/);
  assert.equal(toc.length, 2);
  assert.throws(() => renderGuide('[bad](missing.md)', 'en-US'), /Unresolved guide link/);
});

test('full guides retain modes, settings, locations, and recovery', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-guides-'));
  try {
    await buildSite({ repositoryRoot: resolve(import.meta.dirname, '../..'), outputRoot: out });
    for (const [lang, headings] of [['zh-TW', ['錄影模式', '偏好設定', '修復救援']], ['en-US', ['Recording Modes', 'Preferences', 'Crash Recovery']]]) {
      const html = await readFile(join(out, lang, 'guide', 'index.html'), 'utf8');
      for (const heading of headings) assert.ok(html.includes(heading), `${lang}: ${heading}`);
      assert.match(html, /class="guide-toc"/);
      assert.doesNotMatch(html, /href="[^"]*USER_GUIDE\..*\.md"/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});
