// SPDX-License-Identifier: AGPL-3.0-or-later
// Audit-oriented source delivery. This script does not make a legal compliance determination.
import {readFile,writeFile,mkdir,copyFile,lstat,rm} from 'node:fs/promises';
import {createReadStream,createWriteStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {pipeline} from 'node:stream/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {resolve,join} from 'node:path';
import {pathToFileURL} from 'node:url';

const run=promisify(execFile);
const commandOptions={timeout:300000,maxBuffer:16*1024*1024};
async function git(directory,...args){return (await run('git',['-c','core.hooksPath=/dev/null','-C',directory,...args],commandOptions)).stdout;}
async function hash(path){const digest=createHash('sha256');let size=0;for await(const bytes of createReadStream(path)){digest.update(bytes);size+=bytes.length;}return {sha256:digest.digest('hex'),size};}
function safeName(value){if(typeof value!=='string'||!/^[-a-zA-Z0-9][a-zA-Z0-9._-]*$/.test(value)||value.includes('..'))throw new Error(`Unsafe source identifier or archive name: ${value}`);return value;}
function safePath(value){if(typeof value!=='string'||!value||value.startsWith('/')||value.includes('\\')||value.split('/').some(p=>!p||p==='.'||p==='..')||/[\x00-\x1f]/.test(value))throw new Error(`Unsafe submodule path: ${value}`);return value;}
function https(value){const url=new URL(value);if(url.protocol!=='https:'||url.username||url.password)throw new Error(`Expected public HTTPS source URL: ${value}`);return value;}
function validate(lock){
  if(lock.schema!==1||!Array.isArray(lock.sources)||!lock.sources.length||!Array.isArray(lock.archives))throw new Error('Invalid source lock');
  const names=new Set();
  for(const s of [...lock.sources,...lock.archives]){safeName(s.id);if(names.has(s.id))throw new Error('Duplicate source id');names.add(s.id);https(s.url);}
  for(const s of lock.sources){if(!/^[a-f0-9]{40}$/.test(s.revision))throw new Error('Expected exact git revision');for(const e of s.excludedSubmodules??[]){safePath(e.path);if(!e.reason||!/^[a-f0-9]{40}$/.test(e.revision))throw new Error('Invalid submodule exclusion');}}
  const files=new Set();for(const a of lock.archives){safeName(a.filename);if(files.has(a.filename))throw new Error('Duplicate archive filename');files.add(a.filename);if(!/^[a-f0-9]{64}$/.test(a.sha256))throw new Error('Expected pinned archive checksum');}
}
async function verify(directory,revision){
  if((await git(directory,'rev-parse','HEAD')).trim()!==revision)throw new Error(`Source revision mismatch: ${revision}`);
  if((await git(directory,'status','--porcelain','--untracked-files=no','--ignore-submodules=all')).trim())throw new Error('Source has modified tracked files; clean pinned source required');
}
async function acquire(directory,source,local){
  if(local)await verify(local,source.revision);
  await mkdir(directory);await git(directory,'init','-q');
  // Copy exact local Git objects to our own checkout; never edit the supplied checkout.
  const remote=local?resolve(local):https(source.url);
  await git(directory,'-c',`protocol.file.allow=${local?'always':'never'}`,'fetch','--depth=1',remote,source.revision);
  await git(directory,'-c','advice.detachedHead=false','checkout','--detach','FETCH_HEAD');
  await verify(directory,source.revision);
  await mkdir(join(directory,'.git','info'),{recursive:true});
  // Git archive honors export-ignore by default. Source delivery must retain all tracked files.
  await writeFile(join(directory,'.git','info','attributes'),'* -export-ignore -export-subst\n');
}

export async function collectSources(destination,{lock,sourceDirectories={},submoduleDirectories={},archiveDirectory}={}){
  lock??=JSON.parse(await readFile(new URL('./obs-corresponding-sources.lock.json',import.meta.url),'utf8'));
  validate(lock);destination=resolve(destination);await mkdir(destination);
  const work=join(destination,'.work'),payload=join(destination,'sources');await mkdir(work);await mkdir(payload);
  const report={schema:1,purpose:'Source provenance audit; no legal compliance determination',sources:[],archives:[],excludedSubmodules:[],files:[]};
  await writeFile(join(payload,'source-lock.json'),JSON.stringify(lock,null,2)+'\n');
  async function capture(source,{parent,restorePath,local,ancestors=[]}={}){
    if(ancestors.includes(source.url+'@'+source.revision)||ancestors.length>32)throw new Error('Recursive submodule cycle or excessive depth');
    const index=report.sources.length,directory=join(work,String(index));
    await acquire(directory,source,local);
    const filename=`${index}-${safeName(source.id)}-${source.revision}.tar`;
    await git(directory,'archive','--format=tar',`--output=${join(payload,filename)}`,source.revision);
    const entry={id:source.id,url:source.url,revision:source.revision,archive:`sources/${filename}`,...(parent?{parent,restorePath}:{})};
    report.sources.push(entry);
    const links=(await git(directory,'ls-tree','-rz',source.revision)).split('\0').filter(s=>s.startsWith('160000 '));
    let modules=[];
    if(links.length){const config=await git(directory,'config','--blob',`${source.revision}:.gitmodules`,'--get-regexp','^submodule\\..*\\.(path|url)$');modules=config.trim().split('\n').map(line=>{const p=line.indexOf(' ');return [line.slice(0,p),line.slice(p+1)];});}
    const exclusions=source.excludedSubmodules??[];
    for(const line of links){
      const [,revision,path]=line.match(/^160000 commit ([a-f0-9]{40})\t(.+)$/s)??[];safePath(path);
      const exclusion=exclusions.find(e=>e.path===path);
      if(exclusion){if(exclusion.revision!==revision)throw new Error('Excluded submodule revision mismatch');report.excludedSubmodules.push({parent:source.id,...exclusion});continue;}
      const pathKey=modules.find(([key,value])=>key.endsWith('.path')&&value===path)?.[0];
      let url=modules.find(([key])=>key===pathKey?.replace(/\.path$/,'.url'))?.[1];
      if(!url)throw new Error(`Missing submodule URL: ${path}`);
      if(url.startsWith('./')||url.startsWith('../'))url=new URL(url,source.url+'/').href;
      https(url);
      await capture({id:`submodule-${report.sources.length}`,url,revision},{parent:source.id,restorePath:path,local:submoduleDirectories[revision],ancestors:[...ancestors,source.url+'@'+source.revision]});
    }
    for(const e of exclusions)if(!links.some(line=>line.endsWith('\t'+e.path)))throw new Error('Stale submodule exclusion');
    await verify(directory,source.revision);
  }
  for(const source of lock.sources){process.stderr.write(`Collect source: ${source.id}\n`);await capture(source,{local:sourceDirectories[source.id]});}
  for(const archive of lock.archives){
    process.stderr.write(`Collect archive: ${archive.id}\n`);
    const target=join(payload,archive.filename);
    if(archiveDirectory){const source=join(resolve(archiveDirectory),archive.filename);if(!(await lstat(source)).isFile())throw new Error('Supplied archive is not a regular file');await copyFile(source,target);}
    else {const response=await fetch(archive.url,{signal:AbortSignal.timeout(300000)});if(!response.ok)throw new Error(`Source archive HTTP ${response.status}: ${archive.id}`);await pipeline(response.body,createWriteStream(target,{flags:'wx'}));}
    const actual=await hash(target);if(actual.sha256!==archive.sha256)throw new Error(`Source archive checksum mismatch: ${archive.id}`);
    report.archives.push({...archive,path:`sources/${archive.filename}`,...actual});
  }
  const names=['source-lock.json',...report.sources.map(s=>s.archive.slice(8)),...lock.archives.map(a=>a.filename)];
  for(const name of names)report.files.push({path:`sources/${name}`,...await hash(join(payload,name))});
  await writeFile(join(payload,'SOURCES.json'),JSON.stringify(report,null,2)+'\n');
  await writeFile(join(payload,'RESTORE.txt'),'Source provenance audit, not a legal compliance determination.\nEach Git archive contains the full exact tracked source tree, including files marked export-ignore.\nExtract top-level Git archives into separate directories. For every child source, extract its archive at restorePath inside its parent directory. Nested parent ids form the recursive restoration map in SOURCES.json.\nOBS submodules excluded from the selected binary runtime are listed explicitly. obs-deps contains all pinned build recipes, checksums and applied patch inputs. Apply platform recipes to pristine source using their listed order and options; Windows uses .ps1, macOS uses .zsh. Upstream archive inputs are retained unchanged and checksum verified.\nThe separate matching OpenCam release source supplies the helper, modifications and packaging scripts. Runtime FFmpeg is 8.1.2; 8.1.3 is a separate helper-header input.\n');
  const bundle=join(destination,'OBS-SOURCES.tar.gz');await run('tar',['-czf',bundle,'-C',destination,'sources'],commandOptions);
  report.bundle={path:'OBS-SOURCES.tar.gz',...await hash(bundle)};
  await writeFile(join(destination,'OBS-SOURCES.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});
  await rm(work,{recursive:true,force:true});return report;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
  if(process.argv.length!==3)throw new Error('Usage: node collect-obs-corresponding-sources.mjs NEW_OUTPUT_DIRECTORY');
  const sourceDirectories=process.env.OPENCAM_OBS_SOURCE_DIRECTORIES?JSON.parse(process.env.OPENCAM_OBS_SOURCE_DIRECTORIES):{};
  await collectSources(process.argv[2],{sourceDirectories,archiveDirectory:process.env.OPENCAM_OBS_SOURCE_ARCHIVES});
}
