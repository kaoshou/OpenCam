#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { validateVersionText } from './check-version-consistency.mjs';

try {
  const raw = await readFile(process.argv[2] ?? new URL('../VERSION', import.meta.url), 'utf8');
  const version = validateVersionText(raw);
  // Emit only validated, single-line values suitable for GITHUB_ENV on either OS.
  console.log([
    `OPENCAM_VERSION=${version}`,
    `WINDOWS_SETUP=OpenCam_v${version}_Windows_x64_Setup.exe`,
    `WINDOWS_PORTABLE=OpenCam_v${version}_Windows_x64_Portable.zip`,
    `MACOS_DMG=OpenCam_v${version}_macOS_arm64.dmg`,
    `LINUX_DEB=OpenCam_v${version}_Linux_x64_Beta.deb`,
    `LINUX_ARCHIVE=OpenCam_v${version}_Linux_x64_Beta.tar.gz`,
    `LINUX_SOURCES=OpenCam_v${version}_Linux_Beta_CorrespondingSource.tar.gz`,
    `LINUX_CHECKSUMS=OpenCam_v${version}_Linux_Beta_SHA256SUMS.txt`,
  ].join('\n'));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
