// SPDX-License-Identifier: AGPL-3.0-or-later
import {readdir,lstat,mkdir,copyFile,readFile,writeFile,chmod,mkdtemp,rm,access} from 'node:fs/promises';
import {join,resolve,isAbsolute,dirname} from 'node:path';
import {tmpdir} from 'node:os';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
const execute=promisify(execFile);
const required=['OpenCam','ffmpeg','ffprobe','OpenCam.PulsePlayback','Start-OpenCam.sh',
  'obs-runtime/bin/OpenCam.ObsCapture','LICENSE','NOTICE.md','THIRD-PARTY-NOTICES.md','SOURCE.txt',
  'linux-notices/DOTNET-LICENSE.txt','linux-notices/DOTNET-THIRD-PARTY-NOTICES.txt',
  'linux-notices/OBS-COPYING.txt','linux-notices/OBS-AUTHORS.txt','app-icon.png',
  'linux-notices/COPYING.GPLv2','linux-notices/COPYING.GPLv3','linux-notices/COPYING.LGPLv2.1',
  'linux-notices/obs-embedded/json11-LICENSE.txt','linux-notices/obs-embedded/libcaption-LICENSE.txt',
  'linux-notices/obs-embedded/BLAKE2-LICENSE.txt','linux-notices/obs-embedded/Khronos-platform-header.txt',
  'linux-notices/obs-embedded/EGL-platform-header.txt','linux-notices/obs-embedded/SIMDe-copyright.txt',
  'linux-notices/obs-embedded/uthash-copyright.txt','obs-runtime/runtime.json'];

