// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile,readdir,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {assembleReleaseDelivery} from '../../scripts/assemble-release-delivery.mjs';

const digest=bytes=>createHash('sha256').update(bytes).digest('hex');
async function fixture(t){
  const root=await mkdtemp(join(tmpdir(),'opencam-delivery-'));t.after(()=>rm(root,{recursive:true,force:true}));
  for(const dir of ['sources','linux','windows/installer/Output','mac'])await mkdir(join(root,dir),{recursive:true});
  const version=(await readFile(new URL('../../VERSION',import.meta.url),'utf8')).trim();
  const policy=JSON.parse(await readFile(new URL(`../../docs/releases/v${version}-source-coverage.json`,import.meta.url),'utf8'));
  const scope=['obs-windows','obs-macos','ffmpeg-tools-windows','ffmpeg-tools-macos'];
  const archives=[];
  for(const name of ['OBS-SOURCES.tar.gz','Windows-Media-SOURCES.tar.gz','macOS-Media-SOURCES.tar.gz']){
    const bytes=Buffer.from('source '+name);await writeFile(join(root,'sources',name),bytes);
    archives.push({name,sha256:digest(bytes),covers:scope});
  }
  await writeFile(join(root,'sources/SOURCE-COVERAGE.json'),JSON.stringify({version,status:'reviewed',reviewedBy:policy.reviewedBy,archives}));
  await writeFile(join(root,'sources/OBS-SOURCES.json'),'original provenance bytes');
  await writeFile(join(root,'sources/SOURCE-INDEX.txt'),'original source index');
  const sums=[];
  for(const name of [`OpenCam_v${version}_Linux_x64_Beta.deb`,`OpenCam_v${version}_Linux_x64_Beta.tar.gz`,`OpenCam_v${version}_Linux_Beta_CorrespondingSource.tar.gz`]){
    const bytes=Buffer.from(name);await writeFile(join(root,'linux',name),bytes);sums.push(`${digest(bytes)}  ./${name}`);
  }
  await writeFile(join(root,`linux/OpenCam_v${version}_Linux_Beta_SHA256SUMS.txt`),sums.join('\n')+'\n');
  for(const path of [`windows/installer/Output/OpenCam_v${version}_Windows_x64_Setup.exe`,`windows/OpenCam_v${version}_Windows_x64_Portable.zip`,`mac/OpenCam_v${version}_macOS_arm64.dmg`])await writeFile(join(root,path),path);
  // Extra inputs must never leak through a directory-glob upload.
  await writeFile(join(root,'linux/debug.log'),'not for release');
  await writeFile(join(root,'sources/unreviewed.json'),'not for release');
  return {version,sourceDirectory:join(root,'sources'),linuxDirectory:join(root,'linux'),windowsDirectory:join(root,'windows'),macDirectory:join(root,'mac'),destination:join(root,'delivery')};
}

test('delivery publishes only five binaries, one complete source bundle and one verified checksum list',async t=>{
  const f=await fixture(t);await assembleReleaseDelivery(f);
  const names=(await readdir(f.destination)).sort();
  const v=f.version;
  assert.deepEqual(names,[`OpenCam_v${v}_CorrespondingSource.tar.gz`,`OpenCam_v${v}_Linux_x64_Beta.deb`,`OpenCam_v${v}_Linux_x64_Beta.tar.gz`,`OpenCam_v${v}_SHA256SUMS.txt`,`OpenCam_v${v}_Windows_x64_Portable.zip`,`OpenCam_v${v}_Windows_x64_Setup.exe`,`OpenCam_v${v}_macOS_arm64.dmg`].sort());
  const sums=await readFile(join(f.destination,`OpenCam_v${v}_SHA256SUMS.txt`),'utf8');
  for(const name of names.filter(n=>!n.endsWith('_SHA256SUMS.txt')))assert.ok(sums.includes(`${digest(await readFile(join(f.destination,name)))}  ${name}\n`));
  const archive=join(f.destination,`OpenCam_v${v}_CorrespondingSource.tar.gz`);
  for(const name of ['OBS-SOURCES.tar.gz','Windows-Media-SOURCES.tar.gz','macOS-Media-SOURCES.tar.gz','SOURCE-COVERAGE.json','OBS-SOURCES.json','SOURCE-INDEX.txt']){
    const bytes=execFileSync('tar',['-xOzf',archive,name]);
    assert.deepEqual(bytes,await readFile(join(f.sourceDirectory,name)));
  }
  const linux=`OpenCam_v${v}_Linux_Beta_CorrespondingSource.tar.gz`;
  assert.deepEqual(execFileSync('tar',['-xOzf',archive,linux]),await readFile(join(f.linuxDirectory,linux)));
});

test('tampered or missing source delivery blocks creation of a publishable directory',async t=>{
  const f=await fixture(t);await writeFile(join(f.sourceDirectory,'OBS-SOURCES.tar.gz'),'tampered');
  await assert.rejects(assembleReleaseDelivery(f),/Changed source archive/);
  await assert.rejects(readdir(f.destination),{code:'ENOENT'});
});

test('Linux source corruption is not hidden by successful Windows source validation',async t=>{
  const f=await fixture(t);await writeFile(join(f.linuxDirectory,`OpenCam_v${f.version}_Linux_Beta_CorrespondingSource.tar.gz`),'tampered');
  await assert.rejects(assembleReleaseDelivery(f),/Linux.*checksum/i);
  await assert.rejects(readdir(f.destination),{code:'ENOENT'});
});

test('incorrect version and existing output are refused rather than overwritten',async t=>{
  const f=await fixture(t);
  await assert.rejects(assembleReleaseDelivery({...f,version:'9.9.9'}),/version/i);
  await mkdir(f.destination);await writeFile(join(f.destination,'keep'),'user bytes');
  await assert.rejects(assembleReleaseDelivery(f),/exist/i);
  assert.equal(await readFile(join(f.destination,'keep'),'utf8'),'user bytes');
});
