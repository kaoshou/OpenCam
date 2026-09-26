import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { assembleThirdPartyNotices } from '../../../scripts/assemble-third-party-notices.mjs';

// Test-only runtime/FFmpeg inputs. The collector and checked-in upstream texts are real.
export async function addThirdPartyFixture(directory, rid = 'osx-arm64') {
  const packages = join(directory, 'fixture-packages');
  const runtime = join(packages, `microsoft.netcore.app.runtime.${rid}`, '8.0.29');
  await mkdir(runtime, { recursive: true });
  for (const file of ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']) await writeFile(join(runtime, file), `test runtime ${file}`);
  const ffmpegDirectory = join(directory, 'fixture-ffmpeg');
  await mkdir(ffmpegDirectory, { recursive: true });
  for (const tool of ['ffmpeg', 'ffprobe']) for (const suffix of ['license', 'version'])
    await writeFile(join(ffmpegDirectory, `${tool}-${suffix}.txt`), `test ${tool} ${suffix}`);
  const assetsPath = join(directory, 'fixture-assets.json');
  await writeFile(assetsPath, JSON.stringify({
    targets: { [`net8.0/${rid}`]: { 'Serilog/4.4.0': { type: 'package', runtime: { 'lib/Serilog.dll': {} } } } },
    packageFolders: { [packages]: {} },
    project: { frameworks: { 'net8.0': { downloadDependencies: [
      { name: `Microsoft.NETCore.App.Runtime.${rid}`, version: '[8.0.29, 8.0.29]' }
    ] } } }
  }));
  await assembleThirdPartyNotices(directory, { rid, assetsPath, ffmpegDirectory });
  return { assetsPath, ffmpegDirectory };
}
