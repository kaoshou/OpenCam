// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm, lstat, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
const script=fileURLToPath(new URL('../../scripts/prepare-obs-user-test-package.mjs',import.meta.url));
const revision='a'.repeat(40);
const digest='ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad';
async function fixture(t){
  const root=await mkdtemp(join(tmpdir(),'opencam-obs-user-test-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const stage=join(root,'stage'),publish=join(root,'publish');
  await mkdir(stage);await mkdir(publish);
  await writeFile(join(stage,'OpenCam.ObsCapture.exe'),'abc');
  const manifest={protocol:1,enabled:false,platform:'win-x64',obsVersion:'33.0.0',
    sourceRevision:'72e86655715d9a25d69ce446aa4dd44ba58e10d2',
    capabilities:{backends:['wgc'],audio:['none','system','microphone','both','silence'],encoders:['cpu']},
    files:{'OpenCam.ObsCapture.exe':digest}};
  await writeFile(join(stage,'runtime.json'),JSON.stringify(manifest));
  const run=(flag='--enable-user-test-only',...extra)=>spawnSync(process.execPath,[script,stage,publish,revision,'0.4.1',flag,...extra],{encoding:'utf8'});
  return {stage,publish,manifest,run};
}
test('explicit user-test staging enables only the copied verified runtime and records its source',async t=>{
  const f=await fixture(t),original=await readFile(join(f.stage,'runtime.json'));
  const result=f.run();assert.equal(result.status,0,result.stderr);
  assert.deepEqual(await readFile(join(f.stage,'runtime.json')),original);
  assert.equal(JSON.parse(await readFile(join(f.publish,'obs-runtime/runtime.json'))).enabled,true);
  assert.equal(await readFile(join(f.publish,'obs-runtime/OpenCam.ObsCapture.exe'),'utf8'),'abc');
  const info=JSON.parse(await readFile(join(f.publish,'OBS-TEST-INFO.json')));
  assert.equal(info.testOnly,true);assert.equal(info.sourceRevision,revision);assert.equal(info.version,'0.4.1');
  assert.equal(info.physicalCapture,'REQUIRES MANUAL VALIDATION');
  const second=f.run();assert.notEqual(second.status,0);assert.match(second.stderr,/already exists/);
});
test('native-only packaging rejects an old helper before enabling runtime',async t=>{
  const f=await fixture(t);await writeFile(join(f.publish,'OpenCam.ScreenCapture.exe'),'abc');
  const result=f.run('--enable-user-test-only','--native-only');
  assert.notEqual(result.status,0);assert.match(result.stderr,/Legacy screen capture helper/);
  await assert.rejects(lstat(join(f.publish,'obs-runtime')),{code:'ENOENT'});
});
test('native-only packaging explicitly records no compatibility recording core',async t=>{
  const f=await fixture(t),result=f.run('--enable-user-test-only','--native-only');
  assert.equal(result.status,0,result.stderr);
  const info=JSON.parse(await readFile(join(f.publish,'OBS-TEST-INFO.json')));
  assert.equal(info.nativeOnly,true);assert.equal(info.compatibilityFallback,false);
});
test('macOS native-only staging validates the nested helper and enables only the copy',async t=>{
  const f=await fixture(t);
  await rm(join(f.stage,'OpenCam.ObsCapture.exe'));
  await mkdir(join(f.stage,'Contents/MacOS'),{recursive:true});
  await writeFile(join(f.stage,'Contents/MacOS/OpenCam.ObsCapture'),'abc',{mode:0o755});
  f.manifest.platform='osx-arm64';f.manifest.files={'Contents/MacOS/OpenCam.ObsCapture':digest};
  await writeFile(join(f.stage,'runtime.json'),JSON.stringify(f.manifest));
  const result=f.run('--enable-user-test-only','--native-only');
  assert.equal(result.status,0,result.stderr);
  assert.equal(JSON.parse(await readFile(join(f.publish,'obs-runtime/runtime.json'))).enabled,true);
  assert.equal(JSON.parse(await readFile(join(f.stage,'runtime.json'))).enabled,false);
  assert.equal(await readFile(join(f.publish,'obs-runtime/Contents/MacOS/OpenCam.ObsCapture'),'utf8'),'abc');
});
test('macOS native-only staging rejects an old screen helper',async t=>{
  const f=await fixture(t);await writeFile(join(f.publish,'OpenCam.ScreenCapture'),'abc');
  const result=f.run('--enable-user-test-only','--native-only');
  assert.notEqual(result.status,0);assert.match(result.stderr,/Legacy screen capture helper/);
  await assert.rejects(lstat(join(f.publish,'obs-runtime')),{code:'ENOENT'});
});
test('without explicit user-test authorization nothing is enabled or copied',async t=>{
  const f=await fixture(t),result=f.run('--release');assert.notEqual(result.status,0);
  assert.match(result.stderr,/explicit user-test/);await assert.rejects(lstat(join(f.publish,'obs-runtime')),{code:'ENOENT'});
  assert.equal(JSON.parse(await readFile(join(f.stage,'runtime.json'))).enabled,false);
});
test('tampered, unlisted, traversal and wrong-platform inputs are rejected before copying',async t=>{
  for(const mode of ['tampered','unlisted','traversal','wrong-platform']) await t.test(mode,async t=>{
    const f=await fixture(t);
    if(mode==='tampered')await writeFile(join(f.stage,'OpenCam.ObsCapture.exe'),'changed');
    if(mode==='unlisted')await writeFile(join(f.stage,'ambient.dll'),'abc');
    if(mode==='traversal')f.manifest.files['../escape']=digest;
    if(mode==='wrong-platform')f.manifest.platform='osx-arm64';
    if(mode==='traversal'||mode==='wrong-platform')await writeFile(join(f.stage,'runtime.json'),JSON.stringify(f.manifest));
    const result=f.run();assert.notEqual(result.status,0,result.stdout);
    await assert.rejects(lstat(join(f.publish,'obs-runtime')),{code:'ENOENT'});
  });
});
test('symlink inputs are rejected instead of copying files outside the verified runtime', {skip:process.platform==='win32'},async t=>{
  const f=await fixture(t);await writeFile(join(f.publish,'outside'),'abc');
  await rm(join(f.stage,'OpenCam.ObsCapture.exe'));await symlink(join(f.publish,'outside'),join(f.stage,'OpenCam.ObsCapture.exe'));
  const result=f.run();assert.notEqual(result.status,0);await assert.rejects(lstat(join(f.publish,'obs-runtime')),{code:'ENOENT'});
});
