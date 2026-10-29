// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { renderHome } from '../src/content.mjs';
import { renderGuide } from '../src/guide.mjs';

test('current home pages explain video import and source balance without promising universal fast export', () => {
  for (const [language, words] of [
    ['zh-TW', ['加入影片', '音量滑桿', '混音', '僅處理音訊']],
    ['en-US', ['Add Video', 'volume sliders', 'mixed', 'audio-only']]
  ]) {
    const html = renderHome(language);
    for (const word of words) assert.ok(html.includes(word), `${language}: ${word}`);
    assert.match(html, /preview_audio_(?:zhtw|enus)\.png/);
    assert.doesNotMatch(html, /NEW \/ 1\.2\.0[^<]*<\/p><h2>(?:錄影時，直接|Mark up)/);
  }
});

test('audio guide screenshots resolve with their real proportions', () => {
  const { html } = renderGuide('![Audio](images/preview_audio_enus.png)', 'en-US');
  assert.match(html, /width="740" height="610"/);
});

test('release notes contain application changes, not documentation chores', async () => {
  const notes = await readFile(new URL('../../docs/releases/v1.2.0.md', import.meta.url), 'utf8');
  assert.match(notes, /加入影片/);
  assert.match(notes, /音量/);
  assert.doesNotMatch(notes, /更新網站、文件與測試|##.*(?:網站|文件|測試)/);
});
