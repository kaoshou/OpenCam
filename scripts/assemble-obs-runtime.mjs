// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, writeFile, mkdir, readdir, realpath, lstat, stat, copyFile, chmod } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, join, resolve, relative, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const run = promisify(execFile);

export const obsRevision = '72e86655715d9a25d69ce446aa4dd44ba58e10d2';
const macPlugins = ['obs-x264', 'obs-ffmpeg', 'mac-capture', 'mac-videotoolbox', 'coreaudio-encoder'];
const winPlugins = ['obs-x264', 'obs-ffmpeg', 'win-capture', 'win-wasapi', 'obs-nvenc', 'obs-qsv11'];
const macLibraries = new Set(['libobs.framework', 'libobs-metal.dylib', 'libobs-opengl.dylib',
  'libavcodec.dylib', 'libavdevice.dylib', 'libavfilter.dylib', 'libavformat.dylib', 'libavutil.dylib',
  'libswresample.dylib', 'libswscale.dylib', 'libx264.dylib', 'librist.dylib', 'libsrt.dylib',
  'libmbedcrypto.dylib', 'libmbedtls.dylib', 'libmbedx509.dylib']);
const winLibraries = /^(?:obs|libobs-d3d11|libobs-opengl|libobs-winrt|avcodec-\d+|avdevice-\d+|avfilter-\d+|avformat-\d+|avutil-\d+|swresample-\d+|swscale-\d+|libx264-\d+|librist|libsrt|srt|mbedcrypto|mbedtls|mbedx509|zlib|zlib1|libbz2|liblzma|libwinpthread-1|w32-pthreads|jansson|libcurl|libssl-\d+-x64|libcrypto-\d+-x64|vcruntime140(?:_1)?|msvcp140(?:_\d+)?|concrt140)\.dll$/i;
const winSystem = /^(?:api-ms-|ext-ms-)|^(?:kernel32|user32|advapi32|ole32|oleaut32|gdi32|shell32|shlwapi|shcore|comctl32|comdlg32|ws2_32|winmm|version|ntdll|bcrypt|crypt32|secur32|normaliz|iphlpapi|psapi|powrprof|dwmapi|uxtheme|propsys|setupapi|cfgmgr32|avrt|d3d9|d3d11|dxgi|d3dcompiler_47|opengl32|msimg32|winhttp|wtsapi32|dwrite|d2d1|mf|mfplat|mfreadwrite|mfuuid|ksuser|strmiids|runtimeobject|windowsapp|ucrtbase|msvcrt|rpcrt4|vcruntime140|vcruntime140_1|msvcp140|concrt140|ncrypt|coremessaging|avicap32|wldap32)\.dll$/i;
const hash = data => createHash('sha256').update(data).digest('hex');
function safePath(value) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
    || value.split('/').some(p => p === '.' || p === '..')) throw new Error('Unsafe runtime path');
  return value;
}
function inside(root, path) { const r = relative(root, path); return r === '' || (!r.startsWith('..' + sep) && r !== '..' && !r.startsWith(sep)); }
async function exists(path) { try { await lstat(path); return true; } catch (e) { if (e.code === 'ENOENT') return false; throw e; } }

