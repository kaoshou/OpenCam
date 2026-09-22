import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { routeFor, assetPath } from '../src/paths.mjs';
import { renderEntry, renderLayout } from '../src/layout.mjs';
import { buildSite } from '../build.mjs';

const root = resolve(import.meta.dirname, '../..');

test('stable project-site routes and safe assets', () => {
  assert.equal(routeFor('zh-TW', 'home'), '/OpenCam/zh-TW/');
  assert.equal(routeFor('en-US', 'guide'), '/OpenCam/en-US/guide/');
  assert.equal(assetPath('site.css'), '/OpenCam/assets/site.css');
  assert.throws(() => routeFor('fr', 'home'));
  assert.throws(() => assetPath('../secret'));
});

test('language entry and deep-link layout work without JavaScript', () => {
  assert.match(renderEntry(), /href="\/OpenCam\/zh-TW\/"/);
  assert.match(renderEntry(), /href="\/OpenCam\/en-US\/"/);
  assert.match(renderLayout({ language: 'en-US', title: 'Guide', description: 'Guide', page: 'guide', body: '' }), /href="\/OpenCam\/assets\/site\.css"/);
});

test('build emits four deep links and fails for missing source assets', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-site-'));
  try {
    await buildSite({ repositoryRoot: root, outputRoot: out });
    for (const page of ['index.html', 'zh-TW/index.html', 'en-US/index.html', 'zh-TW/guide/index.html', 'en-US/guide/index.html']) {
      assert.match(await readFile(join(out, page), 'utf8'), /OpenCam/);
    }
    await assert.rejects(buildSite({ repositoryRoot: join(out, 'missing'), outputRoot: join(out, 'fail') }), /Missing required site source/);
  } finally {
    await rm(out, { recursive: true, force: true });
  }
});
