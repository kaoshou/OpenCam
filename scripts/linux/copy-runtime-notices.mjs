// SPDX-License-Identifier: AGPL-3.0-or-later
import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {pathToFileURL} from 'node:url';

export async function copyLinuxRuntimeNotices({destination,assetsPath}) {
  const assets=JSON.parse(await readFile(assetsPath,'utf8'));
  if(!assets.targets?.['net10.0/linux-x64']) throw Error('Missing Linux net10 publish assets');
  const dependency=assets.project?.frameworks?.['net10.0']?.downloadDependencies?.find(
    item=>item.name.toLowerCase()==='microsoft.netcore.app.runtime.linux-x64');
  const version=dependency?.version.match(/^\[([0-9.]+), \1\]$/)?.[1];
  const expected=(await readFile(new URL('../../DOTNET_RUNTIME_VERSION',import.meta.url),'utf8')).trim();
  if(!version || version!==expected) throw Error('Linux publish runtime differs from the supported pinned runtime');
  const packageName=`microsoft.netcore.app.runtime.linux-x64/${version}`;
  for(const folder of Object.keys(assets.packageFolders??{})) {
    let license,notices;
    try {
      license=await readFile(join(folder,packageName,'LICENSE.TXT'));
      notices=await readFile(join(folder,packageName,'THIRD-PARTY-NOTICES.TXT'));
    } catch { continue; }
    if(!license.length || !notices.length) continue;
    await writeFile(join(destination,'DOTNET-LICENSE.txt'),license,{flag:'wx'});
    await writeFile(join(destination,'DOTNET-THIRD-PARTY-NOTICES.txt'),notices,{flag:'wx'});
    return;
  }
  throw Error(`Missing exact Linux runtime notices: ${packageName}`);
}

if(process.argv[1] && import.meta.url===pathToFileURL(resolve(process.argv[1])).href) {
  const [destination,assetsPath]=process.argv.slice(2);
  if(!destination || !assetsPath) throw Error('Usage: copy-runtime-notices.mjs DESTINATION PUBLISH_ASSETS');
  await copyLinuxRuntimeNotices({destination,assetsPath});
}
