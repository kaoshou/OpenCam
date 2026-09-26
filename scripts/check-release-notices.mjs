// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { noticePath } from './assemble-third-party-notices.mjs';

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
  await verifyThirdPartyNotices(directory);
}

export async function verifyThirdPartyNotices(directory) {
  try {
    const summary = await readFile(join(directory, 'THIRD-PARTY-NOTICES.md'), 'utf8');
    if (!summary.trim()) throw new Error('Empty summary');
    const base = join(directory, 'third-party');
    const manifest = JSON.parse(await readFile(join(base, 'manifest.json'), 'utf8'));
    const catalog = JSON.parse(await readFile(join(base, 'catalog.json'), 'utf8'));
    if (manifest.schema !== 1 || !['win-x64', 'osx-arm64'].includes(manifest.rid)
      || !manifest.runtime?.startsWith(`microsoft.netcore.app.runtime.${manifest.rid}/`)
      || !Array.isArray(manifest.packages) || !manifest.packages.length
      || !Array.isArray(catalog.files) || !catalog.files.length) throw new Error('Incomplete manifest');
    for (const name of manifest.packages) if (!catalog.packages.includes(name)) throw new Error(`Unreviewed dependency: ${name}`);
    const paths = new Set();
    for (const file of manifest.files) {
      const path = noticePath(file.path);
      if (paths.has(path)) throw new Error('Duplicate notice path');
      paths.add(path);
      const bytes = await readFile(join(base, path));
      if (!bytes.length || createHash('sha256').update(bytes).digest('hex') !== file.sha256) throw new Error(`Changed notice: ${path}`);
    }
    for (const file of catalog.files) {
      if (!manifest.files.some(f => f.path === file.path && f.sha256 === file.sha256)) throw new Error(`Missing upstream notice: ${file.path}`);
    }
    for (const path of ['runtime/LICENSE.TXT', 'runtime/THIRD-PARTY-NOTICES.TXT',
      'ffmpeg/ffmpeg-license.txt', 'ffmpeg/ffmpeg-version.txt', 'ffmpeg/ffprobe-license.txt', 'ffmpeg/ffprobe-version.txt']) {
      if (!paths.has(path)) throw new Error(`Missing ${path}`);
    }
  } catch (error) { throw new Error(`Invalid third-party notices: ${error.message}`, { cause: error }); }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [, , directory, revision, version, option] = process.argv;
  await verifyReleaseNotices(directory, revision, version, { installer: option === '--installer' });
  process.stdout.write(`Verified release notices: ${directory}\n`);
}
