// SPDX-License-Identifier: AGPL-3.0-or-later
import { readdir, lstat, realpath, mkdir, copyFile, readFile, writeFile, chmod } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join, relative, resolve, dirname, isAbsolute } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';
const execute = promisify(execFile);
const pin = '72e86655715d9a25d69ce446aa4dd44ba58e10d2';
async function inventory(root) {
  const canonical = await realpath(root), files = [];
  async function visit(dir) {
    for (const name of await readdir(dir)) {
      const path = join(dir, name), stat = await lstat(path), target = await realpath(path);
      if (target !== canonical && !target.startsWith(canonical + '/')) throw new Error('Source symlink escapes audited prefix');
      if (stat.isDirectory()) await visit(path);
      else if (stat.isSymbolicLink()) {
        if ((await lstat(target)).isDirectory()) throw new Error('Directory symlink is not a runtime file');
        files.push({ path, target, name: relative(root, path), mode: (await lstat(target)).mode });
      } else if (stat.isFile()) files.push({ path, target, name: relative(root, path), mode: stat.mode });
      else throw new Error('Unsupported runtime file type');
    }
  }
  await visit(root); return files;
}
export async function writeLinuxRuntimeManifest(root) {
  const files = {};
  for (const file of await inventory(root)) if (file.name !== 'runtime.json')
    files[file.name] = createHash('sha256').update(await readFile(file.path)).digest('hex');
  await writeFile(join(root, 'runtime.json'), JSON.stringify({ enabled: true, protocol: 1,
    obsVersion: '33.0.0', platform: 'linux-x64', sourceRevision: pin,
    capabilities: { backends: ['x11', 'pipewire'], audio: ['none', 'system', 'microphone', 'both', 'silence'], encoders: ['cpu'] }, files }, null, 2));
}
export async function assembleLinuxApp({ publishRoot, nativeRoot, mediaRoot, outputRoot, revision, version, patchElf = false }) {
  if (![publishRoot, nativeRoot, mediaRoot, outputRoot].every(isAbsolute) || !/^[a-f0-9]{40}$/.test(revision) || version !== '1.2.0')
    throw new Error('Require absolute build roots, exact commit and unchanged version');
  const app = await inventory(publishRoot), native = await inventory(nativeRoot), media = await inventory(mediaRoot);
  if (!native.some(f => f.name === 'bin/OpenCam.ObsCapture')) throw new Error('Missing product helper');
  for (const name of ['ffmpeg', 'ffprobe']) if (!media.some(f => f.name === 'bin/' + name)) throw new Error('Missing media tool');
  if (!app.some(f => f.name === 'OpenCam')) throw new Error('Missing application');
  // Every input was inspected before creating the fresh destination.
  await mkdir(outputRoot);
  async function copy(file, destination) {
    await mkdir(dirname(destination), { recursive: true }); await copyFile(file.target, destination); await chmod(destination, file.mode & 0o777);
  }
  for (const file of app) await copy(file, join(outputRoot, file.name));
  const runtime = join(outputRoot, 'obs-runtime');
  for (const file of native) if (file.name.startsWith('share/') || /\.so(?:\.|$)/.test(file.name) || /^bin\/OpenCam\./.test(file.name))
    await copy(file, join(runtime, file.name));
  for (const file of media) {
    if (file.name === 'bin/ffmpeg' || file.name === 'bin/ffprobe') await copy(file, join(outputRoot, file.name.slice(4)));
    else if (file.name.startsWith('lib/') && /\.so(?:\.|$)/.test(file.name)) await copy(file, join(runtime, file.name));
  }
  if (patchElf) {
    if (process.platform !== 'linux') throw new Error('ELF relocation requires Linux');
    for (const file of await inventory(outputRoot)) {
      const data = await readFile(file.path);
      if (!data.subarray(0, 4).equals(Buffer.from([127, 69, 76, 70]))) continue;
      const rpath = file.name.startsWith('obs-runtime/lib/obs-plugins/') ? '$ORIGIN/..' :
        file.name.startsWith('obs-runtime/lib/') ? '$ORIGIN' : file.name.startsWith('obs-runtime/bin/') ? '$ORIGIN/../lib' : '$ORIGIN/obs-runtime/lib:$ORIGIN';
      await execute('patchelf', ['--set-rpath', rpath, file.path]);
    }
  }
  await writeLinuxRuntimeManifest(runtime);
  await writeFile(join(outputRoot, 'LINUX-TEST-INFO.json'), JSON.stringify({ version, commit: revision, platform: 'Ubuntu 24.04 x64',
    recordingCore: 'OBS/libobs 33.0.0', nativeOnly: true, compatibilityFallback: false,
    status: 'DEVELOPMENT TEST — NOT A RELEASE',
    manualValidation: ['physical audio/video and sync', 'GNOME portal permissions', 'multi-monitor DPI', 'desktop overlays/global shortcuts'],
    limitations: ['CPU encoding only', 'teaching pointer, screen annotations and global shortcuts require X11; annotations require a compositor',
      'Wayland recording requires an authorized persistent portal screen identity; no GNOME extension is installed'] }, null, 2));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [publishRoot, nativeRoot, mediaRoot, outputRoot, revision, version] = process.argv.slice(2);
  await assembleLinuxApp({ publishRoot, nativeRoot, mediaRoot, outputRoot, revision, version, patchElf: true });
}
