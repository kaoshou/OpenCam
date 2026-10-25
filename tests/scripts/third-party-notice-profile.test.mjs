// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp,mkdir,writeFile,readFile,rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
const script=fileURLToPath(new URL('../../scripts/assemble-third-party-notices.mjs',import.meta.url));

async function fixture(t){
  const root=await mkdtemp(join(tmpdir(),'opencam-profile-notices-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const publish=join(root,'publish'),ffmpeg=join(root,'ffmpeg'),packages=join(root,'packages');
  const runtime=join(packages,'microsoft.netcore.app.runtime.win-x64','8.0.31');
  await mkdir(publish);await mkdir(ffmpeg);await mkdir(runtime,{recursive:true});
  for(const name of ['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT'])await writeFile(join(runtime,name),'Fixture runtime notice\n');
  for(const name of ['ffmpeg-license.txt','ffmpeg-version.txt','ffprobe-license.txt','ffprobe-version.txt'])
    await writeFile(join(ffmpeg,name),'Fixture FFmpeg notice\n');
  const assets={targets:{'net8.0/win-x64':{}},packageFolders:{[packages+'/']:{}},
    project:{frameworks:{'net8.0':{downloadDependencies:[{name:'Microsoft.NETCore.App.Runtime.win-x64',version:'[8.0.31, 8.0.31]'}]}}}};
  const path=join(root,'native-only.assets.json');
  const run=async()=>{
    await writeFile(path,JSON.stringify(assets));
    return spawnSync(process.execPath,[script,publish,'win-x64',ffmpeg,path],{encoding:'utf8'});
  };
  return {publish,assets,run};
}
test('notice CLI uses the explicit native-only publish assets, not the normal build assets',async t=>{
  const f=await fixture(t),result=await f.run();
  assert.equal(result.status,0,result.stderr);
  const manifest=JSON.parse(await readFile(join(f.publish,'third-party/manifest.json')));
  assert.equal(manifest.rid,'win-x64');assert.equal(manifest.runtime,'microsoft.netcore.app.runtime.win-x64/8.0.31');
});
test('profile assets still reject wrong RID and unreviewed runtime packages',async t=>{
  for(const mode of ['wrong-rid','unreviewed'])await t.test(mode,async t=>{
    const f=await fixture(t);
    if(mode==='wrong-rid')f.assets.targets={net8_0:{}};
    else f.assets.targets['net8.0/win-x64']['Unreviewed.Package/1.0.0']={type:'package',runtime:{'unreviewed.dll':{}}};
    const result=await f.run();assert.notEqual(result.status,0);
    assert.match(result.stderr,mode==='wrong-rid'?/Restore\/publish assets/:/Unreviewed third-party dependency/);
  });
});
