// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const helper = process.env.OPENCAM_OBS_HELPER;
const runtime = process.env.OPENCAM_OBS_RUNTIME;
const ffmpeg = process.env.OPENCAM_TEST_FFMPEG ?? 'ffmpeg';
const ffprobe = process.env.OPENCAM_TEST_FFPROBE ?? 'ffprobe';
const request = { protocol:1, synthetic:true, fps:30, width:320, height:180, x:0,y:0,
  monitor:'synthetic', monitorWidth:320, monitorHeight:180, cropX:0,cropY:0,
  audio:'system', microphone:'',systemDevice:'',cursor:true,encoder:'cpu',quality:23 };

async function record(input, { kill = false, seconds = 3, env = {} } = {}) {
  assert.ok(helper && runtime, 'Set OPENCAM_OBS_HELPER and OPENCAM_OBS_RUNTIME');
  const child = spawn(helper, ['--runtime', runtime], {stdio:['pipe','pipe','pipe'],env:{...process.env,...env}});
  const parts=[], errors=[]; const events=[];
  let stop;
  let deadline=setTimeout(()=>child.kill('SIGKILL'),15000);
  child.stdout.on('data', b => parts.push(b));
  let lines=''; child.stderr.on('data', b => {
    errors.push(b); lines+=b.toString();
    for (;;) { const at=lines.indexOf('\n'); if(at<0)break;
      const line=lines.slice(0,at); lines=lines.slice(at+1);
      if(line.startsWith('OCОBS ')) throw new Error('Non-ASCII protocol prefix');
      if(line.startsWith('OCOBS ')) {
        const event=JSON.parse(line.slice(6));events.push(event);
        // Measure recorded duration from the actual ready acknowledgement, not
        // AppKit/loader cold startup. Otherwise STOP can beat a scheduled fault.
        if(event.event==='ready'&&!stop) {
          clearTimeout(deadline);
          stop=setTimeout(()=>kill?child.kill('SIGKILL'):child.stdin.end('STOP\n'),seconds*1000);
          deadline=setTimeout(()=>child.kill('SIGKILL'),(seconds+8)*1000);
        }
      }
    }
  });
  const done = new Promise((resolve,reject) => {child.once('error',reject);child.once('close',(code,signal)=>resolve({code,signal}));});
  child.stdin.on('error',()=>{});
  child.stdin.write(typeof input === 'string' ? input+'\n' : JSON.stringify(input)+'\n');
  try {
    const exit=await done;const result={...exit, events, bytes:Buffer.concat(parts), log:Buffer.concat(errors).toString()};
    if(process.env.OPENCAM_OBS_TEST_ARTIFACTS) {
      mkdirSync(process.env.OPENCAM_OBS_TEST_ARTIFACTS,{recursive:true});
      const root=mkdtempSync(join(process.env.OPENCAM_OBS_TEST_ARTIFACTS,'generated-'));
      writeFileSync(join(root,'capture.mkv'),result.bytes);
      writeFileSync(join(root,'events.json'),JSON.stringify({exit,events},null,2));
      writeFileSync(join(root,'helper.log'),result.log);
    }
    return result;
  }
  finally {clearTimeout(stop);clearTimeout(deadline);}
}

test('invalid request never emits media',async()=>{
  const result=await record({...request,width:0},{seconds:0.2});
  assert.equal(result.code,2,result.log); assert.equal(result.bytes.length,0);
  assert.ok(result.events.some(e=>e.event==='error'&&e.code==='invalid_request'),result.log);
});

test('source failure cannot keep encoding a stale texture as a healthy recording',async()=>{
  const result=await record(request,{seconds:4,env:{OPENCAM_OBS_TEST_SOURCE_FAILURE_MS:'1200'}});
  assert.notEqual(result.code,0,result.log);
  assert.ok(result.events.some(e=>e.event==='ready'),result.log);
  assert.ok(result.events.some(e=>e.event==='error'&&e.code==='capture_source_lost'),result.log);
  assert.ok(result.bytes.length>1024);
});

test('source failure before startup emits no black placeholder media',async()=>{
  const result=await record(request,{seconds:2,env:{OPENCAM_OBS_TEST_SOURCE_FAILURE_MS:'0'}});
  assert.notEqual(result.code,0,result.log);assert.equal(result.bytes.length,0);
  const failure=result.events.find(e=>e.event==='error'&&e.code==='capture_source');
  assert.ok(failure,result.log);
  assert.equal(failure.startup?.stage,'source_wait');
  assert.equal(failure.startup?.frames,0);
  assert.equal(failure.startup?.bytes,0);
  assert.equal(failure.startup?.expectedWidth,320);
  assert.equal(failure.startup?.expectedHeight,180);
});

