// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,rm} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {createHash} from 'node:crypto';
import {verifyPinnedSources} from '../../scripts/assemble-release-sources.mjs';
test('source delivery verifies exact archive bytes and safe names rather than trusting a completed label',async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-release-source-'));t.after(()=>rm(root,{recursive:true,force:true}));
  await mkdir(join(root,'sources'));const bytes=Buffer.from('exact source archive');
  const entries=[{name:'source.archive',sha256:createHash('sha256').update(bytes).digest('hex')}];
  await writeFile(join(root,'sources/source.archive'),bytes);
  await verifyPinnedSources(join(root,'sources'),entries);
  await assert.rejects(verifyPinnedSources(join(root,'sources'),[{...entries[0],name:'../outside'}]),/Unsafe/);
  await writeFile(join(root,'sources/source.archive'),'changed');
  await assert.rejects(verifyPinnedSources(join(root,'sources'),entries),/Changed/);
});
