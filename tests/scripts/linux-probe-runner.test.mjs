// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, realpath, copyFile, chmod, rm, readFile, mkdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
const api = () => import('../../scripts/linux/run-capture-probe.mjs');
async function fixture(t, behavior = 'ignore') {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'linux-probe-runner-')));
  t.after(() => rm(root, { recursive: true, force: true }));
  const helper = join(root, 'fixture-child');
  await copyFile(new URL('../fixtures/linux-probe-child.mjs', import.meta.url), helper + '.mjs');
  await chmod(helper + '.mjs', 0o755);
  return { helper: helper + '.mjs', stage: root, evidenceDirectory: join(root, 'evidence'), seconds: .03,
    startupTimeoutMs: 2000, stopTimeoutMs: 100, request: { backend: 'x11', fixtureBehavior: behavior, pidFile: join(root, 'pid') } };
}
function assertGone(pid) { assert.throws(() => process.kill(pid, 0), { code: 'ESRCH' }); }
test('cancelReapsOwnedChild and retains cancellation reason', async t => {
  const f = await fixture(t); const { runCaptureProbe } = await api();
  const controller = new AbortController();
  t.after(() => controller.abort());
  const pending = runCaptureProbe({ ...f, seconds: 5, signal: controller.signal });
  // Cancel an actual running child, not one whose executable has yet to start.
  const deadline = Date.now() + 1800;
  for (;;) {
    try { await readFile(f.request.pidFile); break; }
    catch (error) { if (error.code !== 'ENOENT' || Date.now() > deadline) throw error; }
    await new Promise(resolve => setTimeout(resolve, 10));
  }
  controller.abort();
  const result = await pending;
  assert.equal(result.status, 'FAIL'); assert.equal(result.reasons[0], 'cancelled');
  assertGone(Number(await readFile(f.request.pidFile, 'utf8')));
});
test('firstFailureSurvivesCleanupFailure', async t => {
  const f = await fixture(t, 'error'); const { runCaptureProbe } = await api();
  const result = await runCaptureProbe(f);
  assert.equal(result.status, 'FAIL'); assert.equal(result.reasons[0], 'audio_lost');
  assertGone(Number(await readFile(f.request.pidFile, 'utf8')));
});
test('oversized protocol terminates child rather than growing memory', async t => {
  const f = await fixture(t, 'oversized'); const { runCaptureProbe } = await api();
  const result = await runCaptureProbe(f); assert.equal(result.status, 'FAIL');
  assert.equal(result.reasons[0], 'invalid_protocol'); assertGone(Number(await readFile(f.request.pidFile, 'utf8')));
});
test('never overwrites an existing evidence directory', async t => {
  const f = await fixture(t); const { runCaptureProbe } = await api(); await mkdir(f.evidenceDirectory);
  await assert.rejects(runCaptureProbe(f), /exist/i);
});
test('missing executable yields bounded diagnostic failure rather than a hang', async t => {
  const f = await fixture(t); const { runCaptureProbe } = await api();
  const result = await runCaptureProbe({ ...f, helper: join(f.stage, 'missing') });
  assert.equal(result.status, 'FAIL'); assert.equal(result.reasons[0], 'spawn_failed');
});
