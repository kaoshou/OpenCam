// SPDX-License-Identifier: AGPL-3.0-or-later
// Usage: node scripts/build-windows-icon.mjs
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(repo, 'src/ScreenRecorder.UI/Assets/app_icon.png');
const outputs = [
  join(repo, 'src/ScreenRecorder.UI/Assets/app_icon.ico'),
  join(repo, 'src/ScreenRecorder.Recorder/app_icon.ico'),
];
const sizes = [256, 128, 64, 48, 32, 16];
const temporary = mkdtempSync(join(tmpdir(), 'opencam-icons-'));

try {
  const images = sizes.map((size) => {
    const target = join(temporary, `${size}.png`);
    const result = spawnSync('ffmpeg', [
      '-v', 'error', '-y', '-i', source,
      '-vf', `scale=${size}:${size}:flags=lanczos`,
      '-frames:v', '1', target,
    ], { encoding: 'utf8' });
    if (result.error || result.status !== 0) {
      throw new Error(`ffmpeg could not render ${size}px icon: ${result.stderr || result.error}`);
    }
    return readFileSync(target);
  });

  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2); // ICO format
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  images.forEach((png, index) => {
    const position = 6 + index * 16;
    header[position] = sizes[index] === 256 ? 0 : sizes[index];
    header[position + 1] = sizes[index] === 256 ? 0 : sizes[index];
    header.writeUInt16LE(1, position + 4); // color planes
    header.writeUInt16LE(32, position + 6); // bits per pixel
    header.writeUInt32LE(png.length, position + 8);
    header.writeUInt32LE(offset, position + 12);
    offset += png.length;
  });
  const ico = Buffer.concat([header, ...images]);
  outputs.forEach((path) => writeFileSync(path, ico));
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
