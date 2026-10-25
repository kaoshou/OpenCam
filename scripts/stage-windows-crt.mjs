// SPDX-License-Identifier: AGPL-3.0-or-later
import {readFile,writeFile,readdir,lstat,copyFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {windowsDependencies} from './assemble-obs-runtime.mjs';
const digest=b=>createHash('sha256').update(b).digest('hex');
const crtName=/^(?:vcruntime140(?:_[a-z0-9_]+)?|msvcp140(?:_[a-z0-9_]+)?|concrt140|vccorlib140)\.dll$/i;
export async function stageWindowsCrt(runtime,source,metadata){
  const manifest=JSON.parse(await readFile(join(runtime,'runtime.json')));
  if(manifest.platform!=='win-x64'||manifest.enabled!==false)throw Error('CRT staging requires a disabled Windows runtime');
  const evidence=JSON.parse(await readFile(join(metadata,'provenance.json')));
  const known=new Map();
  for(const file of evidence.files??[]){
    if(!crtName.test(file.name)||file.signatureStatus!=='Valid'||!/O=Microsoft Corporation(?:,|$)/.test(file.signerSubject??''))throw Error('Unauthenticated Microsoft CRT');
    const path=join(source,file.name),info=await lstat(path),bytes=await readFile(path);
    if(!info.isFile()||info.isSymbolicLink()||digest(bytes)!==file.sha256)throw Error('Changed CRT input');
    windowsDependencies(bytes);known.set(file.name.toLowerCase(),{file,bytes});
  }
  const requested=new Set(['vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll']);
  for(const [name,hash] of Object.entries(manifest.files)){
    const bytes=await readFile(join(runtime,name));
    if(digest(bytes)!==hash)throw Error('Changed original runtime');
    if(/\.(exe|dll)$/i.test(name))for(const dll of windowsDependencies(bytes))if(crtName.test(dll))requested.add(dll);
  }
  for(const name of requested){
    const entry=known.get(name);if(!entry)throw Error('Missing required app-local CRT: '+name);
    for(const dependency of windowsDependencies(entry.bytes))if(crtName.test(dependency))requested.add(dependency);
  }
  const license=await readFile(join(metadata,'MSVC-2022-LICENSE.docx'));
  if(digest(license)!==evidence.licenseSha256)throw Error('Changed CRT license');
  for(const name of requested)if((await readdir(runtime)).some(f=>f.toLowerCase()===name))throw Error('CRT output already exists');
  for(const name of requested){
    const entry=known.get(name);
    await writeFile(join(runtime,name),entry.bytes,{flag:'wx'});manifest.files[name]=digest(entry.bytes);
  }
  for(const [name,bytes]of [['MSVC-2022-LICENSE.docx',license],['Microsoft-VC-runtime.json',Buffer.from(JSON.stringify(evidence,null,2)+'\n')]]){
    await writeFile(join(runtime,'licenses',name),bytes,{flag:'wx'});manifest.files['licenses/'+name]=digest(bytes);
  }
  const inventoryPath=join(runtime,'licenses/inventory.json'),inventory=JSON.parse(await readFile(inventoryPath));
  inventory.prerequisites=(inventory.prerequisites??[]).filter(v=>!v.includes('Microsoft VC runtime'));
  inventory.components.push({name:'Microsoft Visual C++ release runtime (app-local)',version:evidence.toolset,
    license:'LicenseRef-Microsoft-Visual-Studio-2022',source:evidence.licenseUrl,
    licenseFiles:['MSVC-2022-LICENSE.docx','Microsoft-VC-runtime.json'],notes:'Unmodified authenticated release x64 CRT from VS2022 VC/redist; no system installation required.'});
  const bytes=Buffer.from(JSON.stringify(inventory,null,2)+'\n');await writeFile(inventoryPath,bytes);
  manifest.files['licenses/inventory.json']=digest(bytes);manifest.appLocalCrt=[...requested].sort();
  await writeFile(join(runtime,'runtime.json'),JSON.stringify(manifest,null,2)+'\n');
  console.log('PASS: authenticated app-local Microsoft CRT closure: '+[...requested].join(', '));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await stageWindowsCrt(...process.argv.slice(2).map(p=>resolve(p)));
