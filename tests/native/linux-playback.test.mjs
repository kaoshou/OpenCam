// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {setTimeout as delay} from 'node:timers/promises';

const helper = process.env.OPENCAM_LINUX_PLAYBACK_HELPER;
test('Pulse playback advances its device clock with only the bounded preview queue, before EOF',
  {skip: process.platform !== 'linux' || !helper, timeout: 12000}, async () => {
    const child = spawn(helper, [], {stdio: ['pipe', 'pipe', 'pipe']});
    let clock = -1, diagnostic = '', closed = false;
    const exit = new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', (code, signal) => { closed = true; resolve({code, signal}); });
    });
    child.stderr.on('data', bytes => { diagnostic = (diagnostic + bytes).slice(-8192); });
    child.stdin.on('error', () => {}); // Exit/clock assertions below own failure reporting.
    const lines = createInterface({input: child.stdout});
    lines.on('line', line => { const value = JSON.parse(line).sampleClock;
      assert.ok(Number.isSafeInteger(value) && value >= clock); clock = value; });
    try {
      // Preview stops adding PCM once queued samples exceed 4800, plus one 1024-frame block.
      // Do NOT close stdin: EOF/drain previously hid the default-prebuffer deadlock.
      for (let i = 0; i < 5; i++) child.stdin.write(Buffer.alloc(8192));
      const deadline = Date.now() + 4000;
      while (clock < 1024 && !closed && Date.now() < deadline) await delay(20);
      assert.ok(clock >= 1024, `Device clock cannot release the bounded producer with 5120 queued frames before EOF; clock=${clock}; ${diagnostic}`);
      child.stdin.end();
      assert.deepEqual(await exit, {code: 0, signal: null});
      assert.equal(clock, 5120, 'Final drain must acknowledge every submitted stereo frame.');
    } finally {
      lines.close();
      if (!closed) { child.kill('SIGKILL'); await exit; }
    }
  });
