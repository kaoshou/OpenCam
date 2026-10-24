// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, lstat } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { assembleObsRuntime } from '../../scripts/assemble-obs-runtime.mjs';

test('stages the actual pinned official Mac runtime and loads the relocated helper', {
  skip: process.platform !== 'darwin' || !process.env.OPENCAM_OBS_SOURCE_RUNTIME || !process.env.OPENCAM_OBS_HELPER,
}, async t => {
  const directory = process.env.OPENCAM_OBS_STAGE_PATH ?? join(await mkdtemp(join(tmpdir(), 'opencam-obs-stage-')), 'runtime');
  const helperBefore = createHash('sha256').update(await readFile(process.env.OPENCAM_OBS_HELPER)).digest('hex');
  const manifest = await assembleObsRuntime(directory, {
    platform: 'osx-arm64', source: process.env.OPENCAM_OBS_SOURCE_RUNTIME, helper: process.env.OPENCAM_OBS_HELPER,
    licenses: new URL('../../third-party/obs-runtime', import.meta.url).pathname,
  });
  assert.equal(manifest.enabled, false); assert.ok(Object.keys(manifest.files).length > 100);
  let count = 0;
  async function walk(path, prefix = '') {
    for (const name of await readdir(path)) {
      const s = await lstat(join(path, name)); const file = prefix + name; assert.equal(s.isSymbolicLink(), false);
      if (s.isDirectory()) await walk(join(path, name), file + '/');
      else if (file !== 'runtime.json') {
        count++; assert.equal(manifest.files[file], createHash('sha256').update(await readFile(join(path, name))).digest('hex'));
      }
    }
  }
  await walk(directory); assert.equal(count, Object.keys(manifest.files).length);
  assert.ok(!Object.keys(manifest.files).some(p => /obs-browser|Chromium|QtCore|obs-websocket|rtmp-services|frontend-api/.test(p)));
  const result = spawnSync(join(directory, 'Contents/MacOS/OpenCam.ObsCapture'), ['--runtime', directory], {input:'{"protocol":999}\n', timeout:10000});
  assert.equal(result.status, 2, result.stderr?.toString()); assert.equal(result.stdout.length, 0);
  assert.match(result.stderr.toString(), /OCOBS .*"event":"error"/);
  assert.ok(!result.stderr.toString().includes('Library not loaded'));
  assert.equal(createHash('sha256').update(await readFile(process.env.OPENCAM_OBS_HELPER)).digest('hex'), helperBefore);
  t.diagnostic(`Disabled staged runtime: ${directory}; ${count} validated SHA-256 files. Staged artifacts retained for further native/managed acceptance.`);
});
