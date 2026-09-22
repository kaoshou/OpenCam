import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = new URL('../../', import.meta.url);
const icon = (path) => readFile(new URL(path, repo));

test('shared app icon is sharp and consistent across macOS, website, and Windows', async () => {
  const png = await icon('src/ScreenRecorder.UI/Assets/app_icon.png');
  assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
  assert.equal(png.readUInt32BE(16), 1024);
  assert.equal(png.readUInt32BE(20), 1024);
  assert.equal(png[25], 6, 'PNG must include an alpha channel for rounded corners');

  const uiIco = await icon('src/ScreenRecorder.UI/Assets/app_icon.ico');
  const recorderIco = await icon('src/ScreenRecorder.Recorder/app_icon.ico');
  assert.deepEqual(recorderIco, uiIco);
  assert.equal(uiIco.readUInt16LE(0), 0);
  assert.equal(uiIco.readUInt16LE(2), 1);
  const count = uiIco.readUInt16LE(4);
  const sizes = Array.from({ length: count }, (_, index) => uiIco[6 + 16 * index] || 256);
  assert.deepEqual(sizes.sort((a, b) => a - b), [16, 32, 48, 64, 128, 256]);
});

test('macOS icon builder packages the high-resolution PNG as ICNS', {
  skip: process.platform !== 'darwin',
}, async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-icon-test-'));
  try {
    const output = join(directory, 'OpenCam.icns');
    const script = fileURLToPath(new URL('../../scripts/build-macos-icon.sh', import.meta.url));
    const source = fileURLToPath(new URL('../../src/ScreenRecorder.UI/Assets/app_icon.png', import.meta.url));
    const result = spawnSync('bash', [script, source, output], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    assert.equal((await readFile(output)).subarray(0, 4).toString('ascii'), 'icns');
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
