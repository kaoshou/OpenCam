// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, symlink, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { assembleLinuxApp } from '../../scripts/linux/assemble-app.mjs';
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'linux-app-assembly-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const dir of ['publish', 'native/bin', 'native/lib', 'native/share', 'media/bin', 'media/lib']) await mkdir(join(root, dir), { recursive: true });
  for (const path of ['publish/OpenCam', 'native/bin/OpenCam.ObsCapture', 'native/lib/libobs.so', 'media/bin/ffmpeg', 'media/bin/ffprobe', 'media/lib/libavformat.so']) await writeFile(join(root, path), path);
  return { publishRoot: join(root, 'publish'), nativeRoot: join(root, 'native'), mediaRoot: join(root, 'media'),
    outputRoot: join(root, 'package'), revision: '1234567890123456789012345678901234567890', version: '1.2.0' };
}
test('assembly inventories every native runtime byte and never invents source revision', async t => {
  const f = await fixture(t); await assembleLinuxApp(f);
  const m = JSON.parse(await readFile(join(f.outputRoot, 'obs-runtime/runtime.json'), 'utf8'));
  assert.equal(m.platform, 'linux-x64'); assert.deepEqual(m.capabilities.backends, ['x11']);
  assert.equal(m.files['bin/OpenCam.ObsCapture'], createHash('sha256').update('native/bin/OpenCam.ObsCapture').digest('hex'));
  assert.equal(JSON.parse(await readFile(join(f.outputRoot, 'LINUX-TEST-INFO.json'))).commit, f.revision);
  await assert.rejects(() => assembleLinuxApp(f), /exist/i);
});
test('missing helper rejects packaging before creating the destination', async t => {
  const f = await fixture(t); await rm(join(f.nativeRoot, 'bin/OpenCam.ObsCapture'));
  await assert.rejects(() => assembleLinuxApp(f), /helper/i);
});
test('native symlink escaping the audited prefix is rejected', async t => {
  const f = await fixture(t); await symlink('/etc/passwd', join(f.nativeRoot, 'lib/escape.so'));
  await assert.rejects(() => assembleLinuxApp(f), /escape|outside/i);
});
