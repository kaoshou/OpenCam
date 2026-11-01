// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { checkReleaseSources } from '../../scripts/check-release-source-provision.mjs';
import { mkdtemp, writeFile, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

test('current release has its own source-delivery review policy before packaging', async()=>{
  const version=(await readFile(new URL('../../VERSION',import.meta.url),'utf8')).trim();
  const policy=JSON.parse(await readFile(new URL(`../../docs/releases/v${version}-source-coverage.json`,import.meta.url),'utf8'));
  assert.equal(policy.version,version);
  assert.equal(policy.status,'policy-approved');
  assert.ok(policy.reviewedBy);
  assert.equal(policy.deliveryManifest,'SOURCE-COVERAGE.json');
  assert.ok(policy.checks.length>0);
});

test('pending source coverage blocks binary publication instead of silently accepting provenance links', async()=>{
  await assert.rejects(checkReleaseSources({status:'pending'}), /source.*pending/i);
});

test('reviewed coverage still requires unchanged delivered archives for every platform scope', async t=>{
  const root=await mkdtemp(join(tmpdir(),'opencam-source-gate-'));
  t.after(()=>rm(root,{recursive:true,force:true}));
  const bytes=Buffer.from('test archive bytes'),name='sources.tar.gz';
  await writeFile(join(root,name),bytes);
  const version=(await readFile(new URL('../../VERSION',import.meta.url),'utf8')).trim();
  const archive={name,sha256:createHash('sha256').update(bytes).digest('hex'),
    covers:['obs-windows','obs-macos','ffmpeg-tools-windows','ffmpeg-tools-macos']};
  const coverage={version,status:'reviewed',reviewedBy:'fixture',archives:[archive]};
  await checkReleaseSources(coverage,root);
  await assert.rejects(checkReleaseSources({...coverage,archives:[{...archive,covers:['obs-windows']}]},root),/Incomplete/);
  await assert.rejects(checkReleaseSources({...coverage,archives:[{...archive,name:'../outside'}]},root),/Invalid/);
  await writeFile(join(root,name),'changed bytes');
  await assert.rejects(checkReleaseSources(coverage,root),/Changed/);
});
