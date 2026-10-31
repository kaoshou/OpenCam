// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateAppMedia, validateFixtureColors, validatePointerFixture } from '../../scripts/linux/verify-app-media.mjs';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
function fixture() { return { streams: [{ index: 0, codec_type: 'video' }, { index: 1, codec_type: 'audio' }],
  packets: [0, 1, 2].flatMap(t => [0, 1].map(index => ({ stream_index: index, pts_time: String(t), dts_time: String(t), duration_time: '1' }))) }; }
test('recorded teaching pointer proof rejects a valid changing background without the pointer',()=> {
  const pixels=Buffer.alloc(1000*3);for(let i=0;i<pixels.length;i+=3){pixels[i]=224;pixels[i+1]=64;pixels[i+2]=32;}
  assert.throws(()=>validatePointerFixture(pixels),/pointer/);
  for(let i=0;i<250*3;i++)pixels[i]=255;
  assert.throws(()=>validatePointerFixture(pixels),/pointer/);
  pixels.fill(30,250*3,500*3);
  assert.doesNotThrow(()=>validatePointerFixture(pixels));
});
test('dynamic-content proof requires both fixture colors, not encoder hash noise', () => {
  assert.doesNotThrow(() => validateFixtureColors(Buffer.from([224,64,32,32,64,224])));
  assert.throws(() => validateFixtureColors(Buffer.from([0,0,0,1,1,1,0,1,0])), /colors/);
  assert.throws(() => validateFixtureColors(Buffer.from([224,64,32,223,64,32])), /colors/);
});
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
test('an invalid exported MP4 makes the actual verification command fail', async t => {
  const root = await mkdtemp(join(tmpdir(), 'opencam-invalid-export-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const mp4 = join(root, 'invalid.mp4'), result = join(root, 'result.json');
  await writeFile(mp4, 'not an MP4'); await writeFile(result, JSON.stringify({ mp4, firstSegmentSeconds: 5 }));
  await assert.rejects(promisify(execFile)(process.execPath, ['scripts/linux/verify-app-media.mjs', result], { timeout: 10000 }), error => error.code === 1);
});
