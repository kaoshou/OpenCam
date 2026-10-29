// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, symlink, access, rm, realpath } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

async function fixture(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'opencam-linux-inputs-')));
  t.after(() => rm(root, { recursive: true, force: true }));
  const sourceRoot = join(root, 'source'); await mkdir(sourceRoot);
  const git = (...args) => execFileSync('git', ['-C', sourceRoot, ...args], { encoding: 'utf8' }).trim();
  git('init', '-q'); git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid');
  await writeFile(join(sourceRoot, 'CMakeLists.txt'), 'fixture\n'); git('add', '.'); git('commit', '-qm', 'fixture');
  return { root, sourceRoot, stageRoot: join(root, 'stage'), evidenceRoot: join(root, 'evidence'), git, revision: git('rev-parse', 'HEAD') };
}
const api = () => import('../../scripts/linux/obs-source-inputs.mjs');

test('rejectsWrongRevision without creating output', async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  await assert.rejects(validateLinuxObsInputs(f), /revision/i);
  await assert.rejects(access(f.stageRoot)); await assert.rejects(access(f.evidenceRoot));
});
test('rejectsDirtyTrackedSources', async t => {
  const f = await fixture(t); const { verifyTrackedCheckout } = await api();
  await writeFile(join(f.sourceRoot, 'CMakeLists.txt'), 'changed\n');
  await assert.rejects(verifyTrackedCheckout(f.sourceRoot, f.revision), /clean|modified/i);
});
test('rejectsUntrackedBuildInstructions', async t => {
  const f = await fixture(t); const { verifyTrackedCheckout } = await api();
  await writeFile(join(f.sourceRoot, 'injected.cmake'), 'execute_process(COMMAND bad)');
  await assert.rejects(verifyTrackedCheckout(f.sourceRoot, f.revision), /clean|untracked/i);
});
test('acceptsPinnedCleanInputs in the checkout verifier', async t => {
  const f = await fixture(t); const { verifyTrackedCheckout } = await api();
  assert.equal(await verifyTrackedCheckout(f.sourceRoot, f.revision), f.revision);
});
test('rejectsExistingStage', async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  await mkdir(f.stageRoot); await writeFile(join(f.stageRoot, 'keep.txt'), 'keep');
  await assert.rejects(validateLinuxObsInputs(f), /exist/i);
  assert.equal(execFileSync('git', ['-C', f.sourceRoot, 'status', '--porcelain'], { encoding: 'utf8' }), '');
});
test('rejectsStageInsideSource', async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  await assert.rejects(validateLinuxObsInputs({ ...f, stageRoot: join(f.sourceRoot, 'build-output') }), /separate|overlap/i);
});
test('rejectsSymlinkEscape', async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  await symlink(f.sourceRoot, join(f.root, 'alias'));
  await assert.rejects(validateLinuxObsInputs({ ...f, stageRoot: join(f.root, 'alias', 'stage') }), /separate|overlap|symbolic/i);
});
test('rejectsNestedCheckoutAndEvidenceInsideStage', async t => {
  const f = await fixture(t); const { verifyTrackedCheckout, validateLinuxObsInputs } = await api();
  const child = join(f.sourceRoot, 'child'); await mkdir(child);
  await assert.rejects(verifyTrackedCheckout(child, f.revision), /root/i);
  await assert.rejects(validateLinuxObsInputs({ ...f, evidenceRoot: join(f.stageRoot, 'evidence') }), /separate|overlap/i);
});
test('rejectsOverlappingBuildAndStage', async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  await assert.rejects(validateLinuxObsInputs({ ...f, buildRoot: f.sourceRoot }), /separate|overlap/i);
});
test('accepts exact downloaded OBS checkout without creating outputs', { skip: !process.env.OPENCAM_LINUX_OBS_SOURCE }, async t => {
  const f = await fixture(t); const { validateLinuxObsInputs } = await api();
  assert.deepEqual(await validateLinuxObsInputs({ ...f, sourceRoot: process.env.OPENCAM_LINUX_OBS_SOURCE }), {
    revision: '72e86655715d9a25d69ce446aa4dd44ba58e10d2', obsVersion: '33.0.0', platform: 'linux-x64',
  });
  await assert.rejects(access(f.stageRoot));
});
test('rejectsWrongFFmpegRevision without creating a prefix', async t => {
  const f = await fixture(t); const { validateLinuxFfmpegInputs } = await api();
  await assert.rejects(validateLinuxFfmpegInputs(f), /revision/i);
  await assert.rejects(access(f.stageRoot));
});
test('Linux builder refuses a macOS host before creating outputs', { skip: process.platform !== 'darwin' }, async t => {
  const f = await fixture(t);
  const result = spawnSync('bash', ['scripts/linux/build-obs-probe.sh', f.sourceRoot, join(f.root, 'build'), f.stageRoot, f.evidenceRoot], { encoding: 'utf8' });
  assert.equal(result.status, 2); assert.match(result.stderr, /Linux x86_64/);
  await assert.rejects(access(f.stageRoot));
});
