// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { parseProbeEvent } from '../../scripts/linux/probe-protocol.mjs';
const helper = process.env.OPENCAM_LINUX_HELPER, runtime = process.env.OPENCAM_LINUX_RUNTIME;
const enabled = Boolean(helper && runtime);
test('portal preparation without a Wayland display fails without producing media or credentials', { skip: !enabled }, () => {
  const result = spawnSync(helper, ['--runtime', runtime, '--prepare-wayland'], {
    env: { ...process.env, XDG_SESSION_TYPE: 'wayland', WAYLAND_DISPLAY: 'opencam_nonexistent_wayland_display' },
    input: '', timeout: 15000,
  });
  assert.equal(result.status, 2);
  assert.equal(result.stdout.length, 0);
  assert.match(result.stderr.toString(), /OCPORTAL .*desktop_missing/);
  assert.doesNotMatch(result.stderr.toString(), /restoreToken|selected|OCOBS /);
});
const base = { protocol: 1, synthetic: false, width: 640, height: 360, fps: 20,
  x: 0, y: 0, monitorWidth: 640, monitorHeight: 360, cropX: 0, cropY: 0,
  monitor: 'xshm:0', audio: 'none', systemDevice: '', microphone: '', cursor: true, encoder: 'cpu', quality: 23 };
function run(request, backend = 'x11', mode = 'capture') {
  const result = spawnSync(helper, ['--runtime', runtime, '--mode', mode, '--backend', backend], {
    input: JSON.stringify(request) + '\nSTOP\n', timeout: 15000, maxBuffer: 8 * 1024 * 1024,
  });
  return { ...result, events: result.stderr?.toString().split('\n').filter(Boolean).map(parseProbeEvent).filter(Boolean) ?? [] };
}
for (const [name, change] of [
  ['invalid request never emits media', r => r.width = 0],
  ['synthetic capture cannot stand in for a desktop', r => r.synthetic = true],
  ['unsupported encoder cannot switch recording paths', r => r.encoder = 'nvenc'],
]) test(name, { skip: !enabled }, () => {
  const request = { ...base }; change(request); const result = run(request);
  assert.equal(result.status, 2, result.stderr?.toString()); assert.equal(result.stdout.length, 0);
  assert.ok(result.events.some(e => e.event === 'error' && e.code === 'invalid_request'));
});
test('enumeration does not claim recording ready', { skip: !enabled || process.env.OPENCAM_LINUX_REAL_CAPTURE !== '1' }, () => {
  const result = run(base, 'x11', 'enumerate'); assert.equal(result.status, 0, JSON.stringify({ signal: result.signal, error: result.error?.message }) + '\n' + result.stderr?.toString());
  assert.equal(result.stdout.length, 0); assert.ok(result.events.some(e => e.event === 'display'));
  assert.ok(!result.events.some(e => e.event === 'ready'));
  if (process.env.OPENCAM_LINUX_FIXTURE_AUDIO === '1') {
    assert.ok(result.events.some(e => e.event === 'audio_device' && e.source === 'pulse_output_capture' && e.id === 'opencam_test.monitor'), 'system capture must select the enumerated monitor source, not the sink name');
    assert.ok(result.events.some(e => e.event === 'audio_device' && e.source === 'pulse_input_capture' && e.id === 'opencam_mic'), 'microphone fixture must be explicitly enumerated');
  }
});
test('missing selected audio endpoint cannot be silently omitted', { skip: !enabled || process.env.OPENCAM_LINUX_REAL_CAPTURE !== '1' }, () => {
  const result = run({ ...base, audio: 'system', systemDevice: 'opencam_missing_endpoint' });
  assert.notEqual(result.status, 0); assert.equal(result.stdout.length, 0);
  assert.ok(result.events.some(e => e.event === 'error' && e.code === 'audio_device_identity'));
  assert.ok(!result.events.some(e => e.event === 'ready'));
});
test('missing source does not choose the first display', { skip: !enabled || process.env.OPENCAM_LINUX_REAL_CAPTURE !== '1' }, () => {
  const result = run({ ...base, monitor: 'xshm:999999999' });
  assert.notEqual(result.status, 0); assert.equal(result.stdout.length, 0);
  assert.ok(result.events.some(e => e.event === 'error' && e.code === 'display_identity'));
});
test('Wayland session never falls back to XWayland root capture', { skip: !enabled }, () => {
  const result = spawnSync(helper, ['--runtime', runtime, '--mode', 'capture', '--backend', 'x11'], {
    env: { ...process.env, XDG_SESSION_TYPE: 'wayland', WAYLAND_DISPLAY: 'wayland-test' },
    input: JSON.stringify(base) + '\n', timeout: 15000,
  });
  assert.equal(result.status, 2); assert.equal(result.stdout.length, 0);
  assert.match(result.stderr.toString(), /wayland_requires_portal/);
});
