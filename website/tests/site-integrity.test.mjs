// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { buildSite, validateOutput } from '../build.mjs';

const root = resolve(import.meta.dirname, '../..');

test('all generated routes and assets are internally valid', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-integrity-'));
  try {
    await buildSite({ repositoryRoot: root, outputRoot: out });
    await validateOutput(out);
    const path = join(out, 'en-US', 'guide', 'index.html');
    const html = await readFile(path, 'utf8');
    await writeFile(path, html.replace('/OpenCam/assets/site.css', '/OpenCam/assets/not-found.css'));
    await assert.rejects(validateOutput(out), /Unresolved site asset/);
  } finally { await rm(out, { recursive: true, force: true }); }
});

test('missing screenshot blocks publication', async () => {
  const out = await mkdtemp(join(tmpdir(), 'opencam-missing-'));
  try {
    await assert.rejects(buildSite({ repositoryRoot: out, outputRoot: join(out, 'dist') }), /Missing required site source/);
  } finally { await rm(out, { recursive: true, force: true }); }
});
