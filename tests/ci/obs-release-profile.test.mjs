// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const read = path => readFile(new URL(`../../${path}`, import.meta.url),'utf8');
test('official release reuses validated OBS packaging instead of legacy capture', async()=>{
  const release=await read('.github/workflows/build-and-release.yml');
  assert.match(release,/uses: \.\/.github\/workflows\/verify-obs-core.yml/);
  assert.match(release,/release_package: true/);
  assert.doesNotMatch(release,/scripts\\build-windows-capture|scripts\/build-windows-capture/);
  const core=await read('.github/workflows/verify-obs-core.yml');
  assert.match(core,/workflow_call:/);
  assert.match(core,/--enable-release --native-only/);
  assert.match(core,/--native-only-release/);
  assert.match(core,/WINDOWS_PORTABLE/);assert.match(core,/MACOS_DMG/);
  assert.match(core,/ScreenRecorder.NativeOnlyProbe/);
});
