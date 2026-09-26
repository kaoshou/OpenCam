// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, writeFile, mkdir, readdir, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = resolve(import.meta.dirname, '..');
const hash = data => createHash('sha256').update(data).digest('hex');
export function noticePath(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
      || value.split('/').some(p => p === '.' || p === '..')) throw new Error('Unsafe third-party notice path');
  return value;
}

export async function assembleThirdPartyNotices(destination, {
  rid, assetsPath = join(root, 'src/ScreenRecorder.UI/obj/project.assets.json'),
  ffmpegDirectory, repository = root,
} = {}) {
  if (!['osx-arm64', 'win-x64'].includes(rid)) throw new Error('Unsupported notice RID');
  if (!ffmpegDirectory) throw new Error('Missing FFmpeg notice directory');
  const catalog = JSON.parse(await readFile(join(repository, 'third-party/catalog.json'), 'utf8'));
  const assets = JSON.parse(await readFile(assetsPath, 'utf8'));
  const target = assets.targets[`net8.0/${rid}`];
  if (!target) throw new Error(`Restore/publish assets for ${rid} before assembling notices`);
  const packages = Object.entries(target).filter(([name, item]) => item.type === 'package'
    && (item.runtime || item.native || item.runtimeTargets)
    && !/NativeAssets\.(Linux|WebAssembly)\//.test(name)
    && !(rid === 'osx-arm64' && /(?:NativeAssets\.Win32|Angle\.Windows\.Natives)\//.test(name))
    && !(rid === 'win-x64' && /NativeAssets\.macOS\//.test(name))).map(([name]) => name);
  for (const name of packages) {
    if (!catalog.packages.includes(name)) throw new Error(`Unreviewed third-party dependency: ${name}`);
  }
  const runtime = assets.project.frameworks.net8_0 ?? assets.project.frameworks['net8.0'];
  const dependency = runtime.downloadDependencies?.find(d => d.name.toLowerCase() === `microsoft.netcore.app.runtime.${rid}`);
  const version = dependency?.version.match(/^\[([0-9.]+), \1\]$/)?.[1];
  if (!version) throw new Error('Cannot identify exact self-contained .NET runtime');
  const packageName = `microsoft.netcore.app.runtime.${rid}/${version}`;
  let runtimeDirectory;
  for (const folder of Object.keys(assets.packageFolders)) {
    try { await readFile(join(folder, packageName, 'LICENSE.TXT')); runtimeDirectory = join(folder, packageName); break; } catch {}
  }
  if (!runtimeDirectory) throw new Error(`Missing runtime notices: ${packageName}`);
  const output = join(destination, 'third-party');
  const files = [];
  async function add(source, path, expected) {
    noticePath(path);
    const data = await readFile(source);
    if (!data.length || (expected && hash(data) !== expected)) throw new Error(`Invalid third-party notice: ${path}`);
    await mkdir(dirname(join(output, path)), { recursive: true });
    await writeFile(join(output, path), data);
    files.push({ path, sha256: hash(data) });
  }
  for (const f of catalog.files) await add(join(repository, 'third-party', noticePath(f.path)), f.path, f.sha256);
  for (const file of ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']) await add(join(runtimeDirectory, file), `runtime/${file}`);
  async function collect(directory, prefix) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) throw new Error('Linked FFmpeg notice file is not allowed');
      const path = `${prefix}/${entry.name}`;
      if (entry.isDirectory()) await collect(join(directory, entry.name), path);
      else if (entry.isFile()) await add(join(directory, entry.name), path);
    }
  }
  await collect(ffmpegDirectory, 'ffmpeg');
  for (const required of ['ffmpeg/ffmpeg-license.txt', 'ffmpeg/ffmpeg-version.txt', 'ffmpeg/ffprobe-license.txt', 'ffmpeg/ffprobe-version.txt']) {
    if (!files.some(f => f.path === required)) throw new Error(`Missing ${required}`);
  }
  await copyFile(join(repository, 'THIRD-PARTY-NOTICES.md'), join(destination, 'THIRD-PARTY-NOTICES.md'));
  await copyFile(join(repository, 'third-party/catalog.json'), join(output, 'catalog.json'));
  await writeFile(join(output, 'manifest.json'), JSON.stringify({ schema: 1, rid, runtime: packageName,
    packages, files }, null, 2) + '\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [, , destination, rid, ffmpegDirectory] = process.argv;
  if (!destination) throw new Error('Usage: assemble-third-party-notices destination rid ffmpeg-notices-directory');
  await assembleThirdPartyNotices(destination, { rid, ffmpegDirectory });
}