// Read platform headers directly so staging works on either build host, without invoking binaries from the input.
function macDependencies(data, rpaths = []) {
  let image = data;
  if (data.length < 32) throw new Error('Invalid Mach-O platform binary');
  const magic = data.readUInt32BE(0);
  if (magic === 0xcafebabe || magic === 0xcafebabf) {
    const count = data.readUInt32BE(4), stride = magic === 0xcafebabf ? 32 : 20;
    if (count > 32 || 8 + count * stride > data.length) throw new Error('Invalid Mach-O architecture table');
    let found;
    for (let n = 0; n < count; n++) {
      const p = 8 + n * stride;
      if (data.readUInt32BE(p) !== 0x100000c) continue;
      const offset = stride === 32 ? Number(data.readBigUInt64BE(p + 8)) : data.readUInt32BE(p + 8);
      const size = stride === 32 ? Number(data.readBigUInt64BE(p + 16)) : data.readUInt32BE(p + 12);
      if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(size) || offset + size > data.length) throw new Error('Invalid Mach-O slice');
      found = data.subarray(offset, offset + size);
    }
    if (!found) throw new Error('Runtime architecture must contain arm64'); image = found;
  }
  if (image.readUInt32LE(0) !== 0xfeedfacf || image.readUInt32LE(4) !== 0x100000c) throw new Error('Runtime architecture must be macOS arm64');
  const commands = image.readUInt32LE(16), end = 32 + image.readUInt32LE(20); let p = 32;
  if (end > image.length || commands > 10000) throw new Error('Invalid Mach-O load commands');
  const deps = [];
  for (let n = 0; n < commands; n++) {
    if (p + 8 > end) throw new Error('Invalid Mach-O load command');
    const command = image.readUInt32LE(p), size = image.readUInt32LE(p + 4);
    if (size < 8 || p + size > end) throw new Error('Invalid Mach-O load command size');
    if ([12, 0x80000018, 0x8000001f, 0x80000023].includes(command)) {
      const offset = image.readUInt32LE(p + 8), zero = image.indexOf(0, p + offset);
      if (offset < 24 || offset >= size || zero < 0 || zero >= p + size) throw new Error('Invalid Mach-O dependency path');
      deps.push(image.toString('utf8', p + offset, zero));
    }
    if (command === 0x8000001c) {
      const offset = image.readUInt32LE(p + 8), zero = image.indexOf(0, p + offset);
      if (offset < 12 || offset >= size || zero < 0 || zero >= p + size) throw new Error('Invalid Mach-O rpath');
      rpaths.push(image.toString('utf8', p + offset, zero));
    }
    p += size;
  }
  return deps;
}
export function windowsDependencies(data) {
  if (data.length < 64 || data.toString('ascii', 0, 2) !== 'MZ') throw new Error('Invalid Windows platform binary');
  const p = data.readUInt32LE(60);
  if (p + 264 > data.length || data.readUInt32LE(p) !== 0x4550 || data.readUInt16LE(p + 4) !== 0x8664 || data.readUInt16LE(p + 24) !== 0x20b)
    throw new Error('Runtime architecture must be Windows x64');
  const sections = data.readUInt16LE(p + 6), optSize = data.readUInt16LE(p + 20), sectionStart = p + 24 + optSize;
  function rva(value) {
    for (let n = 0; n < sections; n++) {
      const s = sectionStart + n * 40; if (s + 40 > data.length) break;
      const start = data.readUInt32LE(s + 12), size = data.readUInt32LE(s + 16);
      if (value >= start && value < start + size) { const file = value - start + data.readUInt32LE(s + 20); if (file < data.length) return file; }
    }
    throw new Error('Invalid PE dependency address');
  }
  const deps = [];
  function imports(directory, stride, nameOffset, delayed = false) {
    const address = data.readUInt32LE(p + 24 + 112 + directory * 8); if (!address) return;
    let entry = rva(address);
    for (let n = 0; n < 4096; n++, entry += stride) {
      if (entry + stride > data.length) throw new Error('Invalid PE imports');
      if (data.subarray(entry, entry + stride).every(b => b === 0)) return;
      if (delayed && data.readUInt32LE(entry) !== 1) throw new Error('Unsupported PE delay-import addressing');
      const name = rva(data.readUInt32LE(entry + nameOffset)), zero = data.indexOf(0, name);
      if (zero < 0 || zero - name > 256) throw new Error('Invalid PE library name'); deps.push(data.toString('ascii', name, zero).toLowerCase());
    }
    throw new Error('Unbounded PE imports');
  }
  imports(1, 20, 12); imports(13, 32, 4, true); return deps;
}

