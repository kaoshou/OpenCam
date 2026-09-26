// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { verifyReleaseNotices } from '../../scripts/check-release-notices.mjs';
import {
  checkVersionConsistency,
  validateVersionText,
} from '../../scripts/check-version-consistency.mjs';
import { productVersion, renderHome } from '../src/content.mjs';

const repositoryRoot = resolve(import.meta.dirname, '../..');
const revision = '0123456789abcdef0123456789abcdef01234567';

test('release notices identify AGPL and exact corresponding source revision', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-notices-'));
  try {
    const license = await readFile(join(repositoryRoot, 'LICENSE'));
    await writeFile(join(directory, 'LICENSE'), license);
    await writeFile(join(directory, 'SOURCE.txt'),
      `OpenCam 0.2.0\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/${revision}\n`);
    await writeFile(join(directory, 'NOTICE.md'),
      'Copyright (C) 2026 Yu-Han Cheng\nOpenCam is licensed under AGPL-3.0-or-later.\n');
    await verifyReleaseNotices(directory, revision, '0.2.0');
    await writeFile(join(directory, 'SOURCE.txt'),
      `OpenCam 0.2.0\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/0000000000000000000000000000000000000000\n`);
    await assert.rejects(verifyReleaseNotices(directory, revision, '0.2.0'), /corresponding source/i);
    await writeFile(join(directory, 'SOURCE.txt'),
      `OpenCam 0.2.0\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/${revision}\n`);
    await rm(join(directory, 'NOTICE.md'));
    await assert.rejects(verifyReleaseNotices(directory, revision, '0.2.0'), /NOTICE/);
    await writeFile(join(directory, 'NOTICE.md'),
      'Copyright (C) 2026 Yu-Han Cheng\nOpenCam is licensed under AGPL-3.0-or-later.\n');
    await rm(join(directory, 'LICENSE'));
    await assert.rejects(verifyReleaseNotices(directory, revision, '0.2.0'), /LICENSE/);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('Windows installer uses an exact plaintext copy of the project license', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'opencam-installer-license-'));
  try {
    const license = await readFile(join(repositoryRoot, 'LICENSE'));
    await writeFile(join(directory, 'LICENSE'), license);
    await writeFile(join(directory, 'SOURCE.txt'),
      `OpenCam 0.2.0\nSPDX-License-Identifier: AGPL-3.0-or-later\nCorresponding source: https://github.com/kaoshou/OpenCam/tree/${revision}\n`);
    await writeFile(join(directory, 'NOTICE.md'),
      'Copyright (C) 2026 Yu-Han Cheng\nOpenCam is licensed under AGPL-3.0-or-later.\n');
    await assert.rejects(verifyReleaseNotices(directory, revision, '0.2.0', { installer: true }), /LICENSE\.txt/);
    await writeFile(join(directory, 'LICENSE.txt'), 'wrong license');
    await assert.rejects(verifyReleaseNotices(directory, revision, '0.2.0', { installer: true }), /LICENSE\.txt/);
    await writeFile(join(directory, 'LICENSE.txt'), license);
    await verifyReleaseNotices(directory, revision, '0.2.0', { installer: true });
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test('canonical version is strict and drives packaging plus localized website content', async () => {
  const raw = await readFile(join(repositoryRoot, 'VERSION'), 'utf8');
  assert.equal(validateVersionText(raw), '0.2.1');
  for (const invalid of ['0.2.1 \n', '0.2.1\n\n', '0.2.x\n', '0.2.1']) {
    assert.throws(() => validateVersionText(invalid), /VERSION/);
  }

  assert.equal(productVersion, '0.2.1');
  assert.match(renderHome('zh-TW'), /v0\.2\.1/);
  assert.match(renderHome('en-US'), /v0\.2\.1/);
  await checkVersionConsistency(repositoryRoot);
});
