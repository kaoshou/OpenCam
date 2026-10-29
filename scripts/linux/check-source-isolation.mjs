// SPDX-License-Identifier: AGPL-3.0-or-later
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
const run = promisify(execFile);
const protectedPaths = ['src', 'native/obs-capture', 'scripts/build-obs-capture.sh', 'scripts/build-obs-capture.ps1',
  'scripts/assemble-obs-runtime.mjs', '.github/workflows/build-and-release.yml', '.github/workflows/package-release.yml', 'VERSION'];
export async function assertProductionIsolation(repoRoot, baseline) {
  if (!/^[a-f0-9]{40}$/.test(baseline)) throw new Error('Expected exact baseline commit');
  const git = async (...args) => (await run('git', ['-C', repoRoot, ...args], { maxBuffer: 4 * 1024 * 1024 })).stdout;
  await git('rev-parse', '--verify', `${baseline}^{commit}`);
  const changed = await git('diff', '--name-only', baseline, '--', ...protectedPaths);
  const untracked = await git('ls-files', '--others', '--exclude-standard', '--', ...protectedPaths);
  if (changed.trim() || untracked.trim()) throw new Error('Protected production paths changed during Linux core feasibility');
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    await assertProductionIsolation(process.argv[2] ?? process.cwd(), 'e0c369e6495532cc0393c349dc428ffb8a00327e');
    console.log(JSON.stringify({ protectedProductionPaths: 'UNCHANGED', includesUntrackedCheck: true }));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
