#!/usr/bin/env node
// SPDX-License-Identifier: AGPL-3.0-or-later
// Real child-process fixture for runner lifetime tests; never recording evidence.
import { createInterface } from 'node:readline';
import { writeFileSync } from 'node:fs';
const lines = createInterface({ input: process.stdin });
let request;
const emit = value => process.stderr.write('OCLINUX ' + JSON.stringify(value) + '\n');
lines.on('line', line => {
  if (!request) {
    request = JSON.parse(line);
    writeFileSync(request.pidFile, String(process.pid));
    if (request.fixtureBehavior === 'oversized') process.stderr.write('OCLINUX ' + 'x'.repeat(70000) + '\n');
    else if (request.fixtureBehavior === 'error') emit({ event: 'error', code: 'audio_lost' });
    else emit({ event: 'ready', backend: 'x11' });
    return;
  }
  if (request.fixtureBehavior === 'ignore') return;
  emit({ event: 'stopped', backend: 'x11' }); process.exit(0);
});
process.on('SIGTERM', () => {});
setInterval(() => {}, 1000);
