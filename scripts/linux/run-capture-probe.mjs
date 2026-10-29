// SPDX-License-Identifier: AGPL-3.0-or-later
import { mkdir, writeFile, stat } from 'node:fs/promises';
import { createWriteStream } from 'node:fs';
import { finished } from 'node:stream/promises';
import { StringDecoder } from 'node:string_decoder';
import { spawn, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { isAbsolute, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseProbeEvent, evaluateProbeEvidence } from './probe-protocol.mjs';
const execute = promisify(execFile);
export async function runCaptureProbe({ helper, stage, request, seconds = 3, evidenceDirectory, signal,
  startupTimeoutMs = 70000, stopTimeoutMs = 5000 }) {
  if (![helper, stage, evidenceDirectory].every(p => typeof p === 'string' && isAbsolute(p)) ||
      !request || !Number.isFinite(seconds) || seconds <= 0 || seconds > 600 ||
      !Number.isFinite(startupTimeoutMs) || startupTimeoutMs < 1 || startupTimeoutMs > 70000 ||
      !Number.isFinite(stopTimeoutMs) || stopTimeoutMs < 1 || stopTimeoutMs > 5000)
    throw new Error('Invalid bounded probe invocation');
  await mkdir(evidenceDirectory, { mode: 0o700 }); // exclusive new leaf, never overwrite user data
  const redacted = { ...request };
  for (const key of ['monitor', 'microphone', 'systemDevice', 'pidFile']) if (key in redacted) redacted[key] = '[redacted]';
  await writeFile(join(evidenceDirectory, 'request-redacted.json'), JSON.stringify(redacted, null, 2), { flag: 'wx' });
  const events = [], reasons = [], log = [], decoder = new StringDecoder('utf8');
  let child, closed = false, stopping = false, mediaBytes = 0, stderrBytes = 0, line = '';
  let stopTimer, killTimer, leaseTimer;
  const fail = code => { if (!reasons.includes(code)) reasons.push(code); };
  const media = join(evidenceDirectory, 'capture.mkv');
  const output = createWriteStream(media, { flags: 'wx', mode: 0o600 });
  const outputDone = finished(output); outputDone.catch(() => {});
  const stop = () => {
    if (closed || stopping || !child) return;
    stopping = true;
    if (child.stdin.writable) child.stdin.end('STOP\n');
    killTimer = setTimeout(() => { if (!closed) { fail('cleanup_timeout'); child.kill('SIGKILL'); } }, stopTimeoutMs);
  };
  const abort = () => { fail('cancelled'); stop(); };
  let exitCode;
  try {
    child = spawn(helper, ['--runtime', stage, '--mode', 'capture', '--backend', request.backend ?? 'x11'], { stdio: ['pipe', 'pipe', 'pipe'] });
    const done = new Promise(resolveExit => {
      child.once('error', () => { fail('spawn_failed'); });
      child.once('close', (code, termination) => { closed = true; resolveExit({ code, termination }); });
    });
    output.on('error', () => { fail('media_write_failed'); stop(); });
    child.stdin.on('error', error => { if (error.code !== 'EPIPE') { fail('control_pipe_failed'); stop(); } });
    child.stdout.on('data', bytes => {
      mediaBytes += bytes.length;
      if (mediaBytes > 1024 * 1024 * 1024) { fail('media_limit'); stop(); return; }
      if (!output.write(bytes)) { child.stdout.pause(); output.once('drain', () => child.stdout.resume()); }
    });
    child.stderr.on('data', bytes => {
      stderrBytes += bytes.length;
      if (stderrBytes > 4 * 1024 * 1024) { fail('diagnostic_limit'); stop(); return; }
      const text = decoder.write(bytes); log.push(text); line += text;
      try {
        for (;;) {
          const at = line.indexOf('\n'); if (at < 0) break;
          const event = parseProbeEvent(line.slice(0, at)); line = line.slice(at + 1);
          if (!event) continue;
          events.push(event);
          if (event.event === 'error') { fail(/^[a-z_]{1,64}$/.test(event.code) ? event.code : 'native_error'); stop(); }
          if (event.event === 'ready' && !stopTimer) {
            clearTimeout(leaseTimer); stopTimer = setTimeout(stop, seconds * 1000);
          }
        }
        if (Buffer.byteLength(line) > 65536) throw new Error('Oversized line');
      } catch { fail('invalid_protocol'); stop(); }
    });
    leaseTimer = setTimeout(() => { fail('startup_timeout'); stop(); }, startupTimeoutMs);
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) abort(); else child.stdin.write(JSON.stringify(request) + '\n');
    const exit = await done; exitCode = exit.code;
    line += decoder.end();
    if (line.trim()) { try { const event = parseProbeEvent(line); if (event) events.push(event); } catch { fail('invalid_protocol'); } }
  } finally {
    clearTimeout(leaseTimer); clearTimeout(stopTimer); clearTimeout(killTimer);
    signal?.removeEventListener('abort', abort);
    output.end();
    try { await outputDone; } catch { fail('media_write_failed'); }
  }
  await writeFile(join(evidenceDirectory, 'events.json'), JSON.stringify(events, null, 2), { flag: 'wx' });
  await writeFile(join(evidenceDirectory, 'helper.log'), log.join(''), { flag: 'wx' });
  let mediaProbe, fullDecodeExit;
  if (!reasons.length && mediaBytes && events.some(e => e.event === 'ready')) {
    try {
      const options = { timeout: 60000, maxBuffer: 32 * 1024 * 1024, killSignal: 'SIGKILL' };
      const probe = await execute('ffprobe', ['-v', 'error', '-show_streams', '-show_packets', '-of', 'json', media], options);
      await writeFile(join(evidenceDirectory, 'ffprobe.json'), probe.stdout, { flag: 'wx' });
      const decoded = await execute('ffmpeg', ['-xerror', '-v', 'error', '-i', media, '-f', 'null', '-'], options);
      fullDecodeExit = 0;
      await writeFile(join(evidenceDirectory, 'decode.log'), decoded.stderr, { flag: 'wx' });
      const hashes = await execute('ffmpeg', ['-xerror', '-v', 'error', '-i', media, '-map', '0:v:0', '-f', 'framehash', '-'], options);
      await writeFile(join(evidenceDirectory, 'framehash.txt'), hashes.stdout, { flag: 'wx' });
      mediaProbe = { ...JSON.parse(probe.stdout), expected: { ...request, backend: request.backend ?? 'x11' },
        packetScanComplete: true, frameScanComplete: true,
        frameHashes: hashes.stdout.split('\n').filter(l => l && !l.startsWith('#')).map(l => l.split(',').at(-1).trim()) };
    } catch (error) {
      fail('media_validation_failed');
      await writeFile(join(evidenceDirectory, 'validation-error.log'), String(error.stderr ?? error.code ?? 'validation_failed').slice(0, 1024 * 1024), { flag: 'wx' });
    }
  }
  const result = reasons.length ? { status: 'FAIL', reasons } : evaluateProbeEvidence({ exitCode, events, mediaProbe, fullDecodeExit });
  const report = { ...result, exitCode, mediaBytes: (await stat(media)).size, scope: 'native_diagnostic_probe_not_product_or_physical_sync_acceptance' };
  await writeFile(join(evidenceDirectory, 'result.json'), JSON.stringify(report, null, 2), { flag: 'wx' });
  return result;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const [helper, stage, evidenceDirectory, seconds, requestJson] = process.argv.slice(2);
    const result = await runCaptureProbe({ helper, stage, evidenceDirectory, seconds: Number(seconds), request: JSON.parse(requestJson) });
    console.log(JSON.stringify(result)); process.exitCode = result.status === 'PASS' ? 0 : 1;
  } catch (error) { console.error(error.message); process.exitCode = 2; }
}
