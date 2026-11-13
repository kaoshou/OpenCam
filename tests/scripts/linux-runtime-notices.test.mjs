// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile,rm,readdir} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {copyLinuxRuntimeNotices} from '../../scripts/linux/copy-runtime-notices.mjs';

async function fixture(t,version='10.0.15',target='net10.0/linux-x64') {
  const root=await mkdtemp(join(tmpdir(),'opencam-runtime-notices-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const cache=join(root,'packages'),destination=join(root,'notices'),assetsPath=join(root,'assets.json');
  await mkdir(destination);
  for(const v of ['8.0.31','10.0.15']) {
    const directory=join(cache,'microsoft.netcore.app.runtime.linux-x64',v);
    await mkdir(directory,{recursive:true});
    for(const name of ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT']) await writeFile(join(directory,name),`${v} ${name}`);
  }
  await writeFile(assetsPath,JSON.stringify({targets:{[target]:{}},packageFolders:{[cache]:{}},
    project:{frameworks:{'net10.0':{downloadDependencies:[{name:'Microsoft.NETCore.App.Runtime.linux-x64',version:`[${version}, ${version}]`}]}}}}));
  return {root,cache,destination,assetsPath};
}

test('Linux notices use the actual pinned runtime despite an older cache entry',async t=>{
  const f=await fixture(t);
  await copyLinuxRuntimeNotices(f);
  assert.equal(await readFile(join(f.destination,'DOTNET-LICENSE.txt'),'utf8'),'10.0.15 LICENSE.TXT');
  assert.equal(await readFile(join(f.destination,'DOTNET-THIRD-PARTY-NOTICES.txt'),'utf8'),'10.0.15 THIRD-PARTY-NOTICES.TXT');
});

for(const [label,version,target] of [
  ['old runtime','8.0.31','net10.0/linux-x64'],
  ['unpinned patch','10.0.10','net10.0/linux-x64'],
  ['wrong RID','10.0.15','net10.0/win-x64'],
  ['old framework','10.0.15','net8.0/linux-x64'],
]) test(`Linux notice collection rejects ${label} without writing notices`,async t=>{
  const f=await fixture(t,version,target);
  await assert.rejects(()=>copyLinuxRuntimeNotices(f),/runtime|assets/i);
  assert.deepEqual(await readdir(f.destination),[]);
});

test('Linux notices cannot fall back to old files when the pinned package is incomplete',async t=>{
  const f=await fixture(t);
  await rm(join(f.cache,'microsoft.netcore.app.runtime.linux-x64/10.0.15/THIRD-PARTY-NOTICES.TXT'));
  await assert.rejects(()=>copyLinuxRuntimeNotices(f),/runtime notices/i);
  assert.deepEqual(await readdir(f.destination),[]);
});
