import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, cp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { join, resolve } from 'node:path';
import { verifyReleaseNotices, verifyThirdPartyNotices } from '../../scripts/check-release-notices.mjs';
import { assembleThirdPartyNotices } from '../../scripts/assemble-third-party-notices.mjs';
import { addThirdPartyFixture } from './helpers/notices.mjs';

const root = resolve(import.meta.dirname, '../..');
const revision = '0123456789abcdef0123456789abcdef01234567';

test('Windows Git checkout preserves every catalogued license hash', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'opencam-notice-checkout-'));
  try {
    await cp(join(root, '.gitattributes'), join(dir, '.gitattributes'));
    await cp(join(root, 'third-party'), join(dir, 'third-party'), { recursive: true });
    const git = args => execFileSync('git', args, { cwd: dir });
    git(['init', '--quiet']);
    git(['-c', 'core.autocrlf=false', 'add', '.gitattributes', 'third-party']);
    const catalog = JSON.parse(await readFile(join(dir, 'third-party/catalog.json'), 'utf8'));
    for (const file of catalog.files) {
      const data = git(['-c', 'core.autocrlf=true', 'cat-file', '--filters', `:third-party/${file.path}`]);
      assert.equal(createHash('sha256').update(data).digest('hex'), file.sha256, file.path);
    }
  } finally { await rm(dir, { recursive: true, force: true }); }
});

test('release rejects missing third-party notices even when project notices are valid', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'opencam-third-party-'));
  try {
    await cp(join(root, 'LICENSE'), join(dir, 'LICENSE'));
    await cp(join(root, 'NOTICE.md'), join(dir, 'NOTICE.md'));
    await writeFile(join(dir, 'SOURCE.txt'), `OpenCam 0.2.3\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/${revision}`);
    await assert.rejects(verifyReleaseNotices(dir, revision, '0.2.3'), /third.party/i);
  } finally { await rm(dir, { recursive: true, force: true }); }
});

for (const rid of ['win-x64', 'osx-arm64']) {
  test(`${rid} assembled payload preserves upstream notices and rejects missing, changed and escaping files`, async () => {
    const dir = await mkdtemp(join(tmpdir(), 'opencam-notice-payload-'));
    try {
      await addThirdPartyFixture(dir, rid);
      await verifyThirdPartyNotices(dir);
      assert.deepEqual(await readFile(join(dir, 'third-party/licenses/Inter-LICENSE.txt')),
        await readFile(join(root, 'third-party/licenses/Inter-LICENSE.txt')));
      const file = join(dir, 'third-party/runtime/LICENSE.TXT');
      const original = await readFile(file);
      await writeFile(file, 'changed');
      await assert.rejects(verifyThirdPartyNotices(dir), /Changed notice/);
      await writeFile(file, original);
      await rm(file);
      await assert.rejects(verifyThirdPartyNotices(dir), /third-party/);
      await writeFile(file, original);
      const manifestPath = join(dir, 'third-party/manifest.json');
      const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
      manifest.files[0].path = '../outside';
      await writeFile(manifestPath, JSON.stringify(manifest));
      await assert.rejects(verifyThirdPartyNotices(dir), /Unsafe third-party notice path/);
    } finally { await rm(dir, { recursive: true, force: true }); }
  });
}

test('collector fails closed when a runtime dependency changes without license review', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'opencam-unreviewed-notice-'));
  try {
    const options = await addThirdPartyFixture(dir);
    const assets = JSON.parse(await readFile(options.assetsPath, 'utf8'));
    assets.targets['net8.0/osx-arm64']['New.Dependency/1.0.0'] = { type: 'package', runtime: { 'new.dll': {} } };
    await writeFile(options.assetsPath, JSON.stringify(assets));
    await assert.rejects(assembleThirdPartyNotices(dir, { rid: 'osx-arm64', ...options }), /Unreviewed third-party dependency/);
  } finally { await rm(dir, { recursive: true, force: true }); }
});
