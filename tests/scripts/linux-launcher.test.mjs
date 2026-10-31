// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, symlink, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';

async function fixture(tools, lddOutput = 'all libraries resolved', lddStatus = 0) {
  const root = await mkdtemp(join(tmpdir(), 'opencam-launch-'));
  const bin = join(root, 'tools'); await mkdir(bin);
  await mkdir(join(root, 'obs-runtime/bin'), {recursive: true});
  await writeFile(join(root, 'Start-OpenCam.sh'), await readFile(new URL('../../scripts/linux/Start-OpenCam.sh', import.meta.url)));
  for (const command of ['bash', 'dirname']) await symlink(command === 'bash' ? '/bin/bash' : '/usr/bin/dirname', join(bin, command));
  await writeFile(join(bin, 'uname'), '#!/bin/bash\nif [[ "$1" == -s ]]; then echo Linux; else echo x86_64; fi\n', {mode: 0o755});
  for (const tool of tools) await writeFile(join(bin, tool), `#!/bin/bash\nprintf '%s\\n' '${tool === 'ldd' ? lddOutput : 'fixture'}'\nexit ${tool === 'ldd' ? lddStatus : 0}\n`, {mode: 0o755});
  for (const file of ['OpenCam', 'ffmpeg', 'obs-runtime/bin/OpenCam.ObsCapture', 'OpenCam.PulsePlayback'])
    await writeFile(join(root, file), '#!/bin/bash\necho APP_STARTED\n', {mode: 0o755});
  return {root, run: () => spawnSync('/bin/bash', [join(root, 'Start-OpenCam.sh')], {
    encoding: 'utf8', env: {PATH: bin, DISPLAY: ':fixture', HOME: root}, timeout: 5000})};
}

test('launcher names the missing audio tool and gives the one-command setup entry point', async () => {
  const f = await fixture(['ldd', 'xdg-open']);
  try {
    const r = f.run(); assert.equal(r.status, 2);
    assert.match(r.stderr, /pactl/);
    assert.match(r.stderr, /Install-Dependencies\.sh/);
    assert.doesNotMatch(r.stdout, /APP_STARTED/);
  } finally { await rm(f.root, {recursive: true, force: true}); }
});
test('missing desktop folder handler is detected before starting the app', async () => {
  const f = await fixture(['pactl', 'ldd']);
  try {
    const r = f.run(); assert.equal(r.status, 2);
    assert.match(r.stderr, /xdg-open/);
    assert.doesNotMatch(r.stdout, /APP_STARTED/);
  } finally { await rm(f.root, {recursive: true, force: true}); }
});
test('resolved required libraries allow launching the actual application entry point', async () => {
  const f = await fixture(['pactl', 'ldd', 'xdg-open']);
  try { const r = f.run(); assert.equal(r.status, 0); assert.match(r.stdout, /APP_STARTED/); }
  finally { await rm(f.root, {recursive: true, force: true}); }
});
test('nonzero ldd with missing ABI still produces actionable failure instead of exiting silently', async () => {
  const f = await fixture(['pactl', 'ldd', 'xdg-open'], 'libxcb-xinput.so.0 => not found\nlibc.so.6 => resolved', 1);
  try {
    const r = f.run(); assert.equal(r.status, 2);
    assert.match(r.stderr, /libxcb-xinput\.so\.0/);
    assert.match(r.stderr, /Install-Dependencies\.sh/);
    assert.doesNotMatch(r.stderr, /libc\.so\.6 => resolved/);
    assert.doesNotMatch(r.stdout, /APP_STARTED/);
  } finally { await rm(f.root, {recursive: true, force: true}); }
});
