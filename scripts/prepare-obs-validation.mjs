// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, writeFile, mkdir, readdir, lstat, copyFile } from 'node:fs/promises';
import { createReadStream, createWriteStream } from 'node:fs';
import { createHash } from 'node:crypto';
import { pipeline } from 'node:stream/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const run = promisify(execFile);
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const dependencyLock = JSON.parse(await readFile(new URL('./obs-dependencies.lock.json', import.meta.url), 'utf8'));
async function exists(path) {try {await lstat(path);return true;}catch(e){if(e.code==='ENOENT')return false;throw e;}}

export async function verifyArchive(path, artifact) {
  const digest=createHash('sha256');let size=0;
  for await(const bytes of createReadStream(path)){digest.update(bytes);size+=bytes.length;}
  if(digest.digest('hex')!==artifact.sha256 || size!==artifact.size)throw new Error('Pinned OBS archive checksum/size mismatch');
}
export function verifyReleaseAsset(release, artifact=dependencyLock.windowsRuntime) {
  if(release.tag_name!==dependencyLock.obsVersion || release.draft || release.prerelease)throw new Error('Expected pinned stable OBS release');
  const assets=release.assets?.filter(a=>a.name===artifact.name)??[];
  if(assets.length!==1 || assets[0].id!==artifact.id || assets[0].size!==artifact.size ||
      assets[0].digest!==`sha256:${artifact.sha256}` || assets[0].browser_download_url!==artifact.url)
    throw new Error('Official GitHub asset metadata differs from the pinned OBS artifact');
  return assets[0];
}
export async function verifyPinnedSource(directory, revision) {
  let actual;
  try{actual=(await run('git',['-C',directory,'rev-parse','HEAD'])).stdout.trim();}
  catch(e){throw new Error(`Expected pinned source revision ${revision}`,{cause:e});}
  if(actual!==revision)throw new Error(`Expected pinned source revision ${revision}`);
  const dirty=(await run('git',['-C',directory,'status','--porcelain','--untracked-files=no'])).stdout;
  if(dirty.trim())throw new Error('Pinned source contains modified tracked files');
}
export async function prepareFFmpegPublicHeaders(source) {
  await verifyPinnedSource(source,dependencyLock.sources.ffmpeg.revision);
  const configure=await readFile(join(source,'configure'),'utf8');
  if(configure.match(/HAVE_LIST_PUB="([^"]*)"/)?.[1].trim().split(/\s+/).join(',')!=='bigendian,fast_unaligned')
    throw new Error('Pinned FFmpeg public configuration contract changed');
  const data=Buffer.from((await readFile(new URL('./ffmpeg-avconfig-win-x64.h',import.meta.url),'utf8')).replaceAll('\r\n','\n'));
  if(createHash('sha256').update(data).digest('hex')!==dependencyLock.ffmpegPublicHeader.sha256)throw new Error('Known generated header checksum mismatch');
  const destination=join(source,'libavutil/avconfig.h');
  if(await exists(destination)){
    if(!(await readFile(destination)).equals(data))throw new Error('Refusing to replace a different generated header');
  }else await writeFile(destination,data,{flag:'wx'});
}
async function clonePinned(directory, source) {
  await mkdir(directory);
  await run('git',['-C',directory,'init']);await run('git',['-C',directory,'remote','add','origin',source.url]);
  await run('git',['-C',directory,'-c','protocol.file.allow=never','fetch','--depth=1','origin',source.revision],{timeout:300000});
  await run('git',['-C',directory,'-c','advice.detachedHead=false','checkout','--detach','FETCH_HEAD']);
  await verifyPinnedSource(directory,source.revision);
}
async function copyTree(source, destination) {
  const info=await lstat(source);if(info.isSymbolicLink())throw new Error('Linked source notice is not allowed');
  if(info.isDirectory()){
    await mkdir(destination,{recursive:true});
    for(const name of (await readdir(source)).sort())await copyTree(join(source,name),join(destination,name));
  }else if(info.isFile()){await mkdir(dirname(destination),{recursive:true});await copyFile(source,destination);}
  else throw new Error('Unsupported source notice entry');
}
async function noticeFiles(directory,prefix='') {
  const files=[];
  for(const name of (await readdir(directory)).sort()){
    const path=join(directory,name), item=await lstat(path);
    if(item.isDirectory())files.push(...await noticeFiles(path,prefix+name+'/'));
    else files.push(prefix+name);
  }
  return files;
}
export async function assembleWindowsObsNotices(destination,{obsSource,ffmpegSource,obsDepsSource,simdeSource}={}) {
  for(const[name,path]of Object.entries({obs:obsSource,ffmpeg:ffmpegSource,obsDeps:obsDepsSource,simde:simdeSource}))
    await verifyPinnedSource(path,dependencyLock.sources[name].revision);
  if(await exists(destination))throw new Error('Notice destination already exists');
  const common=join(repository,'third-party/obs-runtime');
  const inventory=JSON.parse(await readFile(join(common,'inventory.json'),'utf8'));
  // Platform recipes differ; replace macOS static codec entries below with Windows recipes.
  inventory.components=inventory.components.filter(c=>!['aom','libtheora','lame','opus','libogg','libvorbis','libvpx','jansson'].includes(c.name));
  await mkdir(destination);
  for(const name of await readdir(common))if(name.endsWith('.txt')||name==='FFmpeg-LICENSE.md')await copyTree(join(common,name),join(destination,name));
  for(const name of ['COPYING.GPLv3','COPYING.LGPLv3'])await copyTree(join(ffmpegSource,name),join(destination,`FFmpeg-${name}.txt`));
  inventory.platform='win-x64';inventory.runtimeArtifact=dependencyLock.windowsRuntime;
  inventory.components.find(c=>c.name.startsWith('FFmpeg ')).license='GPL-3.0-or-later';
  const ffmpeg=inventory.components.find(c=>c.name.startsWith('FFmpeg '));
  ffmpeg.licenseFiles=[...new Set([...ffmpeg.licenseFiles,'FFmpeg-COPYING.GPLv3.txt','FFmpeg-COPYING.LGPLv3.txt'])];
  ffmpeg.notes='Official Windows x64 DLL configure string enables GPL and version3, with static aom, SVT-AV1, Theora, LAME, Opus, Vorbis and VPX. Their complete notice trees are copied below.';
  const depsRevision=dependencyLock.sources.obsDeps.revision;
  const packages=[
    ['aom','3.13.1','BSD-2-Clause','deps.ffmpeg/40-aom.ps1'],
    ['svt-av1','2.3.0','BSD-3-Clause-Clear','deps.ffmpeg/30-svt-av1.ps1'],
    ['libtheora','1.1.1','BSD-3-Clause','deps.ffmpeg/50-libtheora.ps1'],
    ['lame','3.100','LGPL-2.0-or-later','deps.ffmpeg/60-lame.ps1'],
    ['opus','1.5.2','BSD-3-Clause','deps.ffmpeg/20-opus.ps1'],
    ['libogg','1.3.5','BSD-3-Clause','deps.ffmpeg/30-libogg.ps1'],
    ['libvorbis','1.3.7','BSD-3-Clause','deps.ffmpeg/30-libvorbis.ps1'],
    ['libvpx','1.14.1','BSD-3-Clause','deps.ffmpeg/30-libvpx.ps1'],
    ['amf','obs-deps 2026-10-23','MIT','deps.ffmpeg/80-amf.ps1'],
    ['nv-codec-headers','obs-deps 2026-10-23','MIT','deps.ffmpeg/70-nv-codec.ps1'],
    ['vpl','obs-deps 2026-10-23','MIT','deps.windows/70-vpl.ps1'],
    ['jansson','obs-deps 2026-10-23','MIT','deps.windows/30-jansson.ps1'],
    ['curl','obs-deps 2026-10-23','curl','deps.windows/30-curl.ps1'],
    ['zlib','obs-deps 2026-10-23','Zlib','deps.ffmpeg/10-zlib.ps1'],
  ];
  for(const[name,version,license,script]of packages){
    const output=join(destination,'obs-deps',name);await copyTree(join(obsDepsSource,'licenses',name),output);
    inventory.components.push({name,version,license,source:`https://github.com/obsproject/obs-deps/blob/${depsRevision}/${script}`,
      licenseFiles:(await noticeFiles(output)).map(f=>`obs-deps/${name}/${f}`),notes:'Includes retained nested dependency copyright, license and patent notices. Exact source hashes, build options and patches are in the linked pinned build recipe.'});
  }
  for(const name of ['COPYING','COPYING.LIB','CONTRIBUTORS'])await copyTree(join(obsSource,'deps/w32-pthreads',name),join(destination,'w32-pthreads',name));
  inventory.components.push({name:'OBS w32-pthreads',version:'2.9.1 (OBS 33 vendored)',license:'LGPL-2.0-or-later',
    source:`https://github.com/obsproject/obs-studio/tree/${dependencyLock.sources.obs.revision}/deps/w32-pthreads`,
    licenseFiles:['w32-pthreads/COPYING','w32-pthreads/COPYING.LIB','w32-pthreads/CONTRIBUTORS']});
  inventory.prerequisites=['Windows 10 x64 1903 or newer for WGC; hosted synthetic tests do not validate physical WGC',
    'Microsoft VC runtime vcruntime140.dll, vcruntime140_1.dll and msvcp140.dll installed in SystemDirectory; these DLLs are not redistributed by this validation runtime'];
  inventory.components[1].notes='Selected libobs modules; full upstream Windows win-capture entry is excluded. OpenCam builds only pinned duplicator-monitor-capture.c and cursor-capture.c with its owned display-only module entry, excluding game hooks/updaters.';
  await writeFile(join(destination,'inventory.json'),JSON.stringify(inventory,null,2)+'\n',{flag:'wx'});
  await writeFile(join(destination,'SOURCE-INFO.md'),`# Windows OBS core validation provenance\n\nRuntime source provenance. Assembly produces a disabled stage; verified application packaging activates its own copy.\n\nOBS binary: ${dependencyLock.windowsRuntime.url}\nSHA-256: ${dependencyLock.windowsRuntime.sha256}\n\nOBS/dependency source pins and upstream recipe links are in inventory.json and scripts/obs-dependencies.lock.json. Preserve corresponding source and patches with any later binary distribution. Official packages include authenticated, unmodified app-local Microsoft release CRT files beside the helper; their full VS2022 license and signed-file provenance are inventoried separately. No SystemDirectory DLLs are copied.\n\nThe display-only capture module is compiled from the two pinned OBS display/cursor source files plus native/obs-capture/windows-display-module.c in the exact OpenCam validation commit. It never links the upstream hook/updater module entry. GPU probe executables and their exact imported DLL closure are explicit inputs. No OBS UI, browser or game injection payload is staged.\n`,{flag:'wx'});
  return inventory;
}
export async function prepareObsValidation(destination,{archivePath}={}) {
  destination=resolve(destination);if(await exists(destination))throw new Error('Acquisition directory already exists');
  await mkdir(destination);
  const headers={Accept:'application/vnd.github+json','User-Agent':'OpenCam-OBS-core-validation'};
  if(process.env.GH_TOKEN)headers.Authorization=`Bearer ${process.env.GH_TOKEN}`;
  const response=await fetch(dependencyLock.releaseApi,{headers,signal:AbortSignal.timeout(30000)});
  if(!response.ok)throw new Error(`Official OBS metadata HTTP ${response.status}`);
  const metadata=await response.json();verifyReleaseAsset(metadata);
  await writeFile(join(destination,'release-metadata.json'),JSON.stringify(metadata,null,2)+'\n',{flag:'wx'});
  const artifact=dependencyLock.windowsRuntime;
  archivePath=archivePath?resolve(archivePath):join(destination,artifact.name);
  if(!await exists(archivePath)){
    const download=await fetch(artifact.url,{signal:AbortSignal.timeout(300000)});
    if(!download.ok||!download.body)throw new Error(`Pinned OBS download HTTP ${download.status}`);
    await pipeline(download.body,createWriteStream(archivePath,{flags:'wx'}));
  }
  await verifyArchive(archivePath,artifact);
  const runtime=join(destination,'obs-runtime');await mkdir(runtime);await run('tar',['-xf',archivePath,'-C',runtime],{timeout:120000,maxBuffer:1024*1024});
  const paths={};
  const cloned=await Promise.allSettled(Object.entries(dependencyLock.sources).map(async([name,source])=>{
    paths[name]=join(destination,`${name}-source`);await clonePinned(paths[name],source);
  }));
  for(const result of cloned)if(result.status==='rejected')throw result.reason;
  await prepareFFmpegPublicHeaders(paths.ffmpeg);
  const notices=join(destination,'notices');
  await assembleWindowsObsNotices(notices,{obsSource:paths.obs,ffmpegSource:paths.ffmpeg,simdeSource:paths.simde,obsDepsSource:paths.obsDeps});
  const report={schema:1,obsVersion:dependencyLock.obsVersion,runtime,notices,sources:paths,archive:artifact,ffmpegPublicHeader:dependencyLock.ffmpegPublicHeader,enabled:false};
  await writeFile(join(destination,'acquisition.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});return report;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
  const[, ,destination,...args]=process.argv;
  if(!destination)throw new Error('Usage: node prepare-obs-validation.mjs NEW_DIRECTORY [--obs-archive EXISTING_VERIFIED_ZIP]');
  if(args.length&&!(args.length===2&&args[0]==='--obs-archive'))throw new Error('Unsupported acquisition arguments');
  await prepareObsValidation(destination,{archivePath:args[1]});process.stdout.write(`Pinned OBS validation inputs prepared in ${resolve(destination)}\n`);
}
