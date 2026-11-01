// SPDX-License-Identifier: AGPL-3.0-or-later
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {isAbsolute,join,resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
const repository=resolve(import.meta.dirname,'../..');
export async function packageGnomeExtension(output,commit) {
    if(!isAbsolute(output)||!/^[a-f0-9]{40}$/.test(commit))throw new Error('absolute_output_and_commit_required');
    const files=new Map();
    for(const name of ['metadata.json','extension.js','geometry.js','pointer.js','protocol.js','session.js'])
        files.set(name,await readFile(join(repository,'linux/gnome-extension',name)));
    for(const style of ['WarmIvory','TealSilver','RoundShort','HighContrast'])
        files.set(`artwork/${style}.png`,await readFile(join(repository,'src/ScreenRecorder.Core/Assets/TeachingPointers',style+'.png')));
    files.set('LICENSE',await readFile(join(repository,'LICENSE')));
    const inventory=Object.fromEntries([...files].map(([name,data])=>[name,createHash('sha256').update(data).digest('hex')]));
    await mkdir(output); // Fresh destination only; never overwrite or enable an extension.
    await mkdir(join(output,'artwork'));
    for(const [name,data] of files)await writeFile(join(output,name),data,{flag:'wx'});
    await writeFile(join(output,'OPENCAM-EXTENSION-INFO.json'),JSON.stringify({version:'1.2.0-beta',commit,
        status:'FEASIBILITY ONLY — NOT ENABLED IN OPENCAM',files:inventory},null,2),{flag:'wx'});
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
    try{await packageGnomeExtension(...process.argv.slice(2));}
    catch{process.stderr.write('Extension packaging failed; use a fresh absolute output and exact commit.\n');process.exitCode=1;}
}