export async function assembleObsRuntime(destination, { source, helper, licenses, platform, displayModule } = {}) {
  if (!['osx-arm64', 'win-x64'].includes(platform)) throw new Error('Unsupported runtime platform');
  if (!source || !helper || !licenses || !destination) throw new Error('Missing runtime source, helper, licenses or destination');
  destination = resolve(destination);
  if (await exists(destination)) throw new Error('Runtime destination already exists');
  let displayModuleInfo;
  if (platform === 'win-x64') {
    if (!displayModule) throw new Error('Windows runtime requires a built display-only module');
    displayModuleInfo = await lstat(displayModule);
    if (!displayModuleInfo.isFile() || displayModuleInfo.isSymbolicLink()) throw new Error('Display-only module must be a regular file');
    windowsDependencies(await readFile(displayModule));
  }
  const sourceRoot = await realpath(source), licenseRoot = await realpath(licenses);
  if (inside(sourceRoot, destination) || inside(destination, sourceRoot)) throw new Error('Runtime destination must be separate from source');
  const inputs = new Map(); const inspected = new Set();
  async function bounded(path, root) {
    const actual = await realpath(path); if (!inside(root, actual)) throw new Error('Runtime symlink escapes source'); return actual;
  }
  async function addTree(path, output, root = sourceRoot, ancestry = new Set()) {
    safePath(output); const actual = await bounded(path, root); const s = await stat(actual);
    if (s.isDirectory()) {
      if (ancestry.has(actual)) throw new Error('Cyclic runtime symlink');
      const next = new Set([...ancestry, actual]);
      for (const entry of (await readdir(actual)).sort()) await addTree(join(actual, entry), `${output}/${entry}`, root, next);
    } else if (s.isFile()) inputs.set(output, { actual, mode: s.mode });
    else throw new Error('Unsupported runtime filesystem entry');
  }
  const dependencies = platform === 'osx-arm64' ? macDependencies : windowsDependencies;
  const helperInfo = await lstat(helper); if (!helperInfo.isFile() || helperInfo.isSymbolicLink()) throw new Error('Helper must be a regular platform binary');
  const helperName = platform === 'osx-arm64' ? 'Contents/MacOS/OpenCam.ObsCapture' : 'OpenCam.ObsCapture.exe';
  dependencies(await readFile(helper)); inputs.set(helperName, { actual: resolve(helper), mode: helperInfo.mode | 0o111 });
  async function inspect(path, rootName, inputRoot = sourceRoot) {
    const actual = await bounded(path, inputRoot); if (inspected.has(actual)) return; inspected.add(actual);
    for (const dependency of dependencies(await readFile(actual))) {
      if (platform === 'osx-arm64') {
        if (dependency.startsWith('/System/Library/') || dependency.startsWith('/usr/lib/')) continue;
        let suffix;
        if (dependency.startsWith('@rpath/')) suffix = dependency.slice(7);
        else if (dependency.startsWith('@loader_path/')) {
          const target = resolve(dirname(actual), dependency.slice(13));
          suffix = relative(join(sourceRoot, 'Contents/Frameworks'), target).split(sep).join('/');
        } else throw new Error(`Unsupported runtime dependency: ${dependency}`);
        safePath(suffix); const component = suffix.split('/')[0];
        if (!macLibraries.has(component)) throw new Error(`Dependency outside runtime allowlist: ${dependency}`);
        await addTree(join(sourceRoot, 'Contents/Frameworks', component), `Contents/Frameworks/${component}`);
        await inspect(join(sourceRoot, 'Contents/Frameworks', suffix), component);
      } else {
        if (winSystem.test(dependency)) continue;
        if (!winLibraries.test(dependency)) throw new Error(`Dependency outside runtime allowlist: ${dependency}`);
        const files = await readdir(join(sourceRoot, 'bin/64bit'));
        const name = files.find(n => n.toLowerCase() === dependency);
        if (!name) throw new Error(`Missing runtime dependency: ${dependency}`);
        await addTree(join(sourceRoot, 'bin/64bit', name), `bin/64bit/${name}`); await inspect(join(sourceRoot, 'bin/64bit', name), rootName);
      }
    }
  }
  const encoders = ['cpu'];
  if (platform === 'osx-arm64') {
    const info = await readFile(join(sourceRoot, 'Contents/Info.plist'), 'utf8');
    if (!/<key>CFBundleShortVersionString<\/key>\s*<string>33\.0\.0<\/string>/.test(info)) throw new Error('Expected official OBS version 33.0.0');
    for (const library of ['libobs.framework', 'libobs-metal.dylib', 'libobs-opengl.dylib']) {
      await addTree(join(sourceRoot, 'Contents/Frameworks', library), `Contents/Frameworks/${library}`);
      await inspect(join(sourceRoot, 'Contents/Frameworks', library, ...(library.endsWith('.framework') ? ['libobs'] : [])), library);
    }
    for (const plugin of macPlugins) {
      const path = `Contents/PlugIns/${plugin}.plugin`;
      if (plugin === 'coreaudio-encoder' && !await exists(join(sourceRoot, path))) continue;
      await addTree(join(sourceRoot, path), path); await inspect(join(sourceRoot, path, 'Contents/MacOS', plugin), plugin);
    }
    // Inspect helper dependencies against the same allowlist (helper lives outside the source input).
    for (const dependency of macDependencies(await readFile(helper))) {
      if (dependency.startsWith('/System/Library/') || dependency.startsWith('/usr/lib/')) continue;
      if (!dependency.startsWith('@rpath/')) throw new Error('Helper dependency must use runtime-relative rpath');
      const suffix = safePath(dependency.slice(7));
      if (!macLibraries.has(suffix.split('/')[0])) throw new Error('Helper dependency outside runtime allowlist');
      await inspect(join(sourceRoot, 'Contents/Frameworks', suffix), 'helper');
    }
    encoders.push('apple');
  } else {
    // WGC support is dlopened by win-capture and therefore absent from PE import tables.
    for (const name of ['obs.dll', 'libobs-d3d11.dll', 'libobs-winrt.dll']) {
      await addTree(join(sourceRoot, 'bin/64bit', name), `bin/64bit/${name}`); await inspect(join(sourceRoot, 'bin/64bit', name), name);
    }
    await addTree(join(sourceRoot, 'data/libobs'), 'data/libobs');
    for (const plugin of winPlugins) {
      const path = `obs-plugins/64bit/${plugin}.dll`;
      const official = `core/${plugin}/${plugin}.dll`;
      const sourcePath = await exists(join(sourceRoot, official)) ? official : path;
      const probe = plugin === 'obs-nvenc' ? 'obs-nvenc-test.exe' : plugin === 'obs-qsv11' ? 'obs-qsv-test.exe' : undefined;
      if (plugin !== 'win-capture' && !await exists(join(sourceRoot, sourcePath))) { if (probe) continue; throw new Error(`Missing required runtime plugin ${plugin}`); }
      if (probe) {
        // Both modules launch a pinned support executable through libobs's executable-directory API.
        if (!await exists(join(sourceRoot, 'bin/64bit', probe))) continue;
        await addTree(join(sourceRoot, 'bin/64bit', probe), probe); await inspect(join(sourceRoot, 'bin/64bit', probe), plugin);
      }
      if (plugin === 'win-capture') {
        const actual = await realpath(displayModule);
        inputs.set(path, { actual, mode: displayModuleInfo.mode });
        await inspect(actual, 'display-only', dirname(actual));
      } else {
        await addTree(join(sourceRoot, sourcePath), path); await inspect(join(sourceRoot, sourcePath), plugin);
      }
      const sourceData = sourcePath === official ? `core/${plugin}/data` : `data/obs-plugins/${plugin}`;
      if (plugin === 'win-capture') {
        // Display capture uses the module translations, never the bundled game-injection payload.
        await addTree(join(sourceRoot, sourceData, 'locale'), `data/obs-plugins/${plugin}/locale`);
      } else if (await exists(join(sourceRoot, sourceData))) await addTree(join(sourceRoot, sourceData), `data/obs-plugins/${plugin}`);
      if (plugin === 'obs-nvenc') encoders.push('nvenc'); if (plugin === 'obs-qsv11') encoders.push('qsv');
    }
    // Pinned Windows x64 obs-ffmpeg launches this probe even though AMF lives in the main module.
    if (await exists(join(sourceRoot, 'bin/64bit/obs-amf-test.exe'))) {
      await addTree(join(sourceRoot, 'bin/64bit/obs-amf-test.exe'), 'obs-amf-test.exe');
      await inspect(join(sourceRoot, 'bin/64bit/obs-amf-test.exe'), 'obs-ffmpeg'); encoders.push('amf');
    }
    for (const dependency of windowsDependencies(await readFile(helper))) {
      if (winSystem.test(dependency)) continue;
      if (!winLibraries.test(dependency)) throw new Error('Helper dependency outside runtime allowlist');
      const name = (await readdir(join(sourceRoot, 'bin/64bit'))).find(n => n.toLowerCase() === dependency);
      if (!name) throw new Error(`Missing helper dependency ${dependency}`);
      await addTree(join(sourceRoot, 'bin/64bit', name), `bin/64bit/${name}`); await inspect(join(sourceRoot, 'bin/64bit', name), 'helper');
    }
  }
  // Validate every copied native binary, including duplicated framework aliases and plugin helpers.
  for (const [path, input] of inputs) {
    const data = await readFile(input.actual);
    const magic = data.length >= 4 ? data.readUInt32BE(0) : 0;
    if (magic === 0x7f454c46) throw new Error('Unexpected ELF platform binary in selected runtime tree');
    const native = [0xcafebabe, 0xcafebabf, 0xcffaedfe, 0xfeedfacf].includes(magic) || data.toString('ascii', 0, 2) === 'MZ';
    if (native || /\.(?:dll|exe|dylib|so)$/i.test(path) || /\/MacOS\//.test(path) || /\/libobs$/.test(path)) dependencies(data);
  }
  const inventory = JSON.parse(await readFile(join(licenseRoot, 'inventory.json'), 'utf8'));
  if (inventory.schema !== 1 || !Array.isArray(inventory.components) || !inventory.components.length) throw new Error('Missing license inventory');
  if (inventory.platform && inventory.platform !== platform) throw new Error('License inventory platform does not match runtime');
  if ((inventory.obsVersion && inventory.obsVersion !== '33.0.0') || (inventory.obsRevision && inventory.obsRevision !== obsRevision)) throw new Error('License inventory OBS source/version does not match runtime');
  for (const component of inventory.components) {
    if (!component.name || !component.version || !component.license || !/^https:\/\//.test(component.source) || !component.licenseFiles?.length) throw new Error('Incomplete source/license inventory');
    for (const file of component.licenseFiles) {
      safePath(file); const actual = await bounded(join(licenseRoot, file), licenseRoot);
      if (!(await stat(actual)).isFile() || !(await readFile(actual)).length) throw new Error('Missing full license text');
    }
  }
  await addTree(licenseRoot, 'licenses', licenseRoot);
  if (platform === 'osx-arm64') {
    const productVersion=(await readFile(new URL('../VERSION',import.meta.url),'utf8')).trim();
    // AppKit requires standard Contents/MacOS layout and owned main-bundle metadata.
    inputs.set('Contents/Info.plist', { mode: 0o644, data: Buffer.from(`<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>org.opencam.recordingcore</string>
<key>CFBundleName</key><string>OpenCam Recording Helper</string>
<key>CFBundleExecutable</key><string>OpenCam.ObsCapture</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>${productVersion}</string>
<key>CFBundleVersion</key><string>${productVersion}</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
<key>NSMicrophoneUsageDescription</key><string>OpenCam records the microphone you select for your recording.</string>
</dict></plist>\n`) });
  }
  // Preflight finishes before reserving the immutable new destination. No input is modified.
  await mkdir(destination);
  const files = {};
  // Sign the helper before copying the rest of the bundle; individual upstream runtime
  // binaries retain their original signatures. Distribution needs an app signing pass.
  for (const [path, input] of [...inputs].sort(([a], [b]) => a === helperName ? -1 : b === helperName ? 1 : a.localeCompare(b, 'en'))) {
    await mkdir(dirname(join(destination, path)), { recursive: true });
    if (input.data) await writeFile(join(destination, path), input.data, { flag: 'wx' });
    else await copyFile(input.actual, join(destination, path));
    await chmod(join(destination, path), input.mode & 0o777);
    if (platform === 'osx-arm64' && path === helperName) {
      const rpaths = []; macDependencies(await readFile(input.actual), rpaths);
      if (rpaths.length) {
        if (process.platform !== 'darwin') throw new Error('macOS helper relocation requires Apple install_name_tool and codesign');
        const stagedHelper = join(destination, path), args = [];
        for (const rpath of new Set(rpaths)) args.push('-delete_rpath', rpath);
        args.push('-add_rpath', '@loader_path/../Frameworks', stagedHelper);
        await run('install_name_tool', args);
        await run('codesign', ['--force', '--sign', '-', '--identifier', 'org.opencam.recordingcore', stagedHelper]);
      }
    }
    files[path] = hash(await readFile(join(destination, path)));
  }
  const manifest = { protocol: 1, enabled: false, platform, obsVersion: '33.0.0', sourceRevision: obsRevision,
    capabilities: { backends: [platform === 'osx-arm64' ? 'sck' : 'wgc'], audio: ['none', 'system', 'microphone', 'both', 'silence'], encoders }, files };
  await writeFile(join(destination, 'runtime.json'), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
  return manifest;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [, , destination, platform, source, helper, licenses, displayModule] = process.argv;
  if (!licenses) throw new Error('Usage: node assemble-obs-runtime.mjs NEW_DESTINATION osx-arm64|win-x64 OBS_RUNTIME HELPER LICENSE_DIRECTORY [BUILT_WINDOWS_DISPLAY_MODULE]');
  const result = await assembleObsRuntime(destination, { platform, source, helper, licenses, displayModule });
  process.stdout.write(`Staged disabled ${platform} OBS ${result.obsVersion} runtime (${Object.keys(result.files).length} hashed files).\n`);
}
