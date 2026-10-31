// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile, writeFile } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
const execute = promisify(execFile);
export function validatePointerFixture(samples) {
  let light=0,dark=0;
  for(let i=0;i+2<samples.length;i+=3) {
    if(samples[i]>190&&samples[i+1]>190&&samples[i+2]>190)light++;
    if(samples[i]<90&&samples[i+1]<90&&samples[i+2]<90)dark++;
  }
  if(light<150||dark<150)throw new Error('Recorded high-contrast pointer is missing from its known fixture position');
}
export function validateFixtureColors(samples) {
  let red = false, blue = false;
  for (let i = 0; i + 2 < samples.length; i += 3) {
    red ||= samples[i] > samples[i+2] + 50;
    blue ||= samples[i+2] > samples[i] + 50;
  }
  if (!red || !blue) throw new Error('Both changing fixture colors must actually appear');
}
export function validateAppMedia(probe, hashes, expectedSeconds) {
  const video = probe.streams.find(s => s.codec_type === 'video');
  const audio = probe.streams.find(s => s.codec_type === 'audio');
  if (!video || !audio) throw new Error('Expected video and audio streams');
  const ranges = [];
  for (const stream of [video, audio]) {
    const packets = probe.packets.filter(p => p.stream_index === stream.index);
    if (!packets.length) throw new Error('Missing packets');
    let previous = -Infinity, first = Infinity, end = -Infinity;
    for (const packet of packets) {
      const dts = Number(packet.dts_time), pts = Number(packet.pts_time), duration = Number(packet.duration_time);
      if (![dts, pts, duration].every(Number.isFinite) || duration <= 0) throw new Error('Invalid packet duration/timestamp');
      if (dts < previous) throw new Error('Non-monotonic decoding timestamps');
      previous = dts; first = Math.min(first, pts); end = Math.max(end, pts + duration);
    }
    ranges.push({ index: stream.index, first, end, duration: end - first, packets: packets.length });
  }
  if (Math.abs(ranges[0].first - ranges[1].first) > .25 || Math.abs(ranges[0].end - ranges[1].end) > .25)
    throw new Error('Audio/video packet timeline drift exceeds 250ms');
  if (Math.abs(ranges[0].duration - expectedSeconds) > 3) throw new Error('Unexpected media duration');
  if (new Set(hashes).size < 2) throw new Error('Unexpected static video content');
  return { status: 'PASS', ranges, distinctFrameHashes: new Set(hashes).size,
    scope: 'packet timeline and dynamic virtual content; not perceptual physical A/V synchronization' };
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const resultPath = process.argv[2]; const result = JSON.parse(await readFile(resultPath, 'utf8'));
    const options = { timeout: 180000, maxBuffer: 128 * 1024 * 1024, killSignal: 'SIGKILL' };
    const files = [{ path: result.mp4, seconds: result.firstSegmentSeconds + 3, name: 'mp4' },
      ...(result.sourceFiles ?? []).map((path, i) => ({ path, seconds: i === 0 ? result.firstSegmentSeconds : 3, name: 'mkv-' + i }))];
    const measured = [];
    for (const file of files) {
      const prefix = resultPath + '.' + file.name;
      const probe = await execute('ffprobe', ['-v', 'error', '-show_streams', '-show_packets', '-of', 'json', file.path], options);
      await writeFile(prefix + '.ffprobe.json', probe.stdout, { flag: 'wx' });
      const decoded = await execute('ffmpeg', ['-xerror', '-v', 'error', '-i', file.path, '-f', 'null', '-'], options);
      await writeFile(prefix + '.decode.log', decoded.stderr, { flag: 'wx' });
      const hashes = await execute('ffmpeg', ['-xerror', '-v', 'error', '-i', file.path, '-map', '0:v:0', '-f', 'framehash', '-'], options);
      const colors = await execute('ffmpeg', ['-xerror', '-v', 'error', '-i', file.path, '-map', '0:v:0', '-vf', 'fps=2,scale=1:1:flags=area,format=rgb24', '-f', 'rawvideo', '-'], { ...options, encoding: 'buffer' });
      validateFixtureColors(colors.stdout);
      if(result.teachingPointer&&file.name!=='mkv-1') {
        const pointer=await execute('ffmpeg',['-xerror','-v','error','-ss','1','-i',file.path,'-frames:v','1','-vf','crop=140:120:184:184,format=rgb24','-f','rawvideo','-'],{...options,encoding:'buffer'});
        validatePointerFixture(pointer.stdout);
      }
      measured.push({ file: file.name, ...validateAppMedia(JSON.parse(probe.stdout), hashes.stdout.split('\n').filter(l => l && !l.startsWith('#')).map(l => l.split(',').at(-1).trim()), file.seconds) });
    }
    await writeFile(resultPath + '.media-check.json', JSON.stringify(measured, null, 2), { flag: 'wx' });
    console.log(JSON.stringify(measured));
  } catch (error) { console.error(error.stderr ?? error.message); process.exitCode = 1; }
}
