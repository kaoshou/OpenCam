// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const officialLicenseSha256 = '0d96a4ff68ad6d4b6f1f30f713b18d5184912ba8dd389f86aa7710db079abcb0';

export async function verifyReleaseNotices(directory, revision, version, { installer = false } = {}) {
  if (!/^[0-9a-f]{40}$/i.test(revision)) throw new Error('Expected a 40-character source revision');
  if (!/^\d+\.\d+\.\d+$/.test(version)) throw new Error('Expected a semantic version');

  let license;
  try {
    license = await readFile(join(directory, 'LICENSE'));
  } catch {
    throw new Error('Missing LICENSE in release package');
  }
  const digest = createHash('sha256').update(license).digest('hex');
  if (digest !== officialLicenseSha256) throw new Error('Release LICENSE differs from the official AGPLv3 text');

  if (installer) {
    let installerLicense;
    try {
      installerLicense = await readFile(join(directory, 'LICENSE.txt'));
    } catch {
      throw new Error('Missing LICENSE.txt for Windows installer');
    }
    if (!installerLicense.equals(license)) throw new Error('Windows installer LICENSE.txt differs from LICENSE');
  }

  let source;
  try {
    source = await readFile(join(directory, 'SOURCE.txt'), 'utf8');
  } catch {
    throw new Error('Missing SOURCE.txt in release package');
  }
  if (!source.includes(`OpenCam ${version}`) || !source.includes('SPDX-License-Identifier: AGPL-3.0-or-later')) {
    throw new Error('SOURCE.txt has the wrong version or license');
  }
  if (!source.includes(`Corresponding source: https://github.com/kaoshou/OpenCam/tree/${revision}`)) {
    throw new Error('SOURCE.txt does not identify the corresponding source revision');
  }

  let notice;
  try {
    notice = await readFile(join(directory, 'NOTICE.md'), 'utf8');
  } catch {
    throw new Error('Missing NOTICE.md in release package');
  }
  if (!notice.includes('Copyright (C) 2026 Yu-Han Cheng') || !notice.includes('AGPL-3.0-or-later')) {
    throw new Error('NOTICE.md has the wrong copyright or license');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [, , directory, revision, version, option] = process.argv;
  await verifyReleaseNotices(directory, revision, version, { installer: option === '--installer' });
  process.stdout.write(`Verified release notices: ${directory}\n`);
}
