#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { arch, cpus, platform, release } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const tools = {
  dotnet: { command: 'dotnet', args: ['--info'] },
  ffmpeg: { command: 'ffmpeg', args: ['-version'] },
  ffprobe: { command: 'ffprobe', args: ['-version'] },
};
const runnerFields = ['GITHUB_SHA', 'RUNNER_OS', 'RUNNER_ARCH', 'ImageOS', 'ImageVersion'];

// Only known version commands are used by the CLI. No shell, environment dump,
// process arguments, user media, or native audio is included in this evidence.
export function captureTool(tool, { timeoutMs = 10000, maxOutputBytes = 16384 } = {}) {
  const result = spawnSync(tool.command, tool.args, {
    timeout: timeoutMs,
    maxBuffer: maxOutputBytes,
    killSignal: 'SIGKILL',
    windowsHide: true,
  });
  const stdout = result.stdout ?? Buffer.alloc(0);
  const stderr = result.stderr ?? Buffer.alloc(0);
  return {
    status: result.status,
    signal: result.signal,
    errorCode: result.error?.code ?? null,
    stdout: stdout.subarray(0, maxOutputBytes).toString('utf8'),
    stderr: stderr.subarray(0, maxOutputBytes).toString('utf8'),
    truncated: stdout.length > maxOutputBytes || stderr.length > maxOutputBytes || result.error?.code === 'ENOBUFS',
  };
}

export function collectTestEnvironment({ env = process.env, tools: versionTools = tools } = {}) {
  const results = Object.fromEntries(Object.entries(versionTools).map(([name, tool]) => [name, captureTool(tool)]));
  return {
    schemaVersion: 1,
    capturedAtUtc: new Date().toISOString(),
    runner: Object.fromEntries(runnerFields.map((key) => [key, (env[key] ?? '').slice(0, 256)])),
    host: { platform: platform(), release: release(), arch: arch(), cpuCount: cpus().length, node: process.version },
    restoreCommand: 'dotnet restore ScreenRecorder.sln',
    testCommand: 'dotnet test ScreenRecorder.sln -c Release --no-restore -m:1 /nodeReuse:false --logger trx --results-directory test-results',
    tools: results,
    success: Object.values(results).every((result) => result.status === 0 && result.errorCode === null),
  };
}

export async function writeTestEnvironment(destination, options) {
  const report = collectTestEnvironment(options);
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, JSON.stringify(report, null, 2) + '\n', 'utf8');
  return report;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const report = await writeTestEnvironment(resolve(process.argv[2] ?? 'test-results/environment.json'));
    console.log('Saved bounded tool versions and allowlisted runner metadata.');
    if (!report.success) {
      for (const [name, result] of Object.entries(report.tools)) {
        if (result.status !== 0 || result.errorCode) console.error(`${name}: status=${result.status}, error=${result.errorCode}`);
      }
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(`Could not save test environment evidence: ${error.message}`);
    process.exitCode = 1;
  }
}
