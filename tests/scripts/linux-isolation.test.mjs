// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
const api = () => import('../../scripts/linux/check-source-isolation.mjs');
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'linux-isolation-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', ['-C', root, ...args], { encoding: 'utf8' }).trim();
  git('init', '-q'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid');
  for (const dir of ['src', 'native/obs-capture', 'scripts']) await mkdir(join(root, dir), { recursive: true });
  await writeFile(join(root, 'src', 'Windows.cs'), 'baseline');
  await writeFile(join(root, 'native/obs-capture/main.cpp'), 'baseline');
  await writeFile(join(root, 'scripts/build-obs-capture.sh'), 'baseline');
  git('add', '.'); git('commit', '-qm', 'baseline');
  return { root, git, baseline: git('rev-parse', 'HEAD') };
}
test('permits Linux-only additions without touching protected production files', async t => {
  const f = await fixture(t); const { assertProductionIsolation } = await api();
  await mkdir(join(f.root, 'native/obs-linux-probe')); await writeFile(join(f.root, 'native/obs-linux-probe/main.cpp'), 'new Linux code');
  await assertProductionIsolation(f.root, f.baseline);
});
for (const [name, file, staged] of [
  ['detects tracked Windows/macOS changes', 'src/Windows.cs', false],
  ['detects staged original native core changes', 'native/obs-capture/main.cpp', true],
  ['detects existing platform builder changes', 'scripts/build-obs-capture.sh', false],
  ['detects new untracked product code', 'src/Linux.cs', false],
]) test(name, async t => {
  const f = await fixture(t); const { assertProductionIsolation } = await api();
  await writeFile(join(f.root, file), 'changed'); if (staged) f.git('add', file);
  await assert.rejects(assertProductionIsolation(f.root, f.baseline), /protected|production/i);
});
test('invalid baseline never reports a clean audit', async t => {
  const f = await fixture(t); const { assertProductionIsolation } = await api();
  await assert.rejects(assertProductionIsolation(f.root, '0'.repeat(40)));
});
