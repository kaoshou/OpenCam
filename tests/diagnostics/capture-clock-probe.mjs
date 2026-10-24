// SPDX-License-Identifier: AGPL-3.0-or-later
// Controlled diagnostic, not a desktop capture or a production clock mapper.
// The producer records when it supplies synthetic frames, independently of FFmpeg progress.
import {spawn,execFileSync} from 'node:child_process';
import {createInterface} from 'node:readline';
import {setTimeout as delay} from 'node:timers/promises';
import {dirname,join} from 'node:path';
import assert from 'node:assert/strict';
const ffmpeg=process.env.FFMPEG_PATH||'ffmpeg';
const ffprobe=process.env.FFPROBE_PATH||(ffmpeg.includes('/')?join(dirname(ffmpeg),'ffprobe'):'ffprobe');
const results=[];
for(const startupDelayMs of [0,350]){
 const start=performance.now(),frames=[],output=[];let length=0,firstProgress=null,delayedProgress=null,block={};
 const proc=spawn(ffmpeg,['-hide_banner','-loglevel','error','-nostats','-progress','pipe:2','-stats_period','0.1',
  '-probesize','32','-analyzeduration','0','-f','rawvideo','-pixel_format','rgba','-video_size','32x32','-framerate','30','-i','pipe:0',
  '-an','-c:v','libx264','-preset','ultrafast','-tune','zerolatency','-f','matroska','pipe:1'],{stdio:['pipe','pipe','pipe']});
 const exited=new Promise((resolve,reject)=>{proc.on('error',reject);proc.on('close',(code)=>code===0?resolve():reject(Error('ffmpeg exit '+code)));});
 proc.stdout.on('data',b=>{length+=b.length;if(length>2*1024*1024){proc.kill();return;}output.push(b);});
 const reader=createInterface({input:proc.stderr});
 reader.on('line',line=>{const at=line.indexOf('=');if(at<0)return;block[line.slice(0,at)]=line.slice(at+1);
  if(line.startsWith('progress=')){if(!firstProgress&&Number(block.frame)>0){firstProgress={...block,receivedAtMs:performance.now()-start};delayedProgress=delay(500).then(()=>performance.now()-start);}block={};}
 });
 try{
  await delay(startupDelayMs);
  for(let i=0;i<30;i++){
   const pixels=Buffer.alloc(32*32*4);for(let p=0;p<pixels.length;p+=4){pixels[p]=i*8;pixels[p+3]=255;}
   frames.push(performance.now()-start);
   await new Promise((resolve,reject)=>proc.stdin.write(pixels,e=>e?reject(e):resolve()));
   await delay(1000/30);
  }
  proc.stdin.end();await exited;
  assert(firstProgress,'positive progress observed');
  const probe=JSON.parse(execFileSync(ffprobe,['-v','error','-select_streams','v:0','-show_frames','-show_entries','frame=best_effort_timestamp_time','-of','json','-i','pipe:0'],{input:Buffer.concat(output),maxBuffer:2*1024*1024}));
  assert.equal(probe.frames.length,30);assert.equal(Number(probe.frames[0].best_effort_timestamp_time),0);
  // Two consumers receive exactly the same progress payload, but one is scheduled late.
  // Inferring the origin from receive time necessarily shifts by that delivery delay.
  const delayedReceiptMs=await delayedProgress;
  const mediaMs=Number(firstProgress.out_time_us)/1000;
  results.push({startupDelayMs,firstSuppliedFrameMs:frames[0],firstSourcePtsSeconds:0,
   firstProgressFrame:Number(firstProgress.frame),firstProgressMediaMs:mediaMs,firstProgressReceiptMs:firstProgress.receivedAtMs,
   originFromImmediateReceiptMs:firstProgress.receivedAtMs-mediaMs,originFromDelayedReceiptMs:delayedReceiptMs-mediaMs,
   deliveryOnlyOriginShiftMs:delayedReceiptMs-firstProgress.receivedAtMs,frameCount:probe.frames.length});
 }finally{reader.close();if(proc.exitCode===null)proc.kill();}
}
console.log(JSON.stringify({note:'Synthetic input supply time, not an OS acquisition timestamp. A second observer processes the same progress payload after a 500ms timer. No desktop captured.',results},null,2));