test('actual libobs produces changing decodable H264/AAC with preserved clock',async()=>{
  const seconds=Number(process.env.OPENCAM_OBS_SYNTHETIC_SECONDS??4);
  assert.ok(Number.isFinite(seconds)&&seconds>=4&&seconds<=120);
  const result=await record(request,{seconds});
  assert.equal(result.code,0,`${result.signal ?? ''}\n${result.log}`);
  assert.ok(result.events.some(e=>e.event==='ready'&&e.backend==='synthetic'),result.log);
  assert.equal(result.events.at(-1)?.event,'stopped',result.log);
  const root=mkdtempSync(join(tmpdir(),'opencam-obs-test-')); const path=join(root,'recording.mkv');
  writeFileSync(path,result.bytes);
  const probe=spawnSync(ffprobe,['-v','error','-show_streams','-show_packets','-of','json',path],{encoding:'utf8',maxBuffer:16*1024*1024});
  assert.equal(probe.status,0,`${probe.error??''} ${probe.signal??''} ${probe.stderr}`); const data=JSON.parse(probe.stdout);
  assert.equal(data.streams[0].codec_name,'h264'); assert.equal(data.streams[0].width,320);
  assert.equal(data.streams[0].height,180); assert.equal(data.streams[1].codec_name,'aac');
  const video=data.packets.filter(p=>p.codec_type==='video'); const audio=data.packets.filter(p=>p.codec_type==='audio');
  assert.ok(video.length>=(seconds-1.5)*request.fps,`${video.length} video packets in ${seconds}s`);
  const finalProgress=result.events.filter(e=>e.event==='progress').at(-1);
  const ready=result.events.find(e=>e.event==='ready');
  assert.equal(ready.startup?.stage,'first_frame');
  assert.equal(ready.startup?.systemSamples,true);
  assert.equal(ready.startup?.sourceWidth,320);
  assert.ok(ready.startup?.frames>0);
  assert.ok(finalProgress.renderFrames>0);
  assert.ok(finalProgress.laggedFrames>=0&&finalProgress.laggedFrames<=finalProgress.renderFrames);
  assert.ok(finalProgress.averageRenderNs>=0);
  assert.equal(finalProgress?.frames,video.length,'Final segment counters must include every encoded frame');
  // Matroska may shift every stream by AAC encoder priming. The video
  // duration is its full span, not the muxer's common starting offset.
  const videoEnd=Number(video.at(-1).pts_time)+Number(video.at(-1).duration_time)-Number(video[0].pts_time);
  assert.ok(Math.abs(finalProgress.mediaUs/1e6-videoEnd)<0.002,
    `Final media duration must include the last frame: ${finalProgress.mediaUs/1e6} vs ${videoEnd}`);
  assert.ok(Math.abs(Number(video[0].pts_time)-Number(audio[0].pts_time))<0.12);
  assert.ok(Math.abs(Number(video.at(-1).pts_time)-Number(audio.at(-1).pts_time))<0.12);
  const decode=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:v','-f','framemd5','-'],{encoding:'utf8'});
  assert.equal(decode.status,0,decode.stderr);
  const hashes=decode.stdout.split('\n').filter(s=>s&&!s.startsWith('#')).map(s=>s.split(',').at(-1).trim());
  assert.ok(new Set(hashes).size>=10,'No changing source motion');
  const gray=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:v','-vf','scale=1:1','-fps_mode','passthrough','-f','rawvideo','-pix_fmt','gray','-'],{maxBuffer:1024*1024});
  const pcm=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:a','-ac','1','-ar','48000','-f','s16le','-'],{maxBuffer:(seconds+2)*48000*2});
  assert.equal(gray.status,0,gray.stderr.toString()); assert.equal(pcm.status,0,pcm.stderr.toString());
  const flashes=[]; let wasBright=false;
  for(let i=0;i<gray.stdout.length;i++) {const bright=gray.stdout[i]>180;if(bright&&!wasBright)flashes.push(Number(video[i].pts_time));wasBright=bright;}
  const beeps=[]; let wasLoud=false;
  for(let start=0;start+960<=pcm.stdout.length;start+=960) {
    let square=0;for(let j=start;j<start+960;j+=2)square+=(pcm.stdout.readInt16LE(j)/32768)**2;
    const loud=Math.sqrt(square/480)>0.05;if(loud&&!wasLoud)beeps.push(start/960/100+Number(audio[0].pts_time));wasLoud=loud;
  }
  const measured=beeps.filter(t=>t>0.5&&t<seconds-1.2);
  assert.ok(measured.length>=Math.floor(seconds-2),`Missing generated sync beeps: ${beeps}`);
  for(const t of measured) assert.ok(flashes.some(f=>Math.abs(f-t)<0.10),`Content sync lost: beep=${t}, flashes=${flashes}`);
});

