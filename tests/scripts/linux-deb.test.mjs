// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, rm, symlink, access} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';

const revision = '1234567890123456789012345678901234567890';
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'opencam deb '));
  t.after(() => rm(root, {recursive:true, force:true}));
  const appRoot = join(root, 'app'); await mkdir(appRoot);
  for (const name of ['OpenCam','ffmpeg','ffprobe','OpenCam.PulsePlayback','Start-OpenCam.sh',
    'obs-runtime/bin/OpenCam.ObsCapture','LICENSE','NOTICE.md','THIRD-PARTY-NOTICES.md','SOURCE.txt',
    'linux-notices/DOTNET-LICENSE.txt','linux-notices/DOTNET-THIRD-PARTY-NOTICES.txt',
    'linux-notices/OBS-COPYING.txt','linux-notices/OBS-AUTHORS.txt','app-icon.png',
    'linux-notices/COPYING.GPLv2','linux-notices/COPYING.GPLv3','linux-notices/COPYING.LGPLv2.1',
    'linux-notices/obs-embedded/json11-LICENSE.txt','linux-notices/obs-embedded/libcaption-LICENSE.txt',
    'linux-notices/obs-embedded/BLAKE2-LICENSE.txt','linux-notices/obs-embedded/Khronos-platform-header.txt',
    'linux-notices/obs-embedded/EGL-platform-header.txt','linux-notices/obs-embedded/SIMDe-copyright.txt',
    'linux-notices/obs-embedded/uthash-copyright.txt']) {
    const path = join(appRoot, name); await mkdir(join(path, '..'), {recursive:true});
    await writeFile(path, 'fixture', {mode:name.includes('.') ? 0o644 : 0o755});
  }
  const {readdir,copyFile}=await import('node:fs/promises');
  const licenseRoot=new URL('../../third-party/licenses/',import.meta.url);
  await mkdir(join(appRoot,'linux-notices/project-licenses'),{recursive:true});
  for(const name of await readdir(licenseRoot)) await copyFile(new URL(name,licenseRoot),join(appRoot,'linux-notices/project-licenses',name));
  await writeFile(join(appRoot,'obs-runtime/runtime.json'),JSON.stringify({platform:'linux-x64',enabled:true,
    files:{'bin/OpenCam.ObsCapture':createHash('sha256').update('fixture').digest('hex')}}));
  await writeFile(join(appRoot,'LINUX-TEST-INFO.json'), JSON.stringify({version:'1.2.0',commit:revision,
    platform:'Ubuntu 24.04 x64',nativeOnly:true,compatibilityFallback:false}));
  return {appRoot,stageRoot:join(root,'stage'), version:'1.2.0', revision};
}
async function builder() { return (await import('../../scripts/linux/build-deb.mjs')).stageDeb; }

test('dependency expressions allow only package names and explicit alternatives', async () => {
  const {parseDependencies}=await import('../../scripts/linux/build-deb.mjs');
  assert.deepEqual(parseDependencies('libicu74 | libicu76\nxdg-utils\n'), ['libicu74 | libicu76','xdg-utils']);
  for(const invalid of ['', 'libicu74 || libicu76', '-y', 'libicu74;echo', 'libicu74,libicu76',
    'libicu74 |', 'libicu74\n\nlibicu76', 'libicu74 $(id)', 'libicu74\r\n'])
    assert.throws(()=>parseDependencies(invalid), /dependency manifest/i, invalid);
});

test('the same deb accepts Ubuntu 24 ICU 74 or Ubuntu 25 ICU 76 without requiring both', async t => {
  const f=await fixture(t); await (await builder())(f);
  const control=await readFile(join(f.stageRoot,'DEBIAN/control'),'utf8');
  const dependencies=control.match(/^Depends: (.+)$/m)[1].split(', ');
  assert.ok(dependencies.includes('libicu74 | libicu76'));
  assert.ok(!dependencies.includes('libicu74'));
  assert.ok(!dependencies.includes('libicu76'));
});

test('deb tree installs a Beta desktop launcher and declares desktop/native dependencies',async t => {
  const f=await fixture(t); await (await builder())(f);
  const control=await readFile(join(f.stageRoot,'DEBIAN/control'),'utf8');
  assert.match(control,/^Package: opencam-beta$/m); assert.match(control,/^Version: 1\.2\.0~beta\.[a-f0-9]+$/m);
  assert.match(control,/^Architecture: amd64$/m); assert.match(control,/libc6 \(>= 2\.39\)/);
  assert.match(control,/Wayland recording/);
  assert.match(control,/X11 teaching tools/);
  assert.doesNotMatch(control,/not yet enabled/);
  for(const pkg of ['pulseaudio-utils','xdg-utils','libxcb-xinput0','libx264-164','libicu74','libpipewire-0.3-0t64','libwayland-egl1'])
    assert.ok(control.includes(pkg),pkg);
  const desktop=await readFile(join(f.stageRoot,'usr/share/applications/opencam-beta.desktop'),'utf8');
  assert.match(desktop,/^Exec=\/usr\/bin\/opencam-beta$/m); assert.match(desktop,/^Terminal=false$/m);
  assert.match(desktop,/^Name=OpenCam$/m); assert.doesNotMatch(desktop,/^Name=.*Beta/m); assert.doesNotMatch(desktop,/\.sh/);
  assert.equal(await readFile(join(f.stageRoot,'opt/opencam-beta/OpenCam'),'utf8'),'fixture');
  const launcher=await readFile(join(f.stageRoot,'usr/bin/opencam-beta'),'utf8');
  assert.match(launcher,/\/opt\/opencam-beta\/Start-OpenCam\.sh/); assert.doesNotMatch(launcher,/apt |sudo|rm /);
  await assert.rejects(access(join(f.stageRoot,'DEBIAN/postrm')));
});
for (const missing of ['OpenCam','obs-runtime/bin/OpenCam.ObsCapture','LICENSE','SOURCE.txt','app-icon.png',
  'linux-notices/COPYING.GPLv3','linux-notices/project-licenses/Avalonia-LICENSE.md',
  'linux-notices/obs-embedded/SIMDe-copyright.txt','obs-runtime/runtime.json'])
  test(`deb refuses missing ${missing} before creating install tree`,async t => {
    const f=await fixture(t); await rm(join(f.appRoot,missing));
    await assert.rejects(() => builder().then(fn => fn(f)),/missing|incomplete/i);
    await assert.rejects(access(f.stageRoot));
  });
test('deb rejects symlinks before copying files into root-owned install paths',async t => {
  const f=await fixture(t); await symlink('/etc/passwd',join(f.appRoot,'escape'));
  await assert.rejects(() => builder().then(fn => fn(f)),/symlink|regular/i);
  await assert.rejects(access(f.stageRoot));
});
test('deb rejects mismatched source identity and preserves existing destination',async t => {
  const f=await fixture(t);
  await assert.rejects(() => builder().then(fn => fn({...f,revision:'a'.repeat(40)})),/identity|revision/i);
  await mkdir(f.stageRoot); await writeFile(join(f.stageRoot,'keep'),'user');
  await assert.rejects(() => builder().then(fn => fn(f)),/exist/i);
  assert.equal(await readFile(join(f.stageRoot,'keep'),'utf8'),'user');
});
test('deb refuses changed native bytes even when a runtime inventory is present',async t=>{
  const f=await fixture(t); await writeFile(join(f.appRoot,'obs-runtime/bin/OpenCam.ObsCapture'),'changed');
  await assert.rejects(()=>builder().then(fn=>fn(f)),/runtime|hash/i);
  await assert.rejects(access(f.stageRoot));
});
