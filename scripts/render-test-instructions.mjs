// SPDX-License-Identifier: AGPL-3.0-or-later
import {readFile,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {validateVersionText} from './check-version-consistency.mjs';

export function renderTestInstructions(template,version){
  validateVersionText(version+'\n');
  if(!template.includes('{{VERSION}}'))throw Error('Missing version placeholder');
  return template.replaceAll('{{VERSION}}',version);
}
if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
  const [template,output]=process.argv.slice(2);
  if(!output)throw Error('Usage: render-test-instructions.mjs TEMPLATE OUTPUT');
  const version=validateVersionText(await readFile(new URL('../VERSION',import.meta.url),'utf8'));
  await writeFile(output,renderTestInstructions(await readFile(template,'utf8'),version),{flag:'wx'});
}
