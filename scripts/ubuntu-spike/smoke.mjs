// SPDX-License-Identifier: AGPL-3.0-or-later
// Executes only inside the disposable Ubuntu/Xvfb integration-test session.
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';

const [packageArg, evidenceArg] = process.argv.slice(2);
if (!packageArg || !evidenceArg || process.platform !== 'linux' || !process.env.DISPLAY)
  throw Error('Expected Ubuntu/Xvfb package and evidence directories');
const root = resolve(packageArg), evidence = resolve(evidenceArg);
await mkdir(evidence, { recursive: true });
const stdout = [], stderr = [];
const child = spawn(join(root, 'Start-OpenCam.sh'), ['--lang', 'en'], {
  cwd: root,
  env: { ...process.env, OPENCAM_UBUNTU_TEST_DATA: join(evidence, 'test-data') },
  stdio: ['ignore', 'pipe', 'pipe']
});
child.stdout.on('data', data => stdout.push(data));
child.stderr.on('data', data => stderr.push(data));
let exit = null, launchError = null;
child.on('error', error => { launchError = error.message; });
const closed = new Promise(resolve => child.on('close', (code, signal) => { exit = { code, signal }; resolve(); }));
let visible = false, tree = '', screenshotExit = null, aliveBeforeCleanup = false;
const start = Date.now();
try {
  while (!exit && !launchError && Date.now() - start < 20000) {
    const result = spawnSync('xwininfo', ['-root', '-tree'], { encoding: 'utf8' });
    tree = result.stdout || result.stderr || '';
    if (/"OpenCam[^"\n]*"/.test(tree)) { visible = true; break; }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (visible) await new Promise(resolve => setTimeout(resolve, 5000));
  aliveBeforeCleanup = !exit && !launchError;
  const finalTree = spawnSync('xwininfo', ['-root', '-tree'], { encoding: 'utf8' });
  tree = finalTree.stdout || finalTree.stderr || tree;
  visible = visible && /"OpenCam[^"\n]*"/.test(tree);
  screenshotExit = spawnSync('scrot', [join(evidence, 'normal-startup.png')]).status;
} finally {
  if (!exit && !launchError) {
    child.kill('SIGTERM');
    const stopped = await Promise.race([closed.then(() => true), new Promise(resolve => setTimeout(() => resolve(false), 3000))]);
    if (!stopped) { child.kill('SIGKILL'); await closed; }
  } else if (!exit && launchError) await closed;
}
await writeFile(join(evidence, 'normal-window-tree.txt'), tree);
await writeFile(join(evidence, 'normal-stdout.txt'), Buffer.concat(stdout));
await writeFile(join(evidence, 'normal-stderr.txt'), Buffer.concat(stderr));
const report = {
  kind: 'Normal app startup, NOT screenshot-mode initialization',
  session: 'Ubuntu 24.04 x64, Xvfb X11 virtual display, no physical capture/audio',
  windowObserved: visible,
  processAliveBeforeTestCleanup: aliveBeforeCleanup,
  applicationStatus: visible && aliveBeforeCleanup ? 'WINDOW_OPENED' : 'STARTUP_FAILED',
  launchError, exitAfterCleanup: exit, screenshotExit,
  recordingStatus: 'NOT_SUPPORTED',
  editingAndExportStatus: 'NOT_VALIDATED',
  physicalRecordingAcceptance: 'REQUIRES MANUAL VALIDATION AFTER LINUX IMPLEMENTATION'
};
await writeFile(join(evidence, 'normal-startup.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
// Both startup outcomes are experiment results. Infrastructure failures are not.
if (launchError || screenshotExit !== 0) process.exitCode = 1;