export async function stageDeb({appRoot,stageRoot,version,revision}) {
  if(![appRoot,stageRoot].every(isAbsolute)||!/^\d+\.\d+\.\d+$/.test(version)||!/^[a-f0-9]{40}$/.test(revision))
    throw Error('Require absolute roots, numeric product version and exact revision');
  appRoot=resolve(appRoot); stageRoot=resolve(stageRoot);
  if(stageRoot===appRoot||stageRoot.startsWith(appRoot+'/')) throw Error('Stage must be outside application input');
  try { await access(stageRoot); throw Error('Destination already exists'); }
  catch(e) { if(e.code!=='ENOENT') throw e; }
  if(!(await lstat(appRoot)).isDirectory()||(await lstat(appRoot)).isSymbolicLink()) throw Error('Require regular input directory');
  const files=[];
  async function visit(dir,prefix='') {
    for(const entry of await readdir(dir,{withFileTypes:true})) {
      const name=prefix+entry.name,path=join(dir,entry.name),stat=await lstat(path);
      if(stat.isSymbolicLink()) throw Error('Symlink not permitted in deb input');
      if(stat.isDirectory()) await visit(path,name+'/');
      else if(stat.isFile()) files.push({name,path,mode:stat.mode});
      else throw Error('Require regular files in deb input');
    }
  }
  await visit(appRoot);
  const licenseNames=await readdir(new URL('../../third-party/licenses/',import.meta.url));
  for(const name of [...required,...licenseNames.map(n=>'linux-notices/project-licenses/'+n)])
    if(!files.some(f=>f.name===name)) throw Error('Incomplete package: missing '+name);
  const runtime=JSON.parse(await readFile(join(appRoot,'obs-runtime/runtime.json'),'utf8'));
  if(runtime.enabled!==true||runtime.platform!=='linux-x64'||!runtime.files?.['bin/OpenCam.ObsCapture'])
    throw Error('Incomplete native runtime inventory');
  for(const [name,hash] of Object.entries(runtime.files)) {
    if(name.startsWith('/')||name.includes('\\')||name.split('/').some(s=>!s||s==='.'||s==='..')||
      !/^[a-f0-9]{64}$/.test(hash)||!files.some(f=>f.name==='obs-runtime/'+name))
      throw Error('Invalid or missing native runtime inventory file');
    if(createHash('sha256').update(await readFile(join(appRoot,'obs-runtime',name))).digest('hex')!==hash)
      throw Error('Native runtime hash mismatch: '+name);
  }
  const info=JSON.parse(await readFile(join(appRoot,'LINUX-TEST-INFO.json'),'utf8'));
  if(info.version!==version||info.commit!==revision||info.platform!=='Ubuntu 24.04 x64'||
    info.nativeOnly!==true||info.compatibilityFallback!==false) throw Error('Package source identity/revision mismatch');
  const packages=(await readFile(new URL('./runtime-packages.txt',import.meta.url),'utf8')).trim().split('\n');
  if(!packages.length||packages.some(p=>!/[a-z0-9]/.test(p)||!/^[a-z0-9][a-z0-9+.-]+$/.test(p))) throw Error('Invalid runtime dependency manifest');
  await mkdir(stageRoot);
  async function put(name,data,mode=0o644) { const path=join(stageRoot,name); await mkdir(dirname(path),{recursive:true}); await writeFile(path,data,{mode}); }
  for(const f of files) {
    const path=join(stageRoot,'opt/opencam-beta',f.name); await mkdir(dirname(path),{recursive:true});
    await copyFile(f.path,path); await chmod(path,f.mode&0o111?0o755:0o644);
  }
  // Explicit modes also cover inputs whose archive extractor lost executable bits.
  for(const name of ['OpenCam','ffmpeg','ffprobe','OpenCam.PulsePlayback','Start-OpenCam.sh','obs-runtime/bin/OpenCam.ObsCapture'])
    await chmod(join(stageRoot,'opt/opencam-beta',name),0o755);
  await put('DEBIAN/control',`Package: opencam-beta\nVersion: ${version}~beta.${revision.slice(0,12)}\nSection: video\nPriority: optional\nArchitecture: amd64\nMaintainer: OpenCam contributors <noreply@github.com>\nDepends: libc6 (>= 2.39), ${packages.join(', ')}\nHomepage: https://github.com/kaoshou/OpenCam\nDescription: OpenCam screen recorder (Ubuntu Beta)\n Ubuntu 24.04 amd64 development build. OBS/libobs recording, X11 and CPU\n encoding only. Wayland and Linux teaching overlays are not yet enabled.\n Source revision: ${revision}\n`);
  await put('usr/bin/opencam-beta',`#!/bin/sh\n# SPDX-License-Identifier: AGPL-3.0-or-later\nif [ "$(id -u)" -eq 0 ]; then\n  echo 'Run OpenCam as your ordinary desktop user, not root.' >&2\n  exit 2\nfi\nexec /opt/opencam-beta/Start-OpenCam.sh "$@"\n`,0o755);
  await put('usr/share/applications/opencam-beta.desktop',`[Desktop Entry]\nType=Application\nName=OpenCam Beta\nComment=Screen recording and teaching videos (Ubuntu Beta)\nExec=/usr/bin/opencam-beta\nTryExec=/usr/bin/opencam-beta\nIcon=opencam-beta\nTerminal=false\nCategories=AudioVideo;Recorder;\nStartupNotify=true\n`);
  await put('usr/share/icons/hicolor/256x256/apps/opencam-beta.png',await readFile(join(appRoot,'app-icon.png')));
  await put('usr/share/doc/opencam-beta/copyright',await readFile(join(appRoot,'NOTICE.md')));
  await put('usr/share/doc/opencam-beta/THIRD-PARTY-NOTICES.md',await readFile(join(appRoot,'THIRD-PARTY-NOTICES.md')));
  await put('usr/share/doc/opencam-beta/SOURCE.txt',await readFile(join(appRoot,'SOURCE.txt')));
}

if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href) {
  const [appRoot,output,version,revision]=process.argv.slice(2);
  if(process.platform!=='linux'||!output||!isAbsolute(output)) throw Error('Deb build requires Linux and an absolute output path');
  const scratch=await mkdtemp(join(tmpdir(),'opencam-deb-'));
  try {
    const stageRoot=join(scratch,'stage'); await stageDeb({appRoot,stageRoot,version,revision});
    const archive=join(scratch,'app.deb');
    await execute('dpkg-deb',['--root-owner-group','--build',stageRoot,archive]);
    // Exclusive copy: never overwrite an earlier delivery.
    const {constants}=await import('node:fs'); await copyFile(archive,output,constants.COPYFILE_EXCL);
  } finally { await rm(scratch,{recursive:true,force:true}); }
}
