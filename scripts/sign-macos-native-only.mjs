// SPDX-License-Identifier: AGPL-3.0-or-later
import {readdir,readFile,rename,lstat} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {execFileSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';
export async function signMacNativeOnlyApp(app){
  const root=join(app,'Contents/MacOS');
  const resources=join(app,'Contents/Resources');
  const magic=b=>b.length>=4&&[0xfeedfacf,0xfeedface,0xcafebabe,0xbebafeca].includes(b.readUInt32LE(0));
  const data=[];
  for(const e of await readdir(root,{withFileTypes:true})){
    if(e.isSymbolicLink())throw Error('Symlink publish entry');
    if(e.isFile()&&magic(await readFile(join(root,e.name))))continue;
    try{await lstat(join(resources,e.name));throw Error('Resource destination already exists: '+e.name);}
    catch(error){if(error.code!=='ENOENT')throw error;}
    data.push(e.name);
  }
  // macOS treats everything under Contents/MacOS as code. Documents,
  // images and the inventoried private runtime must instead be sealed resources.
  for(const name of data)await rename(join(root,name),join(resources,name));
  async function sign(dir){for(const e of await readdir(dir,{withFileTypes:true})){
    // Signing the main executable by path signs the enclosing app too early.
    if(dir===root&&e.name==='OpenCam')continue;
    const p=join(dir,e.name);
    if(e.isDirectory())await sign(p);
    else if(e.isFile()){
      const b=await readFile(p);
      if(magic(b))
        execFileSync('codesign',['--force','--sign','-',p],{stdio:'inherit'});
    }
  }}
  await sign(root);
  execFileSync('codesign',['--force','--sign','-',app],{stdio:'inherit'});
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href)
  await signMacNativeOnlyApp(resolve(process.argv[2]));
