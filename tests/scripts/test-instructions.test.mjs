// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {renderTestInstructions} from '../../scripts/render-test-instructions.mjs';

test('test package instructions render the actual version instead of an obsolete title',async()=>{
  const template=await readFile(new URL('../../docs/testing/windows-obs-test.zh-TW.txt',import.meta.url),'utf8');
  for(const v of ['1.3.0','2.0.1']){
    const text=renderTestInstructions(template,v);
    assert.ok(text.startsWith(`OpenCam ${v} — Windows x64`));
    assert.ok(text.includes('OpenCam.exe'));
    assert.ok(!text.includes('{{VERSION}}'));
  }
  assert.throws(()=>renderTestInstructions(template,'1.3.0\nOTHER=value'),/version/i);
  assert.throws(()=>renderTestInstructions('OpenCam 1.2.0','1.3.0'),/placeholder/i);
});
