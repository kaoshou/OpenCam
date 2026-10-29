// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
const helper = process.env.OPENCAM_LINUX_HELPER, runtime = process.env.OPENCAM_LINUX_RUNTIME;
test('product helper rejects invalid geometry with OCOBS error and no media', { skip: !helper || !runtime }, async () => {
  const child = spawn(helper, ['--runtime', runtime]);
  let bytes = 0, log = '';
  child.stdout.on('data', x => bytes += x.length);
  child.stderr.on('data', x => log += x);
  child.stdin.on('error', () => {});
  const done = new Promise(resolve => child.on('close', code => resolve(code)));
  child.stdin.end(JSON.stringify({ protocol: 1, width: 0 }) + '\n');
  const code = await done;
  assert.notEqual(code, 0); assert.equal(bytes, 0);
  assert.match(log, /OCOBS .*"event":"error".*"code":"invalid_request"/);
});
test('product Wayland refuses an unverified portal monitor identity without media', { skip: !helper || !runtime }, async () => {
  const child = spawn(helper, ['--runtime', runtime], { env: { ...process.env, XDG_SESSION_TYPE: 'wayland' } });
  let bytes = 0, log = ''; child.stdout.on('data', x => bytes += x.length); child.stderr.on('data', x => log += x);
  child.stdin.on('error', () => {}); const done = new Promise(resolve => child.on('close', resolve));
  child.stdin.end(JSON.stringify({ protocol: 1, width: 640, height: 360, monitorWidth: 640, monitorHeight: 360,
    fps: 20, quality: 23, cropX: 0, cropY: 0, monitor: 'portal:unverified', audio: 'none', encoder: 'cpu' }) + '\n');
  assert.notEqual(await done, 0); assert.equal(bytes, 0); assert.match(log, /"code":"portal_source_unverified"/);
});
