// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const script=fileURLToPath(new URL('../../scripts/package-macos.sh',import.meta.url));
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
