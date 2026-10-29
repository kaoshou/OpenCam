// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test'; import assert from 'node:assert/strict';
import { classifyElfDependencies } from '../../scripts/linux/elf-policy.mjs';
test('only the known optional CoreCLR LTTng provider may miss its tracing ABI', () => {
  const absent='liblttng-ust.so.0 => not found\nlibc.so.6 => /lib/libc.so.6';
  assert.equal(classifyElfDependencies('libcoreclrtraceptprovider.so',absent),'OPTIONAL_LTTNG_UNAVAILABLE');
  assert.throws(()=>classifyElfDependencies('OpenCam',absent),/required/);
  assert.throws(()=>classifyElfDependencies('libcoreclrtraceptprovider.so',absent+'\nlibc.so.6 => not found'),/required/);
  assert.throws(()=>classifyElfDependencies('obs-runtime/lib/libobs.so','libavformat.so.62 => not found'),/required/);
  assert.equal(classifyElfDependencies('OpenCam','libc.so.6 => /lib/libc.so.6'),'PASS');
});
