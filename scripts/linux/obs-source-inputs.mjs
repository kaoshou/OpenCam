// SPDX-License-Identifier: AGPL-3.0-or-later
import { access, lstat, realpath } from 'node:fs/promises';
import { resolve, dirname, relative, isAbsolute, sep } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';

const run = promisify(execFile);
export const obsRevision = '72e86655715d9a25d69ce446aa4dd44ba58e10d2';
const overlaps = (a, b) => [relative(a, b), relative(b, a)].some(r => r === '' || (!isAbsolute(r) && r !== '..' && !r.startsWith(`..${sep}`)));

async function absent(path) {
  try { await lstat(path); } catch (error) { if (error.code === 'ENOENT') return; throw error; }
  throw new Error('Output already exists; choose a new isolated directory');
}
async function canonicalOutput(path) {
  if (!path || !isAbsolute(path)) throw new Error('Absolute output paths are required');
  path = resolve(path);
  await absent(path);
  // Existing parents only. Do not mkdir or follow an unresolved nested output chain.
  const parent = dirname(path);
  const actualParent = await realpath(parent);
  if (actualParent !== parent) throw new Error('Symbolic output parent is not allowed; use its canonical path');
  return path;
}
export async function verifyTrackedCheckout(sourceRoot, revision) {
  if (!/^[0-9a-f]{40}$/.test(revision)) throw new Error('Invalid source revision');
  const actual = await realpath(sourceRoot);
  const git = async (...args) => (await run('git', ['-C', actual, ...args], { maxBuffer: 4 * 1024 * 1024 })).stdout.trim();
  if (await realpath(await git('rev-parse', '--show-toplevel')) !== actual) throw new Error('Expected checkout root');
  if (await git('rev-parse', 'HEAD') !== revision) throw new Error('Expected exact pinned source revision');
  if (await git('status', '--porcelain', '--untracked-files=all')) throw new Error('Source must be clean: modified or untracked files found');
  return revision;
}
async function validateSourceInputs({ sourceRoot, stageRoot, evidenceRoot, buildRoot }, revision) {
  if (!sourceRoot || !isAbsolute(sourceRoot)) throw new Error('Absolute source root is required');
  if (!stageRoot || !evidenceRoot || !isAbsolute(stageRoot) || !isAbsolute(evidenceRoot))
    throw new Error('Absolute output paths are required');
  if (overlaps(resolve(sourceRoot), resolve(stageRoot)) || overlaps(resolve(sourceRoot), resolve(evidenceRoot)) ||
      overlaps(resolve(stageRoot), resolve(evidenceRoot)))
    throw new Error('Source, stage and evidence must use separate non-overlapping directories');
  const source = await realpath(sourceRoot);
  if (buildRoot) {
    if (!isAbsolute(buildRoot)) throw new Error('Absolute build path is required');
    if ([source, resolve(stageRoot), resolve(evidenceRoot)].some(p => overlaps(p, resolve(buildRoot))))
      throw new Error('Build must use a separate non-overlapping directory');
    await canonicalOutput(buildRoot);
  }
  const stage = await canonicalOutput(stageRoot);
  const evidence = await canonicalOutput(evidenceRoot);
  if (overlaps(source, stage) || overlaps(source, evidence) || overlaps(stage, evidence))
    throw new Error('Source, stage and evidence must use separate non-overlapping directories');
  await verifyTrackedCheckout(source, revision);
}
export async function validateLinuxObsInputs(input) {
  await validateSourceInputs(input, obsRevision);
  await access(resolve(input.sourceRoot, 'CMakeLists.txt'));
  return { revision: obsRevision, obsVersion: '33.0.0', platform: 'linux-x64' };
}
export async function validateLinuxFfmpegInputs(input) {
  const revision = '1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7';
  await validateSourceInputs(input, revision);
  await access(resolve(input.sourceRoot, 'configure'));
  return { revision, ffmpegVersion: 'n8.1.3', platform: 'linux-x64' };
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    if (process.argv.length < 5 || process.argv.length > 6) throw new Error('Usage: obs-source-inputs.mjs ABS_SOURCE ABS_NEW_STAGE ABS_NEW_EVIDENCE [ABS_NEW_BUILD]');
    const result = await validateLinuxObsInputs({ sourceRoot: process.argv[2], stageRoot: process.argv[3], evidenceRoot: process.argv[4], buildRoot: process.argv[5] });
    process.stdout.write(JSON.stringify(result) + '\n');
  } catch (error) { process.stderr.write(`Linux input validation failed: ${error.message}\n`); process.exitCode = 2; }
}
