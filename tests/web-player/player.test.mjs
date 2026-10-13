import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const html = readFileSync(new URL('../../src/ScreenRecorder.Recorder/WebPlayer/player.html', import.meta.url), 'utf8');
const runtime = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)].at(-1)[1];
class Element {
  children=[]; listeners={}; attributes={}; textContent=''; hidden=true;
  value=''; classList={values:new Set(),add(name){this.values.add(name)},remove(name){this.values.delete(name)},
    contains(name){return this.values.has(name)},toggle(name,force){const next=force??!this.values.has(name);if(next)this.values.add(name);else this.values.delete(name);return next}};
  append(...items){this.children.push(...items)}
  setAttribute(name,value){this.attributes[name]=value}
  contains(item){return item===this||this.children.some(child=>child.contains(item))}
  addEventListener(name,handler){(this.listeners[name]??=[]).push(handler)}
  dispatch(name,event={}){this.listeners[name]?.forEach(handler=>handler(event))}
  set innerHTML(value){throw new Error('Player content must not inject HTML')}
}
function setup(data = {format:'opencam.web-player',schemaVersion:1,title:'教材 <img onerror=alert(1)>',
  chapters:[{title:'介紹',start:0,end:4},{title:'示範 </script>',start:4,end:8},{title:'整理',start:8,end:12}]}){
  const ids = Object.fromEntries(['video','error','title','summary','chapters','now','speed','stage','layout','controls',
    'seek','play','mute','volume','current-time','total-time','wide','fullscreen'].map(id=>[id,new Element()]));
  Object.assign(ids.video,{controls:true,readyState:1,currentTime:0,duration:12,playbackRate:1,paused:true,ended:false,volume:1,muted:false,
    play(){this.playCalled=true;this.paused=false;this.dispatch('play');return Promise.resolve()},
    pause(){this.paused=true;this.dispatch('pause')}});
  ids.controls.append(ids.seek,ids.play,ids.mute,ids.volume,ids.speed,ids.wide,ids.fullscreen);
  const document={getElementById:id=>ids[id],createElement:()=>new Element(),body:new Element(),listeners:{},activeElement:null,fullscreenElement:null,
    addEventListener(name,handler){this.listeners[name]=handler},dispatch(name,event={}){this.listeners[name]?.(event)},
    exitFullscreen(){this.fullscreenElement=null;this.dispatch('fullscreenchange');return Promise.resolve()}};
  ids.stage.requestFullscreen=()=>{document.fullscreenElement=ids.stage;document.dispatch('fullscreenchange');return Promise.resolve()};
  const timers=new Map();let timerId=0;
  ids.runTimers=()=>{const pending=[...timers.values()];timers.clear();pending.forEach(fn=>fn())};ids.document=document;
  let ready;
  const context={window:{OpenCamPlayerData:data,addEventListener(name,handler){if(name==='DOMContentLoaded')ready=handler},
    setTimeout(fn){timers.set(++timerId,fn);return timerId},clearTimeout(id){timers.delete(id)}},document};
  vm.runInNewContext(runtime,context);ready();return ids;
}
test('chapter click seeks and highlights exactly one chapter without interpreting titles as HTML',()=>{
  const ids=setup();const buttons=ids.chapters.children;
  assert.equal(ids.title.textContent,'教材 <img onerror=alert(1)>');
  buttons[1].dispatch('click');
  assert.equal(ids.video.currentTime,4);assert.equal(ids.video.playCalled,true);
  assert.equal(ids.now.textContent,'示範 </script>');
  assert.deepEqual(buttons.map(b=>b.attributes['aria-current']),['false','true','false']);
});
test('seek before metadata loading is applied when metadata becomes ready',()=>{
  const ids=setup();ids.video.readyState=0;ids.chapters.children[2].dispatch('click');
  assert.equal(ids.video.currentTime,0);ids.video.readyState=1;ids.video.dispatch('loadedmetadata');
  assert.equal(ids.video.currentTime,8);assert.equal(ids.now.textContent,'整理');
});
test('manual seeking and changing speed update the actual video',()=>{
  const ids=setup();ids.video.currentTime=9;ids.video.dispatch('timeupdate');assert.equal(ids.now.textContent,'整理');
  ids.video.currentTime=2;ids.video.dispatch('seeked');assert.equal(ids.now.textContent,'介紹');
  ids.speed.value='1.5';ids.speed.dispatch('change');assert.equal(ids.video.playbackRate,1.5);
});
test('missing or unsupported data gives a visible error rather than crashing',()=>{
  for(const data of [null,{format:'other',schemaVersion:1,chapters:[]},{format:'opencam.web-player',schemaVersion:99,chapters:[]}]){
    const ids=setup(data);assert.equal(ids.error.hidden,false);assert.ok(ids.error.textContent.length>0);assert.equal(ids.video.controls,true);
  }
});
test('chapter rows show start time and their own duration, not the end timestamp',()=>{
  const ids=setup({format:'opencam.web-player',schemaVersion:1,title:'Timing',chapters:[
    {title:'Intro',start:0,end:231},
    {title:'Demo',start:231,end:397},
    {title:'Long lesson',start:3661,end:7323},
    {title:'Short clip',start:7323,end:7323.5}
  ]});
  const rows=ids.chapters.children.map(button=>button.children[1].children[1]);
  assert.deepEqual(rows.map(row=>row.children.map(item=>item.textContent)),[
    ['▶ 0:00','◷ 3:51'],['▶ 3:51','◷ 2:46'],['▶ 1:01:01','◷ 1:01:02'],['▶ 2:02:03','◷ <0:01']
  ]);
  ids.chapters.children[1].dispatch('click');
  assert.equal(ids.video.currentTime,231);
});
test('a large chapter index preserves every clickable chapter',()=>{
  const ids=setup({format:'opencam.web-player',schemaVersion:1,title:'Many chapters',
    chapters:Array.from({length:150},(_,i)=>({title:'Chapter '+i,start:i*4,end:(i+1)*4}))});
  assert.equal(ids.chapters.children.length,150);
  ids.chapters.children[149].dispatch('click');
  assert.equal(ids.video.currentTime,596);
  assert.equal(ids.now.textContent,'Chapter 149');
});
test('overlay controls play, seek, mute, change volume and synchronize media state',()=>{
  const ids=setup();ids.play.dispatch('click');assert.equal(ids.video.paused,false);
  ids.play.dispatch('click');assert.equal(ids.video.paused,true);
  ids.seek.value='75';ids.seek.dispatch('input');assert.equal(ids.video.currentTime,9);
  ids.video.dispatch('timeupdate');assert.equal(ids['current-time'].textContent,'0:09');assert.equal(ids.seek.value,'75');
  ids.mute.dispatch('click');assert.equal(ids.video.muted,true);
  ids.volume.value='0.35';ids.volume.dispatch('input');assert.equal(ids.video.volume,0.35);assert.equal(ids.video.muted,false);
  ids.video.muted=true;ids.video.dispatch('volumechange');assert.equal(ids.mute.attributes['aria-pressed'],'true');
  ids.video.duration=NaN;ids.seek.value='50';ids.seek.dispatch('input');assert.equal(ids.video.currentTime,9);
});
test('wide mode and full screen operate on the stage so controls remain available',async()=>{
  const ids=setup();ids.wide.dispatch('click');assert.equal(ids.layout.classList.contains('wide'),true);
  ids.wide.dispatch('click');assert.equal(ids.layout.classList.contains('wide'),false);
  ids.fullscreen.dispatch('click');assert.equal(ids.document.fullscreenElement,ids.stage);
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(ids.fullscreen.attributes['aria-pressed'],'true');
  ids.seek.value='25';ids.seek.dispatch('input');assert.equal(ids.video.currentTime,3);
  ids.fullscreen.dispatch('click');assert.equal(ids.document.fullscreenElement,null);
  await new Promise(resolve=>setImmediate(resolve));
  ids.stage.requestFullscreen=()=>Promise.reject(new Error('Unsupported'));
  ids.fullscreen.dispatch('click');await new Promise(resolve=>setImmediate(resolve));
  assert.equal(ids.stage.classList.contains('frame-fullscreen'),true);
  ids.document.dispatch('keydown',{key:'Escape'});assert.equal(ids.stage.classList.contains('frame-fullscreen'),false);
});
test('overlay fades only during playback and stays visible for pause or keyboard focus',()=>{
  const ids=setup();ids.play.dispatch('click');ids.runTimers();assert.equal(ids.stage.classList.contains('controls-hidden'),true);
  ids.stage.dispatch('pointermove');assert.equal(ids.stage.classList.contains('controls-hidden'),false);
  ids.document.activeElement=ids.seek;ids.runTimers();assert.equal(ids.stage.classList.contains('controls-hidden'),false);
  ids.document.activeElement=null;ids.stage.dispatch('pointermove');ids.runTimers();
  ids.video.pause();assert.equal(ids.stage.classList.contains('controls-hidden'),false);
});
test('stage keyboard seeking stays within the media and does not steal slider keys',()=>{
  const ids=setup();let prevented=false;
  ids.stage.dispatch('keydown',{target:ids.stage,key:' ',preventDefault(){prevented=true}});
  assert.equal(prevented,true);assert.equal(ids.video.paused,false);assert.equal(ids.video.controls,false);
  ids.video.currentTime=10;
  ids.stage.dispatch('keydown',{target:ids.stage,key:'ArrowRight',preventDefault(){}});assert.equal(ids.video.currentTime,12);
  ids.video.currentTime=2;
  ids.stage.dispatch('keydown',{target:ids.stage,key:'ArrowLeft',preventDefault(){}});assert.equal(ids.video.currentTime,0);
  ids.stage.dispatch('keydown',{target:ids.seek,key:'ArrowRight',preventDefault(){throw Error('Do not steal range input keys')}});
  assert.equal(ids.video.currentTime,0);
});
test('a rejected play action shows an error and leaves controls visible',async()=>{
  const ids=setup();ids.video.play=()=>Promise.reject(new Error('Media failed'));
  ids.play.dispatch('click');await new Promise(resolve=>setImmediate(resolve));
  assert.equal(ids.error.hidden,false);assert.equal(ids.stage.classList.contains('controls-hidden'),false);
});
test('rapid full screen clicks issue only one pending request',async()=>{
  const ids=setup();let requests=0,finish;
  ids.stage.requestFullscreen=()=>{requests++;return new Promise(resolve=>{finish=resolve})};
  ids.fullscreen.dispatch('click');ids.fullscreen.dispatch('click');assert.equal(requests,1);
  ids.document.fullscreenElement=ids.stage;ids.document.dispatch('fullscreenchange');finish();
  await new Promise(resolve=>setImmediate(resolve));
  ids.fullscreen.dispatch('click');assert.equal(ids.document.fullscreenElement,null);
  assert.equal(ids.stage.classList.contains('frame-fullscreen'),false);
});
