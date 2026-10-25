// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, readdir, symlink, lstat, rm, rename } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const moduleUrl = new URL('../../scripts/assemble-obs-runtime.mjs', import.meta.url);
const sha = data => createHash('sha256').update(data).digest('hex');
// Real minimal Mach-O load commands: changing platform checks or dependency closure breaks these fixtures.
function macho(dependencies = [], cpu = 0x100000c) {
  const commands = dependencies.map(name => {
    const b = Buffer.alloc(Math.ceil((25 + Buffer.byteLength(name)) / 8) * 8);
    b.writeUInt32LE(12); b.writeUInt32LE(b.length, 4); b.writeUInt32LE(24, 8); b.write(name, 24); return b;
  });
  const h = Buffer.alloc(32); h.writeUInt32LE(0xfeedfacf); h.writeUInt32LE(cpu, 4);
  h.writeUInt32LE(2, 12); h.writeUInt32LE(commands.length, 16);
  h.writeUInt32LE(commands.reduce((n, b) => n + b.length, 0), 20);
  return Buffer.concat([h, ...commands]);
}
function pe(dependencies = [], machine = 0x8664) {
  const b = Buffer.alloc(2048); b.write('MZ'); b.writeUInt32LE(128, 60); b.writeUInt32LE(0x4550, 128);
  b.writeUInt16LE(machine, 132); b.writeUInt16LE(1, 134); b.writeUInt16LE(240, 148); b.writeUInt16LE(0x20b, 152);
  b.writeUInt32LE(1024, 400); b.writeUInt32LE(0x1000, 404); b.writeUInt32LE(1024, 408); b.writeUInt32LE(512, 412);
  if (dependencies.length) b.writeUInt32LE(0x1000, 272);
  let nameOffset = 256;
  dependencies.forEach((name, n) => { b.writeUInt32LE(0x1000 + nameOffset, 512 + n * 20 + 12); b.write(name, 512 + nameOffset); nameOffset += name.length + 1; });
  return b;
}
async function winFixture(t) {
  const f = await fixture(t); f.platform = 'win-x64'; await writeFile(f.helper, pe(['obs.dll', 'avformat-62.dll', 'KERNEL32.dll']));
  f.displayModule = join(f.root, 'display-only.dll'); await writeFile(f.displayModule, Buffer.concat([pe(['obs.dll']), Buffer.from('display-only-build')]));
  await f.put('bin/64bit/obs.dll', pe()); await f.put('bin/64bit/libobs-d3d11.dll', pe(['obs.dll', 'D3D11.dll']));
  await f.put('bin/64bit/libobs-winrt.dll', pe(['obs.dll', 'windowsapp.dll']));
  await f.put('bin/64bit/obs-amf-test.exe', pe());
  await f.put('bin/64bit/avformat-62.dll', pe(['avcodec-62.dll'])); await f.put('bin/64bit/avcodec-62.dll', pe());
  await f.put('bin/64bit/Qt6Core.dll', pe()); await f.put('data/libobs/default.effect', 'effect');
  for (const name of ['obs-x264', 'obs-ffmpeg', 'win-capture', 'win-wasapi']) {
    await f.put(`obs-plugins/64bit/${name}.dll`, pe(['obs.dll']));
    await f.put(`data/obs-plugins/${name}/locale/en-US.ini`, 'locale');
  }
  await f.put('obs-plugins/64bit/obs-browser.dll', pe()); return f;
}
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'opencam-runtime-test-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const source = join(root, 'OBS.app'); const helper = join(root, 'helper');
  async function put(path, data = 'data') { await mkdir(dirname(join(source, path)), { recursive: true }); await writeFile(join(source, path), data); }
  await writeFile(helper, macho(['@rpath/libobs.framework/Versions/A/libobs']));
  await put('Contents/Info.plist', '<plist><dict><key>CFBundleShortVersionString</key><string>33.0.0</string></dict></plist>');
  await put('Contents/Frameworks/libobs.framework/Versions/A/libobs', macho(['@rpath/libavcodec.dylib']));
  await put('Contents/Frameworks/libobs.framework/Versions/A/Resources/default.effect', 'effect');
  await symlink('Versions/A/libobs', join(source, 'Contents/Frameworks/libobs.framework/libobs'));
  await put('Contents/Frameworks/libavcodec.dylib', macho());
  for (const name of ['libobs-metal.dylib', 'libobs-opengl.dylib']) await put(`Contents/Frameworks/${name}`, macho());
  for (const name of ['obs-x264', 'obs-ffmpeg', 'mac-capture', 'mac-videotoolbox', 'coreaudio-encoder']) {
    await put(`Contents/PlugIns/${name}.plugin/Contents/MacOS/${name}`, macho());
    await put(`Contents/PlugIns/${name}.plugin/Contents/Resources/locale/en-US.ini`, 'locale');
  }
  await put('Contents/PlugIns/obs-browser.plugin/Contents/MacOS/obs-browser', macho());
  await put('Contents/Frameworks/QtCore.framework/QtCore', macho());
  const licenses = join(root, 'licenses'); await mkdir(licenses);
  await writeFile(join(licenses, 'COPYING'), 'Example full license text for fixture');
  await writeFile(join(licenses, 'inventory.json'), JSON.stringify({ schema: 1, components: [{ name: 'OBS Studio', version: '33.0.0', license: 'GPL-2.0-or-later', source: 'https://github.com/obsproject/obs-studio/tree/72e86655715d9a25d69ce446aa4dd44ba58e10d2', licenseFiles: ['COPYING'] }] }));
  return { root, source, helper, licenses, put, destination: join(root, 'stage'), platform: 'osx-arm64' };
}
async function assemble(options) { const m = await import(moduleUrl); return m.assembleObsRuntime(options.destination, options); }
test('stages dependency closure with exhaustive hashes and no browser/frontend runtime', async t => {
  const f = await fixture(t); const manifest = await assemble(f);
  assert.equal(manifest.protocol, 1); assert.equal(manifest.enabled, false);
  assert.equal(manifest.platform, 'osx-arm64'); assert.equal(manifest.obsVersion, '33.0.0');
  assert.deepEqual(manifest.capabilities.backends, ['sck']);
  assert.deepEqual(manifest.capabilities.encoders, ['cpu', 'apple']);
  assert.ok(manifest.files['Contents/Frameworks/libavcodec.dylib']);
  assert.ok(manifest.files['Contents/Frameworks/libobs.framework/libobs']);
  assert.ok(!Object.keys(manifest.files).some(p => /browser|QtCore|frontend/.test(p)));
  async function check(directory, prefix = '') {
    for (const name of await readdir(directory)) {
      const p = prefix + name; const s = await lstat(join(directory, name)); assert.equal(s.isSymbolicLink(), false);
      if (s.isDirectory()) await check(join(directory, name), p + '/');
      else if (p !== 'runtime.json') assert.equal(manifest.files[p], sha(await readFile(join(directory, name))));
    }
  }
  await check(f.destination);
  assert.deepEqual(JSON.parse(await readFile(join(f.destination, 'runtime.json'), 'utf8')), manifest);
  const plist=await readFile(join(f.destination,'Contents/Info.plist'),'utf8');
  const version=(await readFile(new URL('../../VERSION',import.meta.url),'utf8')).trim();
  assert.ok(plist.includes(`<key>CFBundleShortVersionString</key><string>${version}</string>`));
});
test('refuses to overwrite an existing runtime directory', async t => {
  const f = await fixture(t); await mkdir(f.destination); await writeFile(join(f.destination, 'sentinel'), 'original');
  await assert.rejects(assemble(f), /already exists/i); assert.equal(await readFile(join(f.destination, 'sentinel'), 'utf8'), 'original');
});
test('rejects a framework symlink escaping the supplied official runtime', async t => {
  const f = await fixture(t); const external = join(f.root, 'outside'); await writeFile(external, 'private');
  await symlink(external, join(f.source, 'Contents/Frameworks/libobs.framework/Versions/A/escape'));
  await assert.rejects(assemble(f), /escap|outside/i);
  await assert.rejects(lstat(f.destination), { code: 'ENOENT' });
});
test('rejects wrong-architecture helpers before creating a destination', async t => {
  const f = await fixture(t); await writeFile(f.helper, macho([], 0x1000007));
  await assert.rejects(assemble(f), /architecture|platform/i);
  await assert.rejects(lstat(f.destination), { code: 'ENOENT' });
});
test('rejects a foreign platform binary hidden among selected plugin resources', async t => {
  const f = await fixture(t); await f.put('Contents/PlugIns/mac-capture.plugin/Contents/Resources/hidden.so', Buffer.from([0x7f,0x45,0x4c,0x46,2,1,1,0]));
  await assert.rejects(assemble(f), /platform binary/); await assert.rejects(lstat(f.destination), {code:'ENOENT'});
});
test('rejects unresolved or forbidden dynamically linked libraries', async t => {
  const f = await fixture(t); await f.put('Contents/Frameworks/libavcodec.dylib', macho(['@rpath/QtCore.framework/QtCore']));
  await assert.rejects(assemble(f), /dependency|allowlist/i);
});
test('rejects unsafe license inventory paths', async t => {
  const f = await fixture(t);
  await writeFile(join(f.licenses, 'inventory.json'), JSON.stringify({schema:1, components:[{name:'OBS',version:'33.0.0',license:'GPL-2.0-or-later',source:'https://github.com/obsproject/obs-studio',licenseFiles:['../outside']}]}));
  await assert.rejects(assemble(f), /path/i);
});
test('relocates an actual helper rpath without modifying its original binary', { skip: process.platform !== 'darwin' }, async t => {
  const f = await fixture(t); const c = join(f.root, 'helper.c');
  await writeFile(c, 'int main(void) { return 0; }\n');
  execFileSync('clang', ['-arch', 'arm64', c, '-Wl,-rpath,/tmp/opencam-build-only', '-o', f.helper]);
  const original = sha(await readFile(f.helper)); await assemble(f);
  const commands = execFileSync('otool', ['-l', join(f.destination, 'Contents/MacOS/OpenCam.ObsCapture')], {encoding:'utf8'});
  assert.ok(commands.includes('@loader_path/../Frameworks'));
  assert.ok(!commands.includes('/tmp/opencam-build-only'));
  assert.equal(sha(await readFile(f.helper)), original);
  assert.equal(execFileSync(join(f.destination, 'Contents/MacOS/OpenCam.ObsCapture')).length, 0);
});
test('build wrapper rejects unpinned source before creating build output', {skip: process.platform === 'win32'}, async t => {
  const f = await fixture(t); const build = join(f.root, 'build');
  const script = new URL('../../scripts/build-obs-capture.sh', import.meta.url);
  let result;
  try { execFileSync('bash', [fileURLToPath(script), f.source, f.source, f.source, f.source, build], {encoding:'utf8', stdio:'pipe'}); }
  catch (e) { result = e; }
  assert.ok(result); assert.match(result.stderr, /pinned OBS source revision/i);
  await assert.rejects(lstat(build), {code:'ENOENT'});
});
test('stages Windows PE imports and selected plugin data while excluding frontend DLLs', async t => {
  const f = await winFixture(t); const manifest = await assemble(f);
  assert.ok(manifest.files['OpenCam.ObsCapture.exe']); assert.ok(manifest.files['bin/64bit/avcodec-62.dll']);
  assert.ok(manifest.files['data/obs-plugins/win-wasapi/locale/en-US.ini']);
  assert.ok(!Object.keys(manifest.files).some(p => /browser|Qt6/.test(p)));
  assert.deepEqual(manifest.capabilities.backends, ['wgc']); assert.ok(manifest.capabilities.encoders.includes('cpu'));
});
test('includes the WGC support module loaded dynamically rather than via PE imports', async t => {
  const f = await winFixture(t); const manifest = await assemble(f);
  assert.ok(manifest.files['bin/64bit/libobs-winrt.dll']);
});
test('resolves pinned core Windows system imports without redistributing system DLLs', async t => {
  const f = await winFixture(t); await f.put('bin/64bit/libobs-d3d11.dll', pe(['msvcrt.dll', 'd3d9.dll', 'shcore.dll', 'rpcrt4.dll', 'ncrypt.dll', 'CoreMessaging.dll', 'AVICAP32.dll', 'WLDAP32.dll']));
  const manifest = await assemble(f);
  assert.ok(!Object.keys(manifest.files).some(p => /(?:msvcrt|d3d9|shcore|rpcrt4)\.dll$/.test(p)));
});
test('treats the installed Microsoft VC runtime as an explicit system prerequisite', async t => {
  const f = await winFixture(t); await f.put('bin/64bit/obs.dll', pe(['vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll', 'concrt140.dll']));
  const manifest = await assemble(f); assert.ok(!Object.keys(manifest.files).some(p => /(?:vcruntime|msvcp|concrt)/.test(p)));
});
test('does not advertise optional Windows GPU modules with missing probe helpers', async t => {
  const f = await winFixture(t); await f.put('obs-plugins/64bit/obs-nvenc.dll', pe()); await f.put('obs-plugins/64bit/obs-qsv11.dll', pe());
  const manifest = await assemble(f);
  assert.ok(!manifest.capabilities.encoders.includes('nvenc')); assert.ok(!manifest.capabilities.encoders.includes('qsv'));
  assert.ok(!manifest.files['obs-plugins/64bit/obs-nvenc.dll']);
});
test('stages exact optional GPU probe executables beside the Windows helper', async t => {
  const f = await winFixture(t);
  for (const [plugin, probe] of [['obs-nvenc', 'obs-nvenc-test'], ['obs-qsv11', 'obs-qsv-test']]) {
    await f.put(`obs-plugins/64bit/${plugin}.dll`, pe()); await f.put(`bin/64bit/${probe}.exe`, pe());
  }
  const manifest = await assemble(f);
  assert.ok(manifest.files['obs-nvenc-test.exe']); assert.ok(manifest.files['obs-qsv-test.exe']);
  assert.ok(manifest.capabilities.encoders.includes('nvenc')); assert.ok(manifest.capabilities.encoders.includes('qsv'));
  assert.ok(manifest.capabilities.encoders.includes('amf')); assert.ok(manifest.files['obs-amf-test.exe']);
});
test('normalizes the pinned OBS 33 core plugin ZIP layout into the private runtime layout', async t => {
  const f = await winFixture(t);
  await rename(join(f.source, 'obs-plugins'), join(f.source, 'legacy-ignored'));
  for (const name of ['obs-x264', 'obs-ffmpeg', 'win-capture', 'win-wasapi']) {
    await f.put(`core/${name}/${name}.dll`, pe(['obs.dll']));
    await f.put(`core/${name}/data/locale/en-US.ini`, 'official-33-locale');
  }
  const manifest = await assemble(f);
  assert.ok(manifest.files['obs-plugins/64bit/obs-x264.dll']);
  assert.equal(await readFile(join(f.destination, 'data/obs-plugins/obs-x264/locale/en-US.ini'), 'utf8'), 'official-33-locale');
  assert.ok(!Object.keys(manifest.files).some(p => p.startsWith('core/') || p.includes('legacy-ignored')));
});
test('excludes game injection binaries and configuration from Windows screen capture data', async t => {
  const f = await winFixture(t);
  await f.put('data/obs-plugins/win-capture/graphics-hook64.dll', pe());
  await f.put('data/obs-plugins/win-capture/inject-helper32.exe', pe([], 0x14c));
  await f.put('data/obs-plugins/win-capture/obs-vulkan64.json', '{}');
  const manifest = await assemble(f);
  assert.ok(manifest.files['data/obs-plugins/win-capture/locale/en-US.ini']);
  assert.ok(!Object.keys(manifest.files).some(p => /graphics-hook|inject-helper|vulkan/.test(p)));
});
test('does not advertise AMF without its exact native probe executable', async t => {
  const f = await winFixture(t); await rm(join(f.source, 'bin/64bit/obs-amf-test.exe'));
  const manifest = await assemble(f); assert.ok(!manifest.capabilities.encoders.includes('amf'));
});
test('rejects an unresolved Windows helper dependency', async t => {
  const f = await winFixture(t); await writeFile(f.helper, pe(['avformat-99.dll']));
  await assert.rejects(assemble(f), /Missing helper dependency/); await assert.rejects(lstat(f.destination), {code:'ENOENT'});
});
test('rejects a Windows capture plugin with the wrong PE architecture', async t => {
  const f = await winFixture(t); await writeFile(f.displayModule, pe([], 0xaa64));
  await assert.rejects(assemble(f), /architecture/); await assert.rejects(lstat(f.destination), {code:'ENOENT'});
});
test('requires a separately built display-only module and never falls back to the full capture plugin', async t => {
  const f = await winFixture(t); const manifest = await assemble(f);
  assert.equal(manifest.files['obs-plugins/64bit/win-capture.dll'], sha(await readFile(f.displayModule)));
  const missing = await winFixture(t); delete missing.displayModule;
  await assert.rejects(assemble(missing), /display-only module/); await assert.rejects(lstat(missing.destination), {code:'ENOENT'});
});
test('PowerShell build wrapper rejects unpinned source before creating build output', async t => {
  try { execFileSync('pwsh', ['-NoProfile', '-Command', 'exit 0'], {stdio:'ignore'}); } catch { t.skip('PowerShell unavailable'); return; }
  const f = await fixture(t); const build = join(f.root, 'build');
  const script = new URL('../../scripts/build-obs-capture.ps1', import.meta.url); let result;
  try { execFileSync('pwsh', ['-NoProfile', '-File', fileURLToPath(script), '-ObsSource', f.source, '-ObsRuntime', f.source, '-FFmpegHeaders', f.source, '-BuildDirectory', build], {encoding:'utf8', stdio:'pipe'}); }
  catch (e) { result = e; }
  assert.ok(result); assert.match(result.stderr, /pinned OBS source revision/i);
  await assert.rejects(lstat(build), {code:'ENOENT'});
});
test('rejects reusing a notice inventory from a different runtime platform', async t => {
  const f = await winFixture(t); const inventory = JSON.parse(await readFile(join(f.licenses, 'inventory.json'), 'utf8'));
  inventory.platform = 'osx-arm64'; await writeFile(join(f.licenses, 'inventory.json'), JSON.stringify(inventory));
  await assert.rejects(assemble(f), /inventory platform/); await assert.rejects(lstat(f.destination), {code:'ENOENT'});
});
test('provides owned bundle metadata so the relocated AppKit helper can initialize', {skip:process.platform !== 'darwin'}, async t => {
  const f = await fixture(t); const c = join(f.root, 'helper.m');
  await writeFile(c, '#import <Cocoa/Cocoa.h>\nint main(void) { fprintf(stderr, "bundle=%s executable=%s info=%s\\n", [[[NSBundle mainBundle] bundlePath] UTF8String], [[[NSBundle mainBundle] executablePath] UTF8String], [[[[NSBundle mainBundle] infoDictionary] description] UTF8String]); [NSApplication sharedApplication]; return 0; }\n');
  execFileSync('clang', ['-arch', 'arm64', '-framework', 'Cocoa', c, '-Wl,-rpath,/tmp/opencam-build-only', '-o', f.helper]);
  try { execFileSync(f.helper, {stdio:'pipe'}); } catch(e) { assert.fail(`original ${e.signal}: ${e.stderr}`); }
  const manifest = await assemble(f);
  try { assert.equal(execFileSync(join(f.destination, 'Contents/MacOS/OpenCam.ObsCapture'), {stdio:'pipe'}).length, 0); }
  catch(e) { assert.fail(`${e.signal}: ${e.stderr}`); }
  const plist = await readFile(join(f.destination, 'Contents/Info.plist'), 'utf8');
  assert.match(plist, /org\.opencam\.recordingcore/); assert.ok(!plist.includes('com.obsproject.obs-studio'));
  assert.equal(manifest.files['Contents/Info.plist'], sha(plist));
});
