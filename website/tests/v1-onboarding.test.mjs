// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { downloadVersion, renderHome } from '../src/content.mjs';

test('v1 homepage introduces OBS and both familiar workflows without obsolete capture advice', () => {
  for (const lang of ['zh-TW', 'en-US']) {
    const html = renderHome(lang);
    for (const word of [`v${downloadVersion}`, 'OBS', 'oCam', 'EverCam']) assert.ok(html.includes(word), `${lang}: ${word}`);
    assert.match(html, /id="quick-start"/);
    assert.doesNotMatch(html, /高效能擷取|Try high-performance capture/);
    for (const style of ['WarmIvory', 'TealSilver', 'RoundShort', 'HighContrast']) assert.ok(html.includes(`pointer-${style}.png`));
  }
});

test('README and both manuals explain v1 workflows and native recording boundaries', async () => {
  for (const name of ['README.md', 'docs/USER_GUIDE.zh-TW.md', 'docs/USER_GUIDE.en-US.md']) {
    const text = await readFile(new URL(`../../${name}`, import.meta.url), 'utf8');
    for (const word of ['oCam', 'EverCam', 'OBS', 'FFmpeg', '1.0.0']) assert.ok(text.includes(word), `${name}: ${word}`);
    assert.doesNotMatch(text, /v0\.4\./);
  }
});
