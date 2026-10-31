// SPDX-License-Identifier: AGPL-3.0-or-later
// Actual owned GNOME/PipeWire integration; never fabricate portal replies.
import fs from 'node:fs';
import path from 'node:path';
import {spawn,spawnSync} from 'node:child_process';
if(process.env.OPENCAM_DISPOSABLE_CI_DESKTOP!=='1') process.exit(2);
const [runtime,events,preview,evidence,media]=process.argv.slice(2);
const report={status:'FAIL',scope:'actual virtual GNOME portal; not physical desktop/audio acceptance',segments:[]};
function decode(file,fps){
  const probe=spawnSync(path.join(media,'ffprobe'),['-v','error','-show_streams','-show_packets','-of','json',file],{encoding:'utf8',maxBuffer:8*1024*1024,timeout:20000});
  if(probe.status!==0)throw new Error('media_probe_failed');
  const data=JSON.parse(probe.stdout);
  if(data.streams.length!==1||data.streams[0].codec_type!=='video'||data.packets.length<fps*2)throw new Error('media_coverage_failed');
  let last=-Infinity;
  for(const p of data.packets){const dts=Number(p.dts_time);if(!Number.isFinite(dts)||dts<last)throw new Error('media_timing_failed');last=dts;}
  const result=spawnSync(path.join(media,'ffmpeg'),['-v','error','-i',file,'-f','null','-'],{encoding:'utf8',maxBuffer:1024*1024,timeout:20000});
  if(result.status!==0||result.stderr.trim())throw new Error('media_decode_failed');
  return {packets:data.packets.length,width:data.streams[0].width,height:data.streams[0].height,decode:'PASS'};
}
async function segment(selection,index){
  const file=path.join(evidence,`segment-${index}.mkv`),out=fs.createWriteStream(file,{flags:'wx'});
  const child=spawn(path.join(runtime,'bin/OpenCam.ObsCapture'),['--runtime',runtime],{stdio:['pipe','pipe','pipe']});
  child.stdout.pipe(out);
  let pending='',size=0,grant,ready=false,stopped=false,code;
  const timeout=setTimeout(()=>{code='capture_timeout';child.kill('SIGKILL');},65000);
  let stop;
  child.stderr.on('data',chunk=>{
    size+=chunk.length;if(size>256*1024){code='diagnostic_limit';child.kill('SIGKILL');return;}
    pending+=chunk.toString('utf8');
    const lines=pending.split('\n');pending=lines.pop();
    for(const line of lines){
      if(!line.startsWith('OCOBS '))continue;
      try{
        const event=JSON.parse(line.slice(6));
        if(event.event==='portal_grant'){
          if(grant||ready||event.backend!=='pipewire'||event.sourceId!==selection.sourceId||event.width!==selection.width||event.height!==selection.height||!event.restoreToken)throw new Error();
          grant=event;
        }else if(event.event==='ready'){
          if(!grant||ready||event.backend!=='pipewire'||event.monitor!==selection.sourceId)throw new Error();
          ready=true;stop=setTimeout(()=>child.stdin.write('\n'),3000);
        }else if(event.event==='stopped')stopped=true;
        else if(event.event==='error'){code=/^[a-z_]{1,64}$/.test(event.code)?event.code:'native_error';child.stdin.write('\n');}
      }catch{code='private_protocol_invalid';child.kill('SIGKILL');}
    }
  });
  child.stdin.on('error',()=>{});
  child.stdin.write(JSON.stringify({protocol:1,synthetic:false,fps:20,width:selection.width&~1,height:selection.height&~1,
    x:0,y:0,monitor:selection.sourceId,monitorWidth:selection.width,monitorHeight:selection.height,cropX:0,cropY:0,
    audio:'none',microphone:'',systemDevice:'',cursor:true,encoder:'cpu',quality:23,
    portalSourceId:selection.sourceId,portalRestoreToken:selection.restoreToken})+'\n');
  const status=await new Promise((resolve,reject)=>{child.once('error',()=>reject(new Error('helper_spawn_failed')));child.once('close',resolve);});
  clearTimeout(timeout);clearTimeout(stop);
  await new Promise((resolve,reject)=>{if(out.closed)resolve();else{out.once('close',resolve);out.once('error',()=>reject(new Error('media_write_failed')));}});
  if(code||status!==0||!ready||!stopped||!grant)throw new Error(code||'capture_failed');
  report.segments.push({index,...decode(file,20)});
  return grant;
}
try{
  const parsed=fs.readFileSync(events,'utf8').split('\n').filter(l=>l.startsWith('OCPORTAL ')).map(l=>JSON.parse(l.slice(9)));
  const selected=parsed.find(e=>e.event==='selected');
  if(!selected)throw new Error('selection_failed');
  report.width=selected.width;report.height=selected.height;report.previewBytes=fs.statSync(preview).size;
  if(report.previewBytes!==selected.previewWidth*selected.previewHeight*4)throw new Error('preview_failed');
  const restored=await segment(selected,1);
  await segment(restored,2);
  report.status='PASS';
}catch(error){
  const safe=['media_probe_failed','media_coverage_failed','media_timing_failed','media_decode_failed','capture_timeout','diagnostic_limit','private_protocol_invalid','helper_spawn_failed','media_write_failed','capture_failed','selection_failed','preview_failed','portal_source_changed','portal_identity_unavailable','portal_restore_unavailable','source_timeout','portal_cancelled'];
  report.code=safe.includes(error.message)?error.message:'fixture_failed';
}
fs.writeFileSync(path.join(evidence,'result.json'),JSON.stringify(report,null,2));
if(report.status!=='PASS')process.exit(1);
