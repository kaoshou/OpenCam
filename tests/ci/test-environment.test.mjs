// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Missing diagnostics, swallowed tool failures, unbounded output, or copying the
// whole environment must fail these tests. Run actual child processes, not mocks.
const implementation = new URL('../../scripts/collect-test-environment.mjs', import.meta.url);
const loadCollector = () => import(implementation);
const nodeProbe = (source) => ({ command: process.execPath, args: ['-e', source] });

test('environment evidence records tool results and only allowlisted runner metadata', async () => {
  const { collectTestEnvironment } = await loadCollector();
  const report = collectTestEnvironment({
    env: {
      GITHUB_SHA: '0123456789012345678901234567890123456789',
      RUNNER_OS: 'Windows', RUNNER_ARCH: 'X64',
      ImageOS: 'win25', ImageVersion: '20261016.1',
      GITHUB_TOKEN: 'private-token-must-not-be-copied',
      HOME: '/private/user/location',
    },
    tools: {
      dotnet: nodeProbe('console.log("SDK fixture 8.0");'),
      ffmpeg: nodeProbe('console.log("FFmpeg fixture");'),
      ffprobe: nodeProbe('console.log("ffprobe fixture");'),
    },
  });
  assert.equal(report.runner.GITHUB_SHA, '0123456789012345678901234567890123456789');
  assert.equal(report.runner.ImageVersion, '20261016.1');
  assert.equal(report.tools.dotnet.status, 0);
  assert.match(report.tools.dotnet.stdout, /SDK fixture 8\.0/);
  assert.match(report.tools.ffmpeg.stdout, /FFmpeg fixture/);
  assert.match(report.tools.ffprobe.stdout, /ffprobe fixture/);
  assert.ok(report.host.cpuCount > 0);
  assert.ok(report.host.node.startsWith('v'));
  assert.equal(report.success, true);
  const serialized = JSON.stringify(report);
  assert.doesNotMatch(serialized, /private-token-must-not-be-copied|private\/user\/location|GITHUB_TOKEN/);
});

test('nonzero and missing tools remain explicit failures, including stderr', async () => {
  const { collectTestEnvironment } = await loadCollector();
  const report = collectTestEnvironment({
    env: {},
    tools: {
      broken: nodeProbe('console.error("tool failed"); process.exit(17);'),
      missing: { command: join(tmpdir(), 'opencam-nonexistent-tool-756015'), args: [] },
    },
  });
  assert.equal(report.success, false);
  assert.equal(report.tools.broken.status, 17);
  assert.match(report.tools.broken.stderr, /tool failed/);
  assert.equal(report.tools.missing.errorCode, 'ENOENT');
});

test('hanging or noisy version tools are bounded and cannot be reported as successful', async () => {
  const { captureTool } = await loadCollector();
  const hanging = captureTool(nodeProbe('setInterval(() => {}, 1000);'), { timeoutMs: 300 });
  assert.equal(hanging.errorCode, 'ETIMEDOUT');
  assert.notEqual(hanging.status, 0);
  const noisy = captureTool(nodeProbe('process.stdout.write("x".repeat(1000000));'), { maxOutputBytes: 1024 });
  assert.equal(noisy.errorCode, 'ENOBUFS');
  assert.ok(Buffer.byteLength(noisy.stdout) <= 1024);
  assert.equal(noisy.truncated, true);
});

test('environment JSON is saved even when a required tool fails', async () => {
  const { writeTestEnvironment } = await loadCollector();
  const directory = await mkdtemp(join(tmpdir(), 'opencam-test-evidence-'));
  try {
    const file = join(directory, 'nested', 'environment.json');
    const report = await writeTestEnvironment(file, {
      env: { RUNNER_OS: 'test' },
      tools: { dotnet: nodeProbe('process.exit(23);') },
    });
    assert.equal(report.success, false);
    assert.equal(JSON.parse(await readFile(file, 'utf8')).tools.dotnet.status, 23);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('CLI exits unsuccessfully but leaves readable evidence when prerequisites are missing', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-test-evidence-cli-'));
  try {
    const destination = join(directory, 'environment.json');
    const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => key.toLowerCase() !== 'path'));
    env.PATH = directory;
    const result = spawnSync(process.execPath, [fileURLToPath(implementation), destination], { env, encoding: 'utf8' });
    assert.equal(result.status, 1, result.stderr);
    const report = JSON.parse(await readFile(destination, 'utf8'));
    assert.equal(report.success, false);
    for (const name of ['dotnet', 'ffmpeg', 'ffprobe']) assert.equal(report.tools[name].errorCode, 'ENOENT');
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
