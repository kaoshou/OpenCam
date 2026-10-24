// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, readFile, lstat, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { execFileSync } from 'node:child_process';
const moduleUrl = new URL('../../scripts/prepare-obs-validation.mjs', import.meta.url);
async function temp(t) {const p=await mkdtemp(join(tmpdir(),'opencam-obs-acquisition-'));t.after(()=>rm(p,{recursive:true,force:true}));return p;}
test('validates archive bytes against the pinned digest before using an existing download', async t => {
  const { verifyArchive } = await import(moduleUrl); const root=await temp(t), path=join(root,'archive.zip');
  await writeFile(path,'abc');
  const artifact={sha256:'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad',size:3};
  await verifyArchive(path,artifact); await writeFile(path,'abd'); await assert.rejects(verifyArchive(path,artifact),/checksum/);
});
test('rejects a changed GitHub release digest or URL instead of trusting latest release metadata', async () => {
  const { verifyReleaseAsset, dependencyLock } = await import(moduleUrl); const artifact=dependencyLock.windowsRuntime;
  const asset={id:670572636,name:'OBS-Studio-33.0.0-Windows-x64.zip',size:223737114,digest:'sha256:3369a3ecd67b5669a8a08d77357f87cc786d1fba42fbbd1054d841a69f6271a7',browser_download_url:'https://github.com/obsproject/obs-studio/releases/download/33.0.0/OBS-Studio-33.0.0-Windows-x64.zip'};
  const release={tag_name:'33.0.0',draft:false,prerelease:false,assets:[asset]};
  assert.equal(verifyReleaseAsset(release,artifact).id,670572636);
  assert.throws(()=>verifyReleaseAsset({...release,assets:[{...asset,digest:'sha256:'+'0'.repeat(64)}]},artifact),/pinned/);
  assert.throws(()=>verifyReleaseAsset({...release,assets:[{...asset,browser_download_url:'https://example.com/untrusted.zip'}]},artifact),/pinned/);
  assert.throws(()=>verifyReleaseAsset({...release,tag_name:'34.0.0'},artifact),/release/);
});
test('refuses a dirty or wrong source checkout', async t => {
  const { verifyPinnedSource }=await import(moduleUrl);const root=await temp(t);
  const git=(...args)=>execFileSync('git',['-C',root,...args],{encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim();
  git('init');await writeFile(join(root,'source.h'),'original');git('add','source.h');
  git('-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','-m','fixture');const revision=git('rev-parse','HEAD');
  await verifyPinnedSource(root,revision);await assert.rejects(verifyPinnedSource(root,'0'.repeat(40)),/revision/);
  await writeFile(join(root,'source.h'),'modified');await assert.rejects(verifyPinnedSource(root,revision),/modified/);
});
test('does not overwrite an existing acquisition directory', async t => {
  const { prepareObsValidation }=await import(moduleUrl);const root=await temp(t);await writeFile(join(root,'sentinel'),'keep');
  await assert.rejects(prepareObsValidation(root),/already exists/);assert.equal(await readFile(join(root,'sentinel'),'utf8'),'keep');
});
test('rejects unpinned notice sources before creating an inventory directory', async t => {
  const { assembleWindowsObsNotices }=await import(moduleUrl);const root=await temp(t), output=join(root,'notices');
  await assert.rejects(assembleWindowsObsNotices(output,{obsSource:root,ffmpegSource:root,obsDepsSource:root,simdeSource:root}),/revision/);
  await assert.rejects(lstat(output),{code:'ENOENT'});
});
test('does not install generated FFmpeg headers into an unverified source tree', async t => {
  const { prepareFFmpegPublicHeaders }=await import(moduleUrl);const root=await temp(t);
  await assert.rejects(prepareFFmpegPublicHeaders(root),/revision/);await assert.rejects(lstat(join(root,'libavutil/avconfig.h')),{code:'ENOENT'});
});
test('Windows verification refuses missing acquisition evidence without creating output', async t => {
  try{execFileSync('pwsh',['-NoProfile','-Command','exit 0'],{stdio:'ignore'});}catch{t.skip('PowerShell unavailable');return;}
  const root=await temp(t), output=join(root,'result');let failure;
  const { fileURLToPath }=await import('node:url');
  try{execFileSync('pwsh',['-NoProfile','-File',fileURLToPath(new URL('../../scripts/verify-obs-core-windows.ps1',import.meta.url)),
    '-InputDirectory',root,'-OutputDirectory',output,'-FFmpegDirectory',root],{encoding:'utf8',stdio:'pipe'});}catch(e){failure=e;}
  assert.ok(failure);assert.match(failure.stderr,/Pinned acquisition report is missing/);await assert.rejects(lstat(output),{code:'ENOENT'});
});
test('installs only the known generated x64 public FFmpeg header after checking source provenance', {
  skip:!process.env.OPENCAM_OBS_FFMPEG_SOURCE,
}, async t => {
  const { prepareFFmpegPublicHeaders }=await import(moduleUrl);const root=await temp(t), source=join(root,'ffmpeg');
  execFileSync('git',['clone','--shared','--no-hardlinks',process.env.OPENCAM_OBS_FFMPEG_SOURCE,source],{stdio:'pipe'});
  await prepareFFmpegPublicHeaders(source);
  const header=await readFile(join(source,'libavutil/avconfig.h'),'utf8');assert.match(header,/#define AV_HAVE_BIGENDIAN 0/);assert.match(header,/#define AV_HAVE_FAST_UNALIGNED 1/);
  await writeFile(join(source,'libavutil/avconfig.h'),'unexpected header');
  await assert.rejects(prepareFFmpegPublicHeaders(source),/generated header/);
  assert.equal(await readFile(join(source,'libavutil/avconfig.h'),'utf8'),'unexpected header');
});
