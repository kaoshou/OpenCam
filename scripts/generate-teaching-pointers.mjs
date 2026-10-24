// SPDX-License-Identifier: AGPL-3.0-or-later
// Build-time only: npm-compatible sharp, not an application dependency.
// OPENCAM_ASSET_MODULE_ROOT may point at an existing trusted node_modules directory.
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const require = createRequire(process.env.OPENCAM_ASSET_MODULE_ROOT
  ? process.env.OPENCAM_ASSET_MODULE_ROOT.replace(/\/$/,'') + '/asset-tool.cjs' : import.meta.url);
const sharp = require('sharp');
for (const name of ['WarmIvory','TealSilver','RoundShort','HighContrast']) {
  const source = new URL('../src/ScreenRecorder.Core/Assets/TeachingPointers/' + name + '.svg', import.meta.url);
  const png = await sharp(await readFile(source)).resize(280,240).ensureAlpha().toColourspace('srgb')
    .png({compressionLevel:9,palette:false}).toBuffer();
  await writeFile(fileURLToPath(source).replace(/\.svg$/,'.png'),png);
  console.log('Generated ' + name + ': 280x240 RGBA; logical hotspot16,16');
}
