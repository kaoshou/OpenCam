// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateAppMedia } from '../../scripts/linux/verify-app-media.mjs';
function fixture() { return { streams: [{ index: 0, codec_type: 'video' }, { index: 1, codec_type: 'audio' }],
  packets: [0, 1, 2].flatMap(t => [0, 1].map(index => ({ stream_index: index, pts_time: String(t), dts_time: String(t), duration_time: '1' }))) }; }
test('checks actual packet timing, stream presence and moving content independently', () => {
  assert.equal(validateAppMedia(fixture(), ['a', 'b'], 3).status, 'PASS');
  assert.throws(() => validateAppMedia(fixture(), ['a', 'a'], 3), /static/);
  const regression = fixture(); regression.packets[4].dts_time = '-1';
  assert.throws(() => validateAppMedia(regression, ['a', 'b'], 3), /monotonic/);
  const drift = fixture(); drift.packets[5].duration_time = '2';
  assert.throws(() => validateAppMedia(drift, ['a', 'b'], 3), /drift/);
  assert.throws(() => validateAppMedia(fixture(), ['a', 'b'], 30), /duration/);
  const missing = fixture(); missing.streams.pop();
  assert.throws(() => validateAppMedia(missing, ['a', 'b'], 3), /audio/);
});
