// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
const api = () => import('../../scripts/linux/probe-protocol.mjs');
function complete() {
  return { exitCode: 0, fullDecodeExit: 0, events: [
    { event: 'source_ready', backend: 'x11', width: 320, height: 180 },
    { event: 'audio_health', source: 'system', state: 'healthy' },
    { event: 'ready', backend: 'x11' }, { event: 'stopped', backend: 'x11' },
  ], mediaProbe: {
    expected: { backend: 'x11', width: 320, height: 180, fps: 30, audio: 'system' },
    streams: [{ index: 0, codec_type: 'video', width: 320, height: 180, avg_frame_rate: '30/1' },
      { index: 1, codec_type: 'audio', sample_rate: '48000', channels: 2 }],
    packets: [
      { stream_index: 0, pts_time: '0.000', dts_time: '-0.066', duration_time: '0.033' },
      { stream_index: 0, pts_time: '0.066', dts_time: '-0.033', duration_time: '0.033' },
      { stream_index: 0, pts_time: '0.033', dts_time: '0.000', duration_time: '0.033' },
      { stream_index: 1, pts_time: '0.000', dts_time: '0.000', duration_time: '0.021' },
    ], packetScanComplete: true, frameScanComplete: true, frameHashes: ['one', 'two', 'three'],
  } };
}
test('protocol parses framed events, ignores ordinary logs, rejects malformed or oversized lines', async () => {
  const { parseProbeEvent } = await api();
  assert.deepEqual(parseProbeEvent('OCLINUX {"event":"ready","backend":"x11"}'), { event: 'ready', backend: 'x11' });
  assert.equal(parseProbeEvent('ordinary upstream log'), null);
  for (const line of ['OCLINUX {', 'OCLINUX []', 'OCLINUX null', 'OCLINUX {}', 'OCLINUX ' + 'x'.repeat(65536)])
    assert.throws(() => parseProbeEvent(line));
});
test('complete virtual capture accepts reordered PTS with monotonic per-stream DTS', async () => {
  const { evaluateProbeEvidence } = await api();
  assert.deepEqual(evaluateProbeEvidence(complete()), { status: 'PASS', reasons: [] });
});
for (const [name, change, reason] of [
  ['exitZeroWithoutReadyIsNotPass', f => f.events = f.events.filter(e => e.event !== 'ready'), 'missing_ready'],
  ['missing orderly stop is not a success', f => f.events.pop(), 'missing_stopped'],
  ['decodeZeroWithoutDynamicFramesIsNotCapturePass', f => f.mediaProbe.frameHashes = ['same', 'same'], 'unchanging_frames'],
  ['wrongBackendIsNotPass', f => f.mediaProbe.expected.backend = 'pipewire', 'backend_mismatch'],
  ['audioMissingIsNotPass', f => f.mediaProbe.streams.pop(), 'audio_stream_mismatch'],
  ['missing selected endpoint health fails', f => f.events = f.events.filter(e => e.event !== 'audio_health'), 'audio_health_missing'],
  ['second selected endpoint cannot be silently dropped', f => f.mediaProbe.expected.audio = 'both', 'audio_health_missing'],
  ['failed full decode is not a pass', f => f.fullDecodeExit = 1, 'decode_failed'],
  ['wrong geometry fails', f => f.mediaProbe.streams[0].width = 640, 'geometry_mismatch'],
  ['wrong source geometry fails', f => f.events[0].width = 640, 'source_geometry_mismatch'],
  ['wrong fps fails', f => f.mediaProbe.streams[0].avg_frame_rate = '20/1', 'fps_mismatch'],
  ['DTS regression fails even if PTS increases', f => f.mediaProbe.packets[2].dts_time = '-0.100', 'packet_timing_invalid'],
  ['missing duration is not valid timing', f => delete f.mediaProbe.packets[0].duration_time, 'packet_timing_invalid'],
  ['partial packet scans cannot pass', f => f.mediaProbe.packetScanComplete = false, 'incomplete_media_scan'],
  ['error remains failure despite later ready', f => f.events.unshift({ event: 'error', code: 'audio_lost' }), 'audio_lost'],
]) test(name, async () => {
  const { evaluateProbeEvidence } = await api(); const f = complete(); change(f);
  const result = evaluateProbeEvidence(f); assert.equal(result.status, 'FAIL'); assert.ok(result.reasons.includes(reason), JSON.stringify(result));
});
test('manualEnvironmentAbsenceIsNotPass', async () => {
  const { evaluateProbeEvidence } = await api();
  const result = evaluateProbeEvidence({ exitCode: 2, events: [{ event: 'environment_unavailable', code: 'wayland_desktop_missing' }] });
  assert.equal(result.status, 'REQUIRES_MANUAL_VALIDATION');
});
test('no-audio capture is valid without fabricated silent tracks or endpoints', async () => {
  const { evaluateProbeEvidence } = await api(); const f = complete();
  f.mediaProbe.expected.audio = 'none'; f.mediaProbe.streams.pop(); f.mediaProbe.packets.pop();
  f.events = f.events.filter(e => e.event !== 'audio_health');
  assert.equal(evaluateProbeEvidence(f).status, 'PASS');
});
