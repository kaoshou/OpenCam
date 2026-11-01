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
  assert.match(release,/assemble-release-sources\.mjs/);
  assert.match(release,/check-release-source-provision\.mjs/);
  assert.match(core,/macOS-Media-SOURCES\.tar\.gz/);
});

test('portable verification builds and exercises the Linux spawn shim before running media tests', async()=>{
  const release=await read('.github/workflows/build-and-release.yml');
  const portable=release.split('  verify-portable:')[1].split('  verify-windows:')[0];
  const build=portable.indexOf('dotnet build ScreenRecorder.sln');
  const smoke=portable.indexOf('tests/native/linux-spawn-tests.c');
  const library=portable.indexOf('-o "$test_output/libOpenCamLinuxSpawn.so"');
  const child=portable.indexOf('cp "$test_output/libOpenCamLinuxSpawn.so" "$test_output/project-recording-probe/"');
  const tests=portable.indexOf('dotnet test ScreenRecorder.sln');
  assert.ok(build>=0 && smoke>build && library>smoke && child>library && tests>child);
  assert.match(portable,/dotnet test ScreenRecorder\.sln[^\n]*--no-build/);
  assert.match(portable,/"\$RUNNER_TEMP\/opencam-linux-spawn-tests"\n/);
});
