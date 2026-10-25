// SPDX-License-Identifier: AGPL-3.0-or-later
// Opt-in physical acceptance package only. The normal assembler stays disabled.
import { readFile, writeFile, lstat, readdir, realpath, cp } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, resolve, relative, isAbsolute } from 'node:path';
const [stageArg,publishArg,revision,version,authorization,profile]=process.argv.slice(2);
if(profile!==undefined&&profile!=='--native-only')throw Error('Unknown test profile');
const nativeOnly=profile==='--native-only';
if(authorization!=='--enable-user-test-only')throw Error('Requires explicit user-test authorization; not a release tool');
if(!stageArg||!publishArg||!/^[a-f0-9]{40}$/.test(revision??'')||!/^\d+\.\d+\.\d+$/.test(version??''))throw Error('Invalid test package source/version');
if((await lstat(stageArg)).isSymbolicLink()||(await lstat(publishArg)).isSymbolicLink())throw Error('Symlink package root');
const stage=await realpath(stageArg),publish=await realpath(publishArg),destination=join(publish,'obs-runtime');
if(nativeOnly){
  for(const name of await readdir(publish))if(name.toLowerCase()==='opencam.screencapture.exe')
    throw Error('Legacy screen capture helper is prohibited in a native-only package');
}
const between=relative(stage,publish);
if(!between||(!between.startsWith('..')&&!isAbsolute(between)))throw Error('Publish directory must be outside the source runtime');
for(const path of [destination,join(publish,'OBS-TEST-INFO.json')]){
  try{await lstat(path);throw Error('Test package output already exists');}
  catch(error){if(error.code!=='ENOENT')throw error;}
}
const manifestPath=join(stage,'runtime.json');
if(!(await lstat(manifestPath)).isFile()||(await lstat(manifestPath)).size>262144)throw Error('Invalid manifest');
const original=await readFile(manifestPath),manifest=JSON.parse(original);
if(manifest.enabled!==false||manifest.platform!=='win-x64'||manifest.protocol!==1||manifest.obsVersion!=='33.0.0'||
  manifest.sourceRevision!=='72e86655715d9a25d69ce446aa4dd44ba58e10d2'||!manifest.files||Array.isArray(manifest.files))throw Error('Expected pinned disabled Windows runtime');
const entries=Object.entries(manifest.files),listed=new Set(),hash=bytes=>createHash('sha256').update(bytes).digest('hex');
if(!entries.length||entries.length>8192)throw Error('Invalid runtime inventory size');
for(const [name,digest] of entries){
  if(name.length>=4096||name.includes('\\')||name.includes(':')||name.split('/').some(p=>!p||p==='.'||p==='..'||/[\x00-\x1f\x7f]/.test(p))||
    !/^[a-f0-9]{64}$/.test(digest)||listed.has(name.toLowerCase()))throw Error('Invalid runtime inventory entry');
  listed.add(name.toLowerCase());
}
if(!listed.has('opencam.obscapture.exe'))throw Error('Missing owned helper');
async function inspect(directory,prefix=''){
  for(const entry of await readdir(directory)){
    const path=join(directory,entry),name=prefix+entry,info=await lstat(path);
    if(info.isSymbolicLink())throw Error('Symlink runtime entry');
    if(info.isDirectory())await inspect(path,name+'/');
    else if(!info.isFile())throw Error('Unexpected runtime entry');
    else if(name!=='runtime.json'){
      if(!listed.has(name.toLowerCase())||!Object.hasOwn(manifest.files,name)||hash(await readFile(path))!==manifest.files[name])throw Error('Unlisted or changed runtime file');
    }
  }
}
await inspect(stage);
for(const [name,digest] of entries)if(hash(await readFile(join(stage,name)))!==digest)throw Error('Missing or changed runtime file');
await cp(stage,destination,{recursive:true,errorOnExist:true,force:false});
manifest.enabled=true;
const enabled=JSON.stringify(manifest,null,2)+'\n';
await writeFile(join(destination,'runtime.json'),enabled);
await writeFile(join(publish,'OBS-TEST-INFO.json'),JSON.stringify({testOnly:true,sourceRevision:revision,version,
  nativeOnly,compatibilityFallback:!nativeOnly,
  physicalCapture:'REQUIRES MANUAL VALIDATION',longDuration:'REQUIRES MANUAL VALIDATION',
  disabledManifestSha256:hash(original),enabledManifestSha256:hash(enabled)},null,2)+'\n',{flag:'wx'});
if(!original.equals(await readFile(manifestPath)))throw Error('Source runtime changed during packaging');
process.stdout.write(`Prepared explicit Windows user-test runtime: ${resolve(destination)}\n`);
