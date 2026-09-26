// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

test('Pages workflow builds site separately from desktop releases', async () => {
  const path = resolve(import.meta.dirname, '../../.github/workflows/deploy-pages.yml');
  const yaml = await readFile(path, 'utf8');
  for (const expected of ['branches: [master]', "'website/**'", "'docs/USER_GUIDE.*.md'", "'docs/images/**'", "'src/ScreenRecorder.UI/Assets/app_icon.png'", "'.github/workflows/deploy-pages.yml'", 'workflow_dispatch:', 'npm ci --prefix website', 'npm test --prefix website', 'npm run build --prefix website', 'path: website/dist', 'name: github-pages', 'pages: write', 'id-token: write', "github.ref == 'refs/heads/master'"]) {
    assert.ok(yaml.includes(expected), `Missing ${expected}`);
  }
  assert.doesNotMatch(yaml, /gh release|tags:/);
  assert.match(yaml, /deploy:\s*\n\s*if: github\.ref/);
});

test('desktop releases are gated by tests and reproducible FFmpeg inputs', async () => {
  const path = resolve(import.meta.dirname, '../../.github/workflows/build-and-release.yml');
  const yaml = await readFile(path, 'utf8');
  const windowsInstaller = await readFile(
    resolve(import.meta.dirname, '../../scripts/install-windows-ffmpeg.ps1'),
    'utf8');
  const pinnedUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-25-15-37/ffmpeg-n8.1.3-win64-gpl-8.1.zip';
  const sha256 = '8efaa4e62db01a71580dc5a7ec0625dea7a4dfc5fac2d680f94804503d18a34c';

  assert.match(yaml, /permissions:\s*\n\s*contents: read/);
  assert.equal((yaml.match(/contents: write/g) ?? []).length, 1);
  assert.match(yaml, /publish-release:[\s\S]*?permissions:\s*\n\s*contents: write/);
  assert.match(yaml, /dotnet test ScreenRecorder\.sln/);
  assert.match(yaml, /verify-portable:[\s\S]*?runs-on: ubuntu-latest[\s\S]*?npm test --prefix website/);
  assert.match(yaml, /verify-portable:[\s\S]*?apt-get install -y ffmpeg[\s\S]*?dotnet test ScreenRecorder\.sln/);
  assert.match(yaml, /verify-windows:[\s\S]*?install-windows-ffmpeg\.ps1[\s\S]*?GITHUB_PATH[\s\S]*?dotnet test ScreenRecorder\.sln/);
  assert.match(yaml, /verify-macos:[\s\S]*?runs-on: macos-15[\s\S]*?npm test --prefix website/);
  assert.match(yaml, /verify-macos:[\s\S]*?brew install ffmpeg[\s\S]*?dotnet test ScreenRecorder\.sln/);
  assert.match(yaml, /verify-macos:[\s\S]*?tests\/native\/OpenCamSystemAudioTests\.sh/);
  assert.ok(yaml.includes('scripts/check-nuget-vulnerabilities.mjs'));
  assert.match(yaml, /build-windows:\s*\n\s*needs:\s*\[[^\]]*verify-portable[^\]]*verify-windows[^\]]*\]/);
  assert.match(yaml, /build-macos:\s*\n\s*needs:\s*\[[^\]]*verify-portable[^\]]*verify-macos[^\]]*\]/);
  assert.doesNotMatch(windowsInstaller, /BtbN\/FFmpeg-Builds\/releases\/download\/latest\//);
  assert.ok(windowsInstaller.includes(pinnedUrl));
  assert.ok(windowsInstaller.includes(sha256));

  const hashCheck = windowsInstaller.indexOf('Get-FileHash -Algorithm SHA256');
  const extraction = windowsInstaller.indexOf('Expand-Archive');
  assert.ok(hashCheck >= 0 && extraction > hashCheck, 'checksum must be verified before extraction');
  assert.match(windowsInstaller, /Test-Path[^\n]*ffmpeg-n8\.1\.3-win64-gpl-8\.1\\bin\\ffmpeg\.exe/);
  assert.match(windowsInstaller, /Test-Path[^\n]*ffmpeg-n8\.1\.3-win64-gpl-8\.1\\bin\\ffprobe\.exe/);
});
