import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';

const moduleUrl = new URL('../../scripts/collect-obs-corresponding-sources.mjs', import.meta.url);
async function api() { return import(moduleUrl); }
async function fixture(t) {
  const dir = await mkdtemp(join(tmpdir(), 'obs-source-test-'));
  t.after(() => rm(dir, {recursive:true, force:true}));
  const repo = join(dir,'repo'); await mkdir(repo);
  const git = (...args) => execFileSync('git',['-C',repo,...args],{encoding:'utf8'}).trim();
  git('init','-q'); git('config','user.name','Fixture'); git('config','user.email','fixture@example.invalid');
  await writeFile(join(repo,'COPYING'),'Fixture source license\n');
  await writeFile(join(repo,'.gitattributes'),'COPYING export-ignore\n');
  git('add','.'); git('commit','-qm','fixture');
  const revision=git('rev-parse','HEAD');
  return {dir,repo,git,lock:{schema:1,sources:[{id:'fixture',url:'https://example.invalid/source.git',revision}],archives:[]}};
}

test('bundles exact tracked source bytes even when export-ignore would omit them', async t => {
  const f=await fixture(t), {collectSources}=await api();
  const out=join(f.dir,'out');
  const report=await collectSources(out,{lock:f.lock,sourceDirectories:{fixture:f.repo}});
  assert.equal(report.sources[0].revision,f.lock.sources[0].revision);
  const bundle=await readFile(join(out,'OBS-SOURCES.tar.gz'));
  assert.equal(createHash('sha256').update(bundle).digest('hex'),report.bundle.sha256);
  const listing=execFileSync('tar',['-tzf',join(out,'OBS-SOURCES.tar.gz')],{encoding:'utf8'});
  assert.match(listing,/SOURCES.json/);
  const source=join(out,report.files.find(v=>v.path.endsWith('.tar')).path);
  assert.equal(execFileSync('tar',['-xOf',source,'COPYING'],{encoding:'utf8'}),'Fixture source license\n');
  await assert.rejects(collectSources(out,{lock:f.lock}),/exist/i);
});

test('rejects changed tracked source instead of claiming a pinned archive', async t => {
  const f=await fixture(t), {collectSources}=await api();
  await writeFile(join(f.repo,'COPYING'),'modified');
  await assert.rejects(collectSources(join(f.dir,'out'),{lock:f.lock,sourceDirectories:{fixture:f.repo}}),/modified|clean/i);
});

test('rejects corrupted or missing supplied archives', async t => {
  const f=await fixture(t), {collectSources}=await api();
  const archives=join(f.dir,'downloads'); await mkdir(archives);
  f.lock.archives=[{id:'srt',filename:'srt.zip',url:'https://example.invalid/srt.zip',sha256:'0'.repeat(64)}];
  await writeFile(join(archives,'srt.zip'),'corrupt');
  await assert.rejects(collectSources(join(f.dir,'bad'),{lock:f.lock,sourceDirectories:{fixture:f.repo},archiveDirectory:archives}),/checksum/i);
  await rm(join(archives,'srt.zip'));
  await assert.rejects(collectSources(join(f.dir,'missing'),{lock:f.lock,sourceDirectories:{fixture:f.repo},archiveDirectory:archives}),/ENOENT|missing/i);
});

test('rejects unsafe archive names and source identifiers before writing output', async t => {
  const f=await fixture(t), {collectSources}=await api();
  f.lock.archives=[{id:'bad',filename:'../escape.zip',url:'https://example.invalid/a',sha256:'0'.repeat(64)}];
  await assert.rejects(collectSources(join(f.dir,'out'),{lock:f.lock}),/unsafe/i);
  f.lock.archives=[];f.lock.sources[0].id='../escape';
  await assert.rejects(collectSources(join(f.dir,'out'),{lock:f.lock}),/unsafe/i);
});

test('captures recursive gitlinks with an exact restoration map', async t => {
  const f=await fixture(t), {collectSources}=await api();
  f.git('config','-f','.gitmodules','submodule.child.path','vendor/child');
  f.git('config','-f','.gitmodules','submodule.child.url','https://example.invalid/child.git');
  const childRevision=f.lock.sources[0].revision;
  f.git('add','.gitmodules');
  f.git('update-index','--add','--cacheinfo',`160000,${childRevision},vendor/child`);
  f.git('commit','-qm','gitlink');f.lock.sources[0].revision=f.git('rev-parse','HEAD');
  const child=join(f.dir,'child');execFileSync('git',['clone','-q',f.repo,child]);
  execFileSync('git',['-C',child,'checkout','-q',childRevision]);
  const report=await collectSources(join(f.dir,'out'),{lock:f.lock,sourceDirectories:{fixture:f.repo},submoduleDirectories:{[childRevision]:child}});
  assert.equal(report.sources[1].revision,childRevision);
  assert.equal(report.sources[1].restorePath,'vendor/child');
  assert.equal(report.sources[1].parent,'fixture');
});
