// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
const script=new URL('../../scripts/linux/check-installed-elf.sh',import.meta.url).pathname;
const inspect=(path,status,input)=>spawnSync('/bin/bash',[script,path,String(status)],{input,encoding:'utf8'});
test('installed dependency inspection must not accept an ldd error without missing-library text',()=>{
  const result=inspect('OpenCam',1,'ldd: failed to inspect executable');
  assert.equal(result.status,1); assert.match(result.stderr,/inspection failed/i);
});
test('only the exact optional CoreCLR tracing ABI may be unavailable',()=>{
  assert.equal(inspect('libcoreclrtraceptprovider.so',0,'liblttng-ust.so.0 => not found\n').status,0);
  assert.equal(inspect('OpenCam',0,'liblttng-ust.so.0 => not found\n').status,1);
  assert.equal(inspect('libcoreclrtraceptprovider.so',0,'liblttng-ust.so.0 => not found\nlibother.so => not found\n').status,1);
  assert.equal(inspect('OpenCam',0,'libc.so.6 => /lib/libc.so.6\n').status,0);
});
