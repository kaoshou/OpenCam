#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const dynamicVersionFiles = [
  'src/ScreenRecorder.UI/ScreenRecorder.UI.csproj',
  'src/ScreenRecorder.UI/app.manifest',
  'src/ScreenRecorder.Core/Localization/LocalizationService.cs',
  'src/ScreenRecorder.UI/Views/MainWindow.axaml',
  'src/ScreenRecorder.UI/Views/AboutWindow.axaml',
  'src/ScreenRecorder.UI/Views/SettingsWindow.axaml',
  'installer/OpenCam.iss',
  'installer/build_installer.ps1',
  'scripts/package-macos.sh',
  '.github/workflows/build-and-release.yml',
  'website/src/content.mjs',
];

export function validateVersionText(text) {
  if (!/^(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\n$/.test(text)) {
    throw new Error('VERSION must contain numeric SemVer and exactly one final LF');
  }
  return text.slice(0, -1);
}

export async function checkVersionConsistency(repositoryRoot) {
  const versionText = await readFile(resolve(repositoryRoot, 'VERSION'), 'utf8');
  const version = validateVersionText(versionText);
  const files = await Promise.all(dynamicVersionFiles.map(async (relativePath) => ({
    relativePath,
    text: await readFile(resolve(repositoryRoot, relativePath), 'utf8'),
  })));

  for (const file of files) {
    if (file.text.includes(version)) {
      throw new Error(`${file.relativePath} contains independent product version ${version}`);
    }
  }

  const props = await readFile(resolve(repositoryRoot, 'Directory.Build.props'), 'utf8');
  for (const expected of [
    'ReadAllText',
    'VERSION',
    '<Version>$(OpenCamVersion)</Version>',
    '<AssemblyVersion>$(OpenCamVersion).0</AssemblyVersion>',
    '<FileVersion>$(OpenCamVersion).0</FileVersion>',
  ]) {
    if (!props.includes(expected)) {
      throw new Error(`Directory.Build.props is missing ${expected}`);
    }
  }

  const inno = files.find((file) => file.relativePath === 'installer/OpenCam.iss').text;
  if (!/#ifndef MyAppVersion/.test(inno) || !/AppVersion=\{#MyAppVersion\}/.test(inno)) {
    throw new Error('Inno Setup must require the canonical MyAppVersion define');
  }
  const workflow = files.find((file) =>
    file.relativePath === '.github/workflows/build-and-release.yml').text;
  if (!workflow.includes('OPENCAM_VERSION') ||
      !workflow.includes('scripts/check-version-consistency.mjs')) {
    throw new Error('Release workflow does not load and verify VERSION');
  }
  const macPackage = files.find((file) =>
    file.relativePath === 'scripts/package-macos.sh').text;
  if (!macPackage.includes('product_version') || !macPackage.includes('VERSION')) {
    throw new Error('macOS packaging does not consume VERSION');
  }
  const website = files.find((file) => file.relativePath === 'website/src/content.mjs').text;
  if (!website.includes('productVersion') || !website.includes('../../VERSION')) {
    throw new Error('Website content does not consume VERSION');
  }

  return version;
}

const isDirect = process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isDirect) {
  const repositoryRoot = resolve(fileURLToPath(new URL('..', import.meta.url)));
  try {
    const version = await checkVersionConsistency(repositoryRoot);
    console.log(`OpenCam version consistency passed: ${version}`);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
