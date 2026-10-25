// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile,rm} from 'node:fs/promises';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {createHash} from 'node:crypto';
const hash=b=>createHash('sha256').update(b).digest('hex');
function pe(dependencies=[]){
  const b=Buffer.alloc(2048);b.write('MZ');b.writeUInt32LE(128,60);b.writeUInt32LE(0x4550,128);
  b.writeUInt16LE(0x8664,132);b.writeUInt16LE(1,134);b.writeUInt16LE(240,148);b.writeUInt16LE(0x20b,152);
  b.writeUInt32LE(1024,400);b.writeUInt32LE(0x1000,404);b.writeUInt32LE(1024,408);b.writeUInt32LE(512,412);
  if(dependencies.length)b.writeUInt32LE(0x1000,272);
  let offset=256;dependencies.forEach((name,n)=>{b.writeUInt32LE(0x1000+offset,512+n*20+12);b.write(name,512+offset);offset+=name.length+1;});return b;
}
async function fixture(t){
  const root=await mkdtemp(join(tmpdir(),'opencam-crt-'));t.after(()=>rm(root,{recursive:true,force:true}));
  const runtime=join(root,'runtime'),source=join(root,'source'),metadata=join(root,'metadata');
  for(const path of [runtime,source,metadata,join(runtime,'licenses')])await mkdir(path);
  const helper=pe(['vcruntime140_1.dll']),inventory=Buffer.from('{"components":[]}');
  await writeFile(join(runtime,'OpenCam.ObsCapture.exe'),helper);await writeFile(join(runtime,'licenses/inventory.json'),inventory);
  await writeFile(join(runtime,'runtime.json'),JSON.stringify({platform:'win-x64',enabled:false,files:{'OpenCam.ObsCapture.exe':hash(helper),'licenses/inventory.json':hash(inventory)}}));
  const files=[];
  for(const name of ['vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll']){
    const bytes=pe(name==='vcruntime140_1.dll'?['vcruntime140.dll']:['ucrtbase.dll']);await writeFile(join(source,name),bytes);
    files.push({name,sha256:hash(bytes),signatureStatus:'Valid',signerSubject:'CN=Microsoft Corporation, O=Microsoft Corporation, C=US'});
  }
  const license=Buffer.from('license fixture');await writeFile(join(metadata,'MSVC-2022-LICENSE.docx'),license);
  const evidence={files,licenseSha256:hash(license),licenseUrl:'https://visualstudio.microsoft.com/',toolset:'fixture'};
  await writeFile(join(metadata,'provenance.json'),JSON.stringify(evidence));
  return{runtime,source,metadata,evidence};
}
test('app-local CRT is placed beside helper and all bytes are inventoried',async t=>{
  const f=await fixture(t),{stageWindowsCrt}=await import('../../scripts/stage-windows-crt.mjs');
  await stageWindowsCrt(f.runtime,f.source,f.metadata);
  const m=JSON.parse(await readFile(join(f.runtime,'runtime.json')));
  assert.equal(m.enabled,false);assert.equal(m.appLocalCrt.length,3);
  for(const name of m.appLocalCrt)assert.equal(m.files[name],hash(await readFile(join(f.runtime,name))));
  assert.ok(m.files['licenses/MSVC-2022-LICENSE.docx']);
  await assert.rejects(stageWindowsCrt(f.runtime,f.source,f.metadata),/already exists/);
});
test('missing, changed or unauthenticated CRT cannot use system DLLs as a substitute',async t=>{
  for(const mutation of ['missing','changed','unsigned']){
    const f=await fixture(t),{stageWindowsCrt}=await import('../../scripts/stage-windows-crt.mjs');
    if(mutation==='missing'){f.evidence.files.pop();await writeFile(join(f.metadata,'provenance.json'),JSON.stringify(f.evidence));}
    if(mutation==='changed')await writeFile(join(f.source,'vcruntime140.dll'),'changed');
    if(mutation==='unsigned'){f.evidence.files[0].signatureStatus='NotSigned';await writeFile(join(f.metadata,'provenance.json'),JSON.stringify(f.evidence));}
    await assert.rejects(stageWindowsCrt(f.runtime,f.source,f.metadata),/Missing|Changed|Unauthenticated/);
    assert.equal(JSON.parse(await readFile(join(f.runtime,'runtime.json'))).appLocalCrt,undefined);
  }
});