for(const audio of ['none','microphone','both','silence']) test(`audio mode ${audio} retains source handshake`,async()=>{
  const result=await record({...request,audio},{seconds:2});
  assert.equal(result.code,0,result.log);assert.ok(result.events.some(e=>e.event==='ready'),result.log);
  const root=mkdtempSync(join(tmpdir(),'opencam-obs-audio-')); const path=join(root,'recording.mkv');writeFileSync(path,result.bytes);
  const probe=spawnSync(ffprobe,['-v','error','-show_streams','-of','json',path],{encoding:'utf8'});
  assert.equal(probe.status,0,probe.stderr);
  const streams=JSON.parse(probe.stdout).streams;
  assert.equal(streams.filter(s=>s.codec_type==='video').length,1);
  assert.equal(streams.filter(s=>s.codec_type==='audio').length,audio==='none'?0:1);
  if(audio==='silence') {
    const pcm=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:a','-f','s16le','-'],{maxBuffer:2*1024*1024});
    assert.equal(pcm.status,0,pcm.stderr.toString());assert.ok(pcm.stdout.length>48000);
    assert.ok(pcm.stdout.every(byte=>byte===0),'Recovery silence must not contain generated beeps');
  }
});

for(const fps of [20,30]) test(`CPU encoder records full-HD ${fps} FPS without hardware video encoding`,async()=>{
  const result=await record({...request,width:1920,height:1080,monitorWidth:1920,monitorHeight:1080,fps,encoder:'cpu'}, {seconds:6});
  assert.equal(result.code,0,result.log);
  assert.equal(result.events.find(e=>e.event==='ready')?.encoder,'cpu');
  const progress=result.events.filter(e=>e.event==='progress').at(-1);
  assert.ok(progress?.frames>=fps*4,`Insufficient full-HD CPU output: ${JSON.stringify(progress)}`);
  const root=mkdtempSync(join(tmpdir(),'opencam-obs-cpu-')),path=join(root,'recording.mkv');
  writeFileSync(path,result.bytes);
  const decode=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:v:0','-map','0:a:0','-f','null','-'],{encoding:'utf8'});
  assert.equal(decode.status,0,decode.stderr);assert.equal(decode.stderr,'');
  // Keep measured lag visible even when a hosted runner is temporarily busy.
  // Generated content does not establish physical desktop/YouTube performance.
  console.log(JSON.stringify({fullHdCpu:{fps,...progress,lagRatio:progress.laggedFrames/Math.max(1,progress.renderFrames)}}));
});

test('Apple hardware encoder preserves a decodable synthetic video', {skip:process.platform!=='darwin'},async()=>{
  const result=await record({...request,encoder:'apple'},{seconds:3});
  assert.equal(result.code,0,result.log);
  assert.ok(result.events.some(e=>e.event==='ready'&&e.encoder==='apple'),result.log);
  const root=mkdtempSync(join(tmpdir(),'opencam-obs-apple-'));const path=join(root,'recording.mkv');writeFileSync(path,result.bytes);
  const decoded=spawnSync(ffmpeg,['-v','error','-i',path,'-map','0:v','-f','framemd5','-'],{encoding:'utf8'});
  assert.equal(decoded.status,0,decoded.stderr);
  const frames=decoded.stdout.split('\n').filter(line=>line&&!line.startsWith('#'));
  assert.ok(frames.length>30);assert.ok(new Set(frames.map(line=>line.split(',').at(-1).trim())).size>10);
});

test('forced helper termination preserves already written decodable MKV',async()=>{
  const result=await record(request,{seconds:3,kill:true});
  assert.ok(result.bytes.length>1024,result.log);
  const root=mkdtempSync(join(tmpdir(),'opencam-obs-kill-')); const path=join(root,'partial.mkv'); writeFileSync(path,result.bytes);
  const decode=spawnSync(ffmpeg,['-v','warning','-i',path,'-map','0:v','-frames:v','1','-f','rawvideo','-pix_fmt','rgb24','-'],{maxBuffer:1024*1024});
  assert.equal(decode.status,0,decode.stderr.toString()); assert.equal(decode.stdout.length,320*180*3);
});
