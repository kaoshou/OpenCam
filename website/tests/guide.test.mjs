// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { renderGuide } from '../src/guide.mjs';
import { buildSite } from '../build.mjs';

const repositoryRoot = resolve(import.meta.dirname, '../..');

test('project documentation reports only current, evidence-backed behavior', async () => {
  const version = (await readFile(join(repositoryRoot, 'VERSION'), 'utf8')).trim();
  const files = [
    'ARCHITECTURE.md',
    'ROADMAP.md',
    'TESTING.md',
    'RELIABILITY.md',
    'ACCEPTANCE_REPORT.md',
    'MANUAL_TEST_CHECKLIST.md',
    'README.md',
    'docs/USER_GUIDE.zh-TW.md',
    'docs/USER_GUIDE.en-US.md',
  ];
  const entries = await Promise.all(files.map(async file => [file, await readFile(join(repositoryRoot, file), 'utf8')]));
  const documents = Object.fromEntries(entries);
  const allDocumentation = entries.map(([, content]) => content).join('\n');

  assert.match(documents['ARCHITECTURE.md'], /UI.*(?:意外|異常).*(?:安全停止|safe stop)/is);
  assert.doesNotMatch(documents['ARCHITECTURE.md'], /Recorder continues recording after the UI disappears|UI 意外消失[^\n]*持續錄製/i);
  assert.match(documents['ARCHITECTURE.md'], /畫面擷取[^\n]*AVFoundation[^\n]*FFmpeg/i);
  assert.match(documents['MANUAL_TEST_CHECKLIST.md'], /實體硬體.*人工驗證|人工驗證.*實體硬體/s);
  assert.match(documents['MANUAL_TEST_CHECKLIST.md'], /OpenCam 版本[\s\S]*Commit[\s\S]*OS[^\n]*Build[\s\S]*PASS[\s\S]*FAIL[\s\S]*BLOCKED[\s\S]*(?:產出物|Artifact)[\s\S]*Log/i);
  assert.match(documents['README.md'], new RegExp(`目前版本為 \\*\\*${version.replaceAll('.', '\\.')}`));
  assert.match(documents['README.md'], new RegExp(`current version is \\*\\*${version.replaceAll('.', '\\.')}`, 'i'));
  assert.match(documents['docs/USER_GUIDE.zh-TW.md'], new RegExp(`本說明適用於 OpenCam v${version.replaceAll('.', '\\.')}`));
  assert.match(documents['docs/USER_GUIDE.en-US.md'], new RegExp(`guide covers OpenCam v${version.replaceAll('.', '\\.')}`, 'i'));
  assert.match(documents['ACCEPTANCE_REPORT.md'], /tests\/native\/OpenCamSystemAudioTests\.sh/);
  assert.match(documents['ACCEPTANCE_REPORT.md'], /scripts\/build-macos-icon\.sh/);
  assert.doesNotMatch(documents['ACCEPTANCE_REPORT.md'], /scripts\/test-macos-(?:native-audio-helper|icon-build)\.sh|--locked-mode/);

  for (const stale of ['45 項全數 PASS', '0 warnings', '0 警告']) {
    assert.ok(!allDocumentation.includes(stale), `stale claim remains: ${stale}`);
  }
});

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
    await buildSite({ repositoryRoot, outputRoot: out });
    for (const [lang, headings] of [['zh-TW', ['錄影模式', '偏好設定', '修復救援']], ['en-US', ['Recording Modes', 'Preferences', 'Crash Recovery']]]) {
      const html = await readFile(join(out, lang, 'guide', 'index.html'), 'utf8');
      for (const heading of headings) assert.ok(html.includes(heading), `${lang}: ${heading}`);
      assert.match(html, /class="guide-toc"/);
      assert.doesNotMatch(html, /href="[^"]*USER_GUIDE\..*\.md"/);
    }
  } finally { await rm(out, { recursive: true, force: true }); }
});
