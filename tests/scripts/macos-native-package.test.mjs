// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, rm, copyFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const script=fileURLToPath(new URL('../../scripts/package-macos.sh',import.meta.url));
const verifier=fileURLToPath(new URL('../../scripts/verify-macos-bundle.sh',import.meta.url));
test('native-only app packaging refuses missing runtime before any output mutation',async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-mac-package-test-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const result=spawnSync('bash',[script,root,join(root,'OpenCam.app'),'Release','--native-only-user-test'],{encoding:'utf8'});
  assert.notEqual(result.status,0);assert.match(result.stderr,/native-only runtime stage is required/);
});
test('native-only app packaging never overwrites an existing app',async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-mac-package-test-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const app=join(root,'OpenCam.app');await mkdir(app);await writeFile(join(app,'sentinel'),'preserve');
  const result=spawnSync('bash',[script,root,app,'Release','--native-only-user-test',root],{encoding:'utf8'});
  assert.notEqual(result.status,0);assert.match(result.stderr,/native-only output already exists/);
  assert.equal(await readFile(join(app,'sentinel'),'utf8'),'preserve');
});
test('Mac bundle verification rejects missing execute permission on the sole recording helper',{
  skip:process.platform!=='darwin',
},async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-mac-permission-test-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const app=join(root,'OpenCam.app'),mac=join(app,'Contents/MacOS'),res=join(app,'Contents/Resources');
  await mkdir(join(mac,'obs-runtime/Contents/MacOS'),{recursive:true});await mkdir(res,{recursive:true});
  await mkdir(join(mac,'TeachingPointers'));
  for(const name of ['OpenCam','ffmpeg','ffprobe','OpenCam.SystemAudio','OpenCam.Microphone','OpenCam.CursorOverlay'])
    await writeFile(join(mac,name),'fixture',{mode:0o755});
  for(const name of ['WarmIvory','TealSilver','RoundShort','HighContrast'])await writeFile(join(mac,'TeachingPointers',name+'.png'),'fixture');
  await writeFile(join(mac,'PresentationPointer.png'),'fixture');
  await writeFile(join(mac,'obs-runtime/Contents/MacOS/OpenCam.ObsCapture'),'fixture',{mode:0o644});
  await copyFile(fileURLToPath(new URL('../../LICENSE',import.meta.url)),join(res,'LICENSE'));
  for(const name of ['NOTICE.md','OpenCam.icns'])await writeFile(join(res,name),'fixture');
  await writeFile(join(res,'SOURCE.txt'),'SPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/'+'a'.repeat(40));
  await writeFile(join(app,'Contents/Info.plist'),'<?xml version="1.0"?><plist version="1.0"><dict><key>CFBundleIconFile</key><string>OpenCam.icns</string><key>LSMinimumSystemVersion</key><string>13.0</string><key>OpenCamNativeOnlyTest</key><true/></dict></plist>');
  const result=spawnSync('bash',[verifier,app],{encoding:'utf8'});
  assert.notEqual(result.status,0);assert.match(result.stderr,/native-only recording helper is not executable/);
});
