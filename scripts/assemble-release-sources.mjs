// SPDX-License-Identifier: AGPL-3.0-or-later
import {readFile,writeFile,mkdir,copyFile,mkdtemp,rm,lstat} from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {join,resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {pathToFileURL,fileURLToPath} from 'node:url';
import {checkReleaseSources} from './check-release-source-provision.mjs';
const repository=resolve(fileURLToPath(new URL('..',import.meta.url)));
const nameIsSafe=n=>typeof n==='string'&&/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(n)&&!n.includes('..');
async function hash(path){const d=createHash('sha256');for await(const b of createReadStream(path))d.update(b);return d.digest('hex');}
export async function verifyPinnedSources(root,entries){
  for(const entry of entries){
    if(!nameIsSafe(entry.name)||!/^[a-f0-9]{64}$/.test(entry.sha256))throw Error('Unsafe source archive record');
    const path=join(root,entry.name),info=await lstat(path);
    if(!info.isFile()||info.isSymbolicLink()||await hash(path)!==entry.sha256)throw Error('Changed source archive: '+entry.name);
  }
}
function unpack(archive,destination,expectedRoot){
  const names=execFileSync('tar',['-tzf',archive],{encoding:'utf8',maxBuffer:4*1024*1024}).trim().split('\n');
  if(names.some(n=>!n.startsWith(expectedRoot+'/')||n.includes('..')||n.includes('\\')))throw Error('Unsafe source bundle path');
  const types=execFileSync('tar',['-tvzf',archive],{encoding:'utf8',maxBuffer:4*1024*1024}).trim().split('\n');
  if(types.some(n=>!/^[-d]/.test(n)))throw Error('Source bundle contains links or special entries');
  execFileSync('tar',['-xzf',archive,'-C',destination],{stdio:'inherit'});
}
export async function assembleReleaseSources(destination,obs,windows,mac){
  const version=(await readFile(join(repository,'VERSION'),'utf8')).trim();
  const policy=JSON.parse(await readFile(join(repository,`docs/releases/v${version}-source-coverage.json`)));
  if(policy.status!=='policy-approved'||!policy.reviewedBy)throw Error('Source delivery policy not reviewed');
  await mkdir(destination);
  const temp=await mkdtemp(join(tmpdir(),'opencam-source-verify-'));
  const archives=[];
  try{
    const obsReport=JSON.parse(await readFile(join(obs,'OBS-SOURCES.json')));
    const obsBundle=join(obs,'OBS-SOURCES.tar.gz');
    if(await hash(obsBundle)!==obsReport.bundle.sha256)throw Error('Changed OBS source bundle');
    const sourceLock=JSON.parse(await readFile(join(repository,'scripts/obs-corresponding-sources.lock.json')));
    for(const source of sourceLock.sources)if(!obsReport.sources.some(s=>s.id===source.id&&s.url===source.url&&s.revision===source.revision))throw Error('OBS source pin missing: '+source.id);
    const obsUnpacked=join(temp,'obs');await mkdir(obsUnpacked);unpack(obsBundle,obsUnpacked,'sources');
    await verifyPinnedSources(join(obsUnpacked,'sources'),obsReport.files.map(f=>({name:f.path.replace(/^sources\//,''),sha256:f.sha256})));
    for(const source of sourceLock.archives)if(!obsReport.archives.some(s=>s.id===source.id&&s.sha256===source.sha256))throw Error('OBS upstream archive missing');
    await copyFile(obsBundle,join(destination,'OBS-SOURCES.tar.gz'));
    await copyFile(join(obs,'OBS-SOURCES.json'),join(destination,'OBS-SOURCES.json'));
    archives.push({name:'OBS-SOURCES.tar.gz',sha256:obsReport.bundle.sha256,covers:['obs-windows','obs-macos']});
    const winNotices=join(windows,'ffmpeg-notices');
    const winLock=JSON.parse(await readFile(join(repository,'scripts/windows-media-sources.lock.json')));
    await verifyPinnedSources(join(winNotices,'sources'),winLock.sources.map(s=>({name:s.id+'.archive',sha256:s.sha256})));
    for(const name of ['build-ffmpeg-windows.sh','windows-media-sources.lock.json'])
      if(await hash(join(winNotices,'sources',name))!==await hash(join(repository,'scripts',name)))throw Error('Wrong Windows source build recipe');
    if(await hash(join(winNotices,'sources/libvpl-mingw-secure-functions.patch'))!==await hash(join(repository,'scripts/patches/libvpl-mingw-secure-functions.patch')))throw Error('Missing source patch');
    execFileSync('tar',['-czf',join(destination,'Windows-Media-SOURCES.tar.gz'),'-C',windows,'ffmpeg-notices']);
    archives.push({name:'Windows-Media-SOURCES.tar.gz',sha256:await hash(join(destination,'Windows-Media-SOURCES.tar.gz')),covers:['ffmpeg-tools-windows']});
    const macBundle=join(mac,'macOS-Media-SOURCES.tar.gz'),macUnpacked=join(temp,'mac');await mkdir(macUnpacked);
    unpack(macBundle,macUnpacked,'ffmpeg-notices');
    const macScript=await readFile(join(repository,'scripts/build-ffmpeg-macos-arm64.sh'),'utf8');
    const ffmpegVersion=macScript.match(/ffmpeg_version="([^"]+)"/)[1],x264Commit=macScript.match(/x264_commit="([^"]+)"/)[1];
    await verifyPinnedSources(join(macUnpacked,'ffmpeg-notices/sources'),[
      {name:`ffmpeg-${ffmpegVersion}.tar.xz`,sha256:macScript.match(/ffmpeg_sha256="([^"]+)"/)[1]},
      {name:`x264-${x264Commit}.tar.gz`,sha256:macScript.match(/x264_sha256="([^"]+)"/)[1]},
      {name:'build-ffmpeg-macos-arm64.sh',sha256:await hash(join(repository,'scripts/build-ffmpeg-macos-arm64.sh'))}]);
    await copyFile(macBundle,join(destination,'macOS-Media-SOURCES.tar.gz'));
    archives.push({name:'macOS-Media-SOURCES.tar.gz',sha256:await hash(macBundle),covers:['ffmpeg-tools-macos']});
    const coverage={version,status:'reviewed',reviewedBy:policy.reviewedBy,archives};
    await writeFile(join(destination,'SOURCE-COVERAGE.json'),JSON.stringify(coverage,null,2)+'\n');
    await checkReleaseSources(coverage,destination);
    await writeFile(join(destination,'SOURCE-INDEX.txt'),`OpenCam ${version} corresponding-source delivery\n\nOBS-SOURCES.tar.gz: exact OBS, dependency recipes, patches and recursive source inputs. See OBS-SOURCES.json.\nWindows-Media-SOURCES.tar.gz: owned FFmpeg tools, exact source archives, build recipe, applied VPL patch and runtime license notices.\nmacOS-Media-SOURCES.tar.gz: exact FFmpeg/x264 tool source archives, build recipe and notices.\nSOURCE-COVERAGE.json: SHA-256 for these delivered archives.\nOpenCam helper and packaging modifications: matching v${version} GitHub source archive and SOURCE.txt commit.\nMicrosoft release CRT is unmodified app-local redistributable software, not AGPL; exact license and signed-file provenance remain in each Windows package.\nNo statement of legal certification or physical hardware acceptance is implied by source-provenance checks.\n`);
    console.log('PASS: verified corresponding-source archives and source delivery manifest');
  }finally{await rm(temp,{recursive:true,force:true});}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await assembleReleaseSources(...process.argv.slice(2).map(p=>resolve(p)));
