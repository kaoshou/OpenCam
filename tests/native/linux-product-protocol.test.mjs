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
