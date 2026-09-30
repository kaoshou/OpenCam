// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawnSync } from 'node:child_process';

const script = resolve(import.meta.dirname, '../../scripts/release-artifact-names.mjs');

test('packaging emits versioned platform/architecture names for GitHub Actions', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-artifact-names-'));
  try {
    const versionFile = join(directory, 'VERSION');
    for (const [version, expected] of [
      ['0.2.4\n', [
        'OPENCAM_VERSION=0.2.4',
        'WINDOWS_SETUP=OpenCam_v0.2.4_Windows_x64_Setup.exe',
        'WINDOWS_PORTABLE=OpenCam_v0.2.4_Windows_x64_Portable.zip',
        'MACOS_DMG=OpenCam_v0.2.4_macOS_arm64.dmg',
      ]],
      ['1.12.30\n', [
        'OPENCAM_VERSION=1.12.30',
        'WINDOWS_SETUP=OpenCam_v1.12.30_Windows_x64_Setup.exe',
        'WINDOWS_PORTABLE=OpenCam_v1.12.30_Windows_x64_Portable.zip',
        'MACOS_DMG=OpenCam_v1.12.30_macOS_arm64.dmg',
      ]],
    ]) {
      await writeFile(versionFile, version);
      const result = spawnSync(process.execPath, [script, versionFile], { encoding: 'utf8' });
      assert.equal(result.status, 0, result.stderr);
      assert.equal(result.stdout, expected.join('\n') + '\n');
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('invalid versions cannot publish malformed filenames or inject Actions variables', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-artifact-invalid-'));
  try {
    const versionFile = join(directory, 'VERSION');
    for (const invalid of ['0.2.4', 'v0.2.4\n', '../0.2.4\n', '0.2.4\nOTHER=value\n']) {
      await writeFile(versionFile, invalid);
      const result = spawnSync(process.execPath, [script, versionFile], { encoding: 'utf8' });
      assert.notEqual(result.status, 0);
      assert.equal(result.stdout, '');
      assert.match(result.stderr, /VERSION/);
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
