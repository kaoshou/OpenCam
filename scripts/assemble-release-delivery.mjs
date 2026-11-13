// SPDX-License-Identifier: AGPL-3.0-or-later
import {readFile,writeFile,mkdir,copyFile,lstat} from 'node:fs/promises';
import {createReadStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {join,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {checkReleaseSources} from './check-release-source-provision.mjs';
import {validateVersionText} from './check-version-consistency.mjs';

async function hash(path){const h=createHash('sha256');for await(const b of createReadStream(path))h.update(b);return h.digest('hex');}
async function file(path){const s=await lstat(path);if(!s.isFile()||s.isSymbolicLink()||s.size===0)throw Error('Invalid release input: '+path);}

export async function assembleReleaseDelivery({version,sourceDirectory,linuxDirectory,windowsDirectory,macDirectory,destination}){
  const canonical=validateVersionText(await readFile(new URL('../VERSION',import.meta.url),'utf8'));
  if(version!==canonical)throw Error('Release version differs from VERSION');
  try{await lstat(destination);throw Error('Release destination already exists');}catch(e){if(e.code!=='ENOENT')throw e;}
  await checkReleaseSources(undefined,sourceDirectory);
  const prefix=`OpenCam_v${version}`;
  const linuxNames=[`${prefix}_Linux_x64_Beta.deb`,`${prefix}_Linux_x64_Beta.tar.gz`,`${prefix}_Linux_Beta_CorrespondingSource.tar.gz`];
  const linuxSums=`${prefix}_Linux_Beta_SHA256SUMS.txt`;
  await file(join(linuxDirectory,linuxSums));
  const lines=(await readFile(join(linuxDirectory,linuxSums),'utf8')).trim().split(/\r?\n/);
  const records=new Map();
  for(const line of lines){
    const m=line.match(/^([a-f0-9]{64})  (?:\.\/)?([A-Za-z0-9_.-]+)$/);
    if(!m||records.has(m[2])||!linuxNames.includes(m[2]))throw Error('Invalid Linux checksum manifest');
    records.set(m[2],m[1]);
  }
  for(const name of linuxNames){await file(join(linuxDirectory,name));if(records.get(name)!==await hash(join(linuxDirectory,name)))throw Error('Linux source/binary checksum mismatch: '+name);}
  const components=['OBS-SOURCES.tar.gz','Windows-Media-SOURCES.tar.gz','macOS-Media-SOURCES.tar.gz','OBS-SOURCES.json','SOURCE-COVERAGE.json','SOURCE-INDEX.txt'];
  for(const name of components)await file(join(sourceDirectory,name));
  const binaries=[
    [join(windowsDirectory,'installer/Output',`${prefix}_Windows_x64_Setup.exe`),`${prefix}_Windows_x64_Setup.exe`],
    [join(windowsDirectory,`${prefix}_Windows_x64_Portable.zip`),`${prefix}_Windows_x64_Portable.zip`],
    [join(macDirectory,`${prefix}_macOS_arm64.dmg`),`${prefix}_macOS_arm64.dmg`],
    ...linuxNames.slice(0,2).map(n=>[join(linuxDirectory,n),n])
  ];
  for(const [path] of binaries)await file(path);
  await mkdir(destination);
  for(const [path,name] of binaries)await copyFile(path,join(destination,name));
  const bundle=`${prefix}_CorrespondingSource.tar.gz`;
  execFileSync('tar',['-czf',join(destination,bundle),'-C',resolve(sourceDirectory),...components,'-C',resolve(linuxDirectory),linuxNames[2],linuxSums],{stdio:'inherit'});
  const sums=[];
  for(const name of [...binaries.map(([,n])=>n),bundle])sums.push(`${await hash(join(destination,name))}  ${name}`);
  await writeFile(join(destination,`${prefix}_SHA256SUMS.txt`),sums.join('\n')+'\n',{flag:'wx'});
  return [...binaries.map(([,n])=>n),bundle,`${prefix}_SHA256SUMS.txt`];
}

if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
  const [destination,sourceDirectory,linuxDirectory,windowsDirectory,macDirectory]=process.argv.slice(2);
  if(!macDirectory)throw Error('Usage: assemble-release-delivery.mjs DESTINATION VERIFIED_SOURCES LINUX WINDOWS MACOS');
  const version=validateVersionText(await readFile(new URL('../VERSION',import.meta.url),'utf8'));
  const files=await assembleReleaseDelivery({version,destination,sourceDirectory,linuxDirectory,windowsDirectory,macDirectory});
  console.log('Verified compact Release delivery: '+files.join(', '));
}
