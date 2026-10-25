// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';import assert from 'node:assert/strict';
import {mkdtemp,mkdir,writeFile,readFile,rm,copyFile,stat} from 'node:fs/promises';
import {tmpdir} from 'node:os';import {join} from 'node:path';import {execFileSync} from 'node:child_process';
import {signMacNativeOnlyApp} from '../../scripts/sign-macos-native-only.mjs';
test('Mac signing preserves private runtime bytes and keeps non-code files in Resources',{
  skip:process.platform!=='darwin'||process.arch!=='arm64',
},async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-sign-test-'));t.after(()=>rm(root,{recursive:true,force:true}));
  const app=join(root,'OpenCam.app'),mac=join(app,'Contents/MacOS'),res=join(app,'Contents/Resources');
  await mkdir(join(mac,'obs-runtime/Contents/MacOS'),{recursive:true});await mkdir(res,{recursive:true});
  await writeFile(join(root,'main.c'),'int main(void){return 0;}');
  execFileSync('xcrun',['clang','-target','arm64-apple-macos13.0',join(root,'main.c'),'-o',join(mac,'OpenCam')]);
  const helper='obs-runtime/Contents/MacOS/OpenCam.ObsCapture';await copyFile(join(mac,'OpenCam'),join(mac,helper));
  const original=await readFile(join(mac,helper));
  await writeFile(join(mac,'obs-runtime/runtime.json'),'{"enabled":true}');
  await writeFile(join(mac,'OBS-TEST-INFO.json'),'{"testOnly":true}');
  await writeFile(join(mac,'PresentationPointer.png'),'resource');
  await mkdir(join(mac,'TeachingPointers'));await writeFile(join(mac,'TeachingPointers/TealSilver.png'),'resource');
  await writeFile(join(app,'Contents/Info.plist'),'<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleExecutable</key><string>OpenCam</string><key>CFBundleIdentifier</key><string>org.opencam.sign-test</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>');
  await signMacNativeOnlyApp(app);
  execFileSync('codesign',['--verify','--deep','--strict',app]);
  assert.deepEqual(await readFile(join(res,helper)),original);
  assert.ok((await stat(join(res,helper))).mode&0o111);
  assert.equal(await readFile(join(res,'PresentationPointer.png'),'utf8'),'resource');
  assert.equal(await readFile(join(res,'TeachingPointers/TealSilver.png'),'utf8'),'resource');
  await assert.rejects(stat(join(mac,'obs-runtime')),{code:'ENOENT'});
});
