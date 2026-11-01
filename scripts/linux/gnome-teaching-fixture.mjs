// SPDX-License-Identifier: AGPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';
import {spawn,spawnSync} from 'node:child_process';
import {inspectPointerFrames} from './gnome-teaching-pixels.mjs';
if(process.env.OPENCAM_DISPOSABLE_CI_DESKTOP!=='1')process.exit(2);
const [app,privateDir,evidence]=process.argv.slice(2),runtime=path.join(app,'obs-runtime');
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const report={status:'FAIL',scope:'real virtual GNOME 46 Wayland/OBS capture; NOT physical desktop acceptance',checks:{}};
function control(method,argument){
    const result=spawnSync('gdbus',['call','--session','--dest','org.gnome.Shell','--object-path','/io/github/kaoshou/OpenCam/TeachingFixture',
        '--method','io.github.kaoshou.OpenCam.TeachingFixture.'+method,...(argument?[argument]:[])],{encoding:'utf8',timeout:5000});
    if(result.status!==0)throw new Error('extension_unavailable');
    const match=result.stdout.trim().match(/^\('(.+)',\)$/);
    if(!match)throw new Error('fixture_protocol');
    const value=JSON.parse(match[1]);if(value.error)throw new Error(value.error);return value;
}
function runMedia(args,maxBuffer=128*1024*1024){
    const result=spawnSync(path.join(app,'ffmpeg'),args,{maxBuffer,timeout:30000});
    if(result.status!==0)throw new Error('media_decode');return result.stdout;
}
async function prepare(){
    const child=spawn(path.join(runtime,'bin/OpenCam.ObsCapture'),['--runtime',runtime,'--prepare-wayland'],{stdio:['pipe','pipe','pipe']});
    let events='',pixels=[],bytes=0;
    child.stdout.on('data',data=>{bytes+=data.length;if(bytes>4*1024*1024)child.kill('SIGKILL');else pixels.push(data);});
    child.stderr.on('data',data=>{if(events.length<1024*1024)events+=data.toString();else child.kill('SIGKILL');});
    const exit=new Promise(resolve=>child.once('close',resolve));
    const timeout=setTimeout(()=>child.kill('SIGKILL'),40000);
    await delay(2500);
    const chooser=spawnSync('python3',['scripts/linux/gnome-teaching-chooser.py'],{timeout:12000,encoding:'utf8'});
    report.chooserExitStatus=chooser.status;
    fs.writeFileSync(path.join(evidence,'chooser-controls.txt'),(chooser.stdout??'')+(chooser.stderr??''));
    for(let i=0;i<100&&!events.includes('"event":"selected"');i++)await delay(100);
    child.stdin.end('\n');await exit;clearTimeout(timeout);
    const selection=events.split('\n').filter(line=>line.startsWith('OCPORTAL ')).map(line=>JSON.parse(line.slice(9))).find(e=>e.event==='selected');
    if(!selection){
        report.prepareEvents=events.split('\n').filter(line=>line.startsWith('OCPORTAL ')).map(line=>{
            try{const e=JSON.parse(line.slice(9));return {event:/^[a-z_]{1,64}$/.test(e.event)?e.event:'unknown',
                code:/^[a-z_]{1,64}$/.test(e.code)?e.code:undefined};}catch{return {event:'malformed'};}
        });
        throw new Error('selection_failed');
    }
    return {selection,pixels:Buffer.concat(pixels)};
}
async function record(selection,phase,disable=false){
    control('Phase',phase);await delay(400);
    const file=path.join(evidence,`${phase}${disable?'-disable':''}.mkv`),out=fs.createWriteStream(file,{flags:'wx'});
    const child=spawn(path.join(runtime,'bin/OpenCam.ObsCapture'),['--runtime',runtime],{stdio:['pipe','pipe','pipe']});
    child.stdout.pipe(out);
    let pending='',grant,ready=false,stopped=false,error=null,timer,bytes=0,beforeDisable=0,disabled=false;
    child.stdout.on('data',data=>bytes+=data.length);
    const exit=new Promise(resolve=>child.once('close',resolve));
    const timeout=setTimeout(()=>{error='capture_timeout';child.kill('SIGKILL');},30000);
    child.stderr.on('data',data=>{
        pending+=data.toString();if(pending.length>256*1024){error='diagnostic_limit';child.kill('SIGKILL');return;}
        const lines=pending.split('\n');pending=lines.pop();
        for(const line of lines){
            if(!line.startsWith('OCOBS '))continue;
            const event=JSON.parse(line.slice(6));
            if(event.event==='portal_grant')grant=event;
            if(event.event==='error'){error='native_capture_failed';child.stdin.end('\n');}
            if(event.event==='stopped')stopped=true;
            if(event.event==='ready'){
                ready=true;
                timer=setTimeout(()=>{
                    if(disable){
                        beforeDisable=bytes;
                        disabled=spawnSync('gnome-extensions',['disable','teaching-tools@kaoshou.github.io'],{timeout:5000}).status===0;
                    }
                    timer=setTimeout(()=>child.stdin.end('\n'),2200);
                },2200);
            }
        }
    });
    child.stdin.on('error',()=>{});
    child.stdin.write(JSON.stringify({protocol:1,synthetic:false,fps:20,width:selection.width&~1,height:selection.height&~1,
        x:0,y:0,monitor:selection.sourceId,monitorWidth:selection.width,monitorHeight:selection.height,cropX:0,cropY:0,
        audio:'none',microphone:'',systemDevice:'',cursor:true,encoder:'cpu',quality:23,
        portalSourceId:selection.sourceId,portalRestoreToken:selection.restoreToken})+'\n');
    const status=await exit;clearTimeout(timeout);clearTimeout(timer);
    if(!out.closed)await new Promise(resolve=>out.once('close',resolve));
    if(error||status!==0||!ready||!stopped||!grant||grant.sourceId!==selection.sourceId)throw new Error(error||'capture_failed');
    runMedia(['-v','error','-i',file,'-f','null','-']);
    const frames=runMedia(['-v','error','-i',file,'-vf','fps=5','-pix_fmt','rgb24','-f','rawvideo','-']);
    report.checks[`${phase}${disable?'-disable':''}`]={bytes,disabled,grewAfterDisable:disable?disabled&&bytes>beforeDisable:false,
        ...inspectPointerFrames(frames,{width:selection.width,height:selection.height,art,artWidth:140,artHeight:120,
            hotspotX:16,hotspotY:16,positions:[[180,150],[240,150],[300,150]]})};
    return grant;
}
const art=runMedia(['-v','error','-i','src/ScreenRecorder.Core/Assets/TeachingPointers/WarmIvory.png','-vf','scale=140:120','-pix_fmt','rgba','-f','rawvideo','-']);
try{
    const inert=control('Status');
    if(inert.phase!=='inert'||!inert.visible||inert.monitors.length!==2)throw new Error('inert_or_monitor_check');
    const markerState=control('Phase','markers');await delay(800);
    let {selection,pixels}=await prepare();
    if(pixels.length!==selection.previewWidth*selection.previewHeight*4)throw new Error('preview_invalid');
    let observed='';
    for(let digit=0;digit<16;digit++){
        let value=0;
        for(let bit=0;bit<4;bit++){
            const n=digit*4+bit,x=Math.floor((64+(n%16)*12+6)*selection.previewWidth/selection.width),
                y=Math.floor((100+Math.floor(n/16)*12+6)*selection.previewHeight/selection.height),i=(y*selection.previewWidth+x)*4;
            const green=pixels[i+1]>150&&pixels[i+2]<90,blue=pixels[i+2]>150&&pixels[i+1]<90;
            if(!green&&!blue)throw new Error('marker_not_captured');
            if(green)value|=1<<bit;
        }
        observed+=value.toString(16);
    }
    const matches=markerState.markers.filter(nonce=>nonce===observed).length;
    if(matches!==1)throw new Error('monitor_mapping_ambiguous');
    report.checks.monitorMapping={identicalMonitors:2,uniqueVisualChallengeMatches:matches};
    selection=await record(selection,'baseline');
    selection=await record(selection,'pointer');
    selection=await record(selection,'stroke');
    selection=await record(selection,'cleanup');
    if(!control('Status').visible)throw new Error('cursor_not_restored');
    await record(selection,'pointer',true);
    for(const phase of ['baseline','cleanup'])if(report.checks[phase].magentaPixels<100)throw new Error('native_cursor_not_verified');
    for(const phase of ['pointer','stroke']){
        const value=report.checks[phase];
        if(value.magentaPixels!==0||value.positions.length<2)throw new Error('pointer_replacement_failed');
    }
    if(report.checks.stroke.redPixels<100)throw new Error('stroke_not_captured');
    if(!report.checks['pointer-disable'].grewAfterDisable||report.checks['pointer-disable'].magentaPixels<100)throw new Error('disable_recovery_failed');
    report.status='PASS';
}catch(error){
    const safe=['extension_unavailable','fixture_protocol','pointer_operation_failed','media_decode','selection_failed','capture_timeout',
        'diagnostic_limit','native_capture_failed','capture_failed','inert_or_monitor_check','preview_invalid','marker_not_captured',
        'monitor_mapping_ambiguous','cursor_not_restored','native_cursor_not_verified','pointer_replacement_failed','stroke_not_captured','disable_recovery_failed'];
    report.code=safe.includes(error.message)?error.message:'fixture_failed';
}
fs.writeFileSync(path.join(evidence,'teaching-result.json'),JSON.stringify(report,null,2));
if(report.status!=='PASS')process.exit(1);
