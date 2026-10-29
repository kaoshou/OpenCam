// SPDX-License-Identifier: AGPL-3.0-or-later
// Diagnostic acceptance only. PASS here is virtual capture evidence, never a
// physical-desktop, listening-quality or long-duration synchronization claim.
export function parseProbeEvent(line) {
  if (typeof line !== 'string' || Buffer.byteLength(line) > 65536) throw new Error('Probe line exceeds 64 KiB');
  if (!line.startsWith('OCLINUX ')) return null;
  const value = JSON.parse(line.slice(8));
  if (!value || Array.isArray(value) || typeof value !== 'object' ||
      typeof value.event !== 'string' || !/^[a-z_]{1,64}$/.test(value.event)) throw new Error('Invalid probe event');
  return value;
}
const number = value => (typeof value === 'number' || typeof value === 'string' && /^-?\d+(?:\.\d+)?$/.test(value)) &&
  Number.isFinite(Number(value)) ? Number(value) : NaN;
function validPackets(packets, streams) {
  if (!Array.isArray(packets) || !packets.length) return false;
  const known = new Set(streams.map(s => s.index)), last = new Map(), seen = new Set();
  for (const p of packets) {
    const dts = number(p.dts_time), pts = number(p.pts_time), duration = number(p.duration_time);
    if (!known.has(p.stream_index) || !Number.isFinite(dts) || !Number.isFinite(pts) || !(duration > 0) ||
        last.has(p.stream_index) && dts < last.get(p.stream_index)) return false;
    last.set(p.stream_index, dts); seen.add(p.stream_index);
  }
  return streams.every(s => seen.has(s.index));
}
export function evaluateProbeEvidence({ exitCode, events = [], mediaProbe, fullDecodeExit } = {}) {
  const reasons = [];
  const fail = code => { if (!reasons.includes(code)) reasons.push(code); };
  if (!Array.isArray(events)) return { status: 'FAIL', reasons: ['invalid_events'] };
  const errors = events.filter(e => e?.event === 'error');
  for (const e of errors) fail(typeof e.code === 'string' ? e.code : 'native_error');
  // Environment absence is manual only before recording starts, never a way to
  // relabel a device failure or an interrupted recording as a harmless skip.
  if (!errors.length && !events.some(e => e?.event === 'ready') && events.some(e => e?.event === 'environment_unavailable'))
    return { status: 'REQUIRES_MANUAL_VALIDATION', reasons: ['desktop_environment_unavailable'] };
  if (exitCode !== 0) fail('helper_failed');
  if (!events.some(e => e?.event === 'ready')) fail('missing_ready');
  if (!events.some(e => e?.event === 'stopped')) fail('missing_stopped');
  if (fullDecodeExit !== 0) fail('decode_failed');
  const expected = mediaProbe?.expected;
  if (!expected || !['x11', 'pipewire'].includes(expected.backend) ||
      !['none', 'system', 'microphone', 'both'].includes(expected.audio) ||
      !Number.isInteger(expected.width) || expected.width < 2 || !Number.isInteger(expected.height) || expected.height < 2 ||
      !Number.isInteger(expected.fps) || expected.fps < 1) {
    fail('invalid_expected_capture'); return { status: 'FAIL', reasons };
  }
  for (const e of events.filter(e => ['source_ready', 'ready', 'stopped'].includes(e?.event)))
    if (e.backend !== expected.backend) fail('backend_mismatch');
  const source = events.find(e => e?.event === 'source_ready');
  if (!source || source.width !== expected.width || source.height !== expected.height) fail('source_geometry_mismatch');
  const streams = mediaProbe?.streams;
  if (!Array.isArray(streams)) { fail('missing_streams'); return { status: 'FAIL', reasons }; }
  const video = streams.filter(s => s.codec_type === 'video'), audio = streams.filter(s => s.codec_type === 'audio');
  if (video.length !== 1 || video[0].width !== expected.width || video[0].height !== expected.height) fail('geometry_mismatch');
  const ratio = video[0]?.avg_frame_rate?.match(/^(\d+)\/(\d+)$/);
  if (!ratio || !Number(ratio[2]) || Math.abs(Number(ratio[1]) / Number(ratio[2]) - expected.fps) > 0.1) fail('fps_mismatch');
  if (audio.length !== (expected.audio === 'none' ? 0 : 1) || audio.some(s => Number(s.sample_rate) !== 48000 || s.channels !== 2))
    fail('audio_stream_mismatch');
  const selected = expected.audio === 'both' ? ['system', 'microphone'] : expected.audio === 'none' ? [] : [expected.audio];
  for (const role of selected)
    if (!events.some(e => e?.event === 'audio_health' && e.source === role && e.state === 'healthy')) fail('audio_health_missing');
  if (mediaProbe.packetScanComplete !== true || mediaProbe.frameScanComplete !== true) fail('incomplete_media_scan');
  if (!validPackets(mediaProbe.packets, streams)) fail('packet_timing_invalid');
  if (!Array.isArray(mediaProbe.frameHashes) || mediaProbe.frameHashes.some(h => typeof h !== 'string' || !h) ||
      new Set(mediaProbe.frameHashes).size < 2) fail('unchanging_frames');
  return { status: reasons.length ? 'FAIL' : 'PASS', reasons };
}
