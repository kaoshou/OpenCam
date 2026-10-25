import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const html = readFileSync(new URL('../../src/ScreenRecorder.Recorder/WebPlayer/player.html', import.meta.url), 'utf8');
const runtime = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)].at(-1)[1];
class Element {
  style={setProperty(name,value){this[name]=value}};dataset={};
  children=[]; listeners={}; attributes={}; textContent=''; hidden=true;
  value=''; classList={values:new Set(),add(name){this.values.add(name)},remove(name){this.values.delete(name)},
    contains(name){return this.values.has(name)},toggle(name,force){const next=force??!this.values.has(name);if(next)this.values.add(name);else this.values.delete(name);return next}};
  append(...items){this.children.push(...items)}
  replaceChildren(...items){this.children=[...items]}
  scrollIntoView(){this.scrolled=true}
  setAttribute(name,value){this.attributes[name]=value}
  contains(item){return item===this||this.children.some(child=>child.contains(item))}
  addEventListener(name,handler){(this.listeners[name]??=[]).push(handler)}
  dispatch(name,event={}){this.listeners[name]?.forEach(handler=>handler(event))}
  set innerHTML(value){throw new Error('Player content must not inject HTML')}
}
function setup(data = {format:'opencam.web-player',schemaVersion:1,title:'教材 <img onerror=alert(1)>',
  chapters:[{title:'介紹',start:0,end:4},{title:'示範 </script>',start:4,end:8},{title:'整理',start:8,end:12}]},saved={},browserLanguage){
  const ids = Object.fromEntries(['video','error','title','summary','chapters','now','speed','stage','layout','controls',
    'seek','play','mute','volume','current-time','total-time','wide','fullscreen',
    'caption-button','caption-menu','caption-language','caption-overlay','caption-text','caption-size','caption-size-value',
    'caption-theme','caption-opacity','caption-position','caption-reset','chapter-tab','transcript-tab','chapter-panel',
    'transcript-panel','transcript-language','transcript-search','transcript-list','transcript-status','transcript-more','transcript-follow','sidebar','player',
    'reading-button','reading-menu','reading-close','reading-size','reading-size-value','reading-spacing','reading-theme','reading-reset','reading-timestamps','download-html','download-txt','download-status','caption-close','skip-transcript','skip-player','skip-chapters','transcript-key-help','localized-credit'].map(id=>[id,new Element()]));
  ids.player.getBoundingClientRect=()=>({height:480});
  ids['ui-language']=new Element();ids['ui-language-control']=new Element();
  Object.assign(ids.video,{controls:true,readyState:1,currentTime:0,duration:12,playbackRate:1,paused:true,ended:false,volume:1,muted:false,
    play(){this.playCalled=true;this.paused=false;this.dispatch('play');return Promise.resolve()},
    pause(){this.paused=true;this.dispatch('pause')}});
  ids.controls.append(ids.seek,ids.play,ids.mute,ids.volume,ids.speed,ids.wide,ids.fullscreen);
  ids.controls.append(ids['caption-button'],ids['caption-menu']);
  ids['caption-menu'].append(ids['caption-language'],ids['caption-size'],ids['caption-theme'],ids['caption-opacity'],ids['caption-position'],ids['caption-reset']);
  const document={getElementById:id=>ids[id],createElement:()=>new Element(),body:new Element(),documentElement:new Element(),listeners:{},activeElement:null,fullscreenElement:null,
    addEventListener(name,handler){(this.listeners[name]??=[]).push(handler)},dispatch(name,event={}){this.listeners[name]?.forEach(handler=>handler(event))},
    exitFullscreen(){this.fullscreenElement=null;this.dispatch('fullscreenchange');return Promise.resolve()}};
  const jumpLinks=[...html.matchAll(/<a\b([^>]*\bdata-jump="([^"]+)"[^>]*)>/g)].map(([,attributes,jump])=>{
    const id=/\bid="([^"]+)"/.exec(attributes)?.[1],el=ids[id]||new Element();el.dataset.jump=jump;return el;
  });
  document.querySelectorAll=selector=>selector==='[data-jump]'?jumpLinks:[];
  for(const el of Object.values(ids))el.focus=()=>{document.activeElement=el};
  ids.downloads=[];
  document.createElement=()=>{const el=new Element();el.click=()=>ids.downloads.push({name:el.download,blob:ids.blob});el.remove=()=>{};return el};
  ids.stage.requestFullscreen=()=>{document.fullscreenElement=ids.stage;document.dispatch('fullscreenchange');return Promise.resolve()};
  const timers=new Map();let timerId=0;
  ids.runTimers=()=>{const pending=[...timers.values()];timers.clear();pending.forEach(fn=>fn())};ids.document=document;
  let ready;
  const context={Blob,window:{navigator:{language:browserLanguage},innerHeight:800,OpenCamPlayerData:data,URL:{createObjectURL(blob){ids.blob=blob;return 'blob:test'},revokeObjectURL(){}},
    localStorage:{getItem:k=>saved[k]??null,setItem(k,v){saved[k]=v}},addEventListener(name,handler){if(name==='DOMContentLoaded')ready=handler},
    setTimeout(fn){timers.set(++timerId,fn);return timerId},clearTimeout(id){timers.delete(id)}},document};
  vm.runInNewContext(runtime,context);ready();return ids;
}
const subtitleLesson=()=>({format:'opencam.web-player',schemaVersion:1,title:'Subtitles',chapters:[{title:'Lesson',start:0,end:12}],
  subtitles:{defaultTrackId:'tw',tracks:[
    {id:'tw',language:'zh-TW',label:'繁體中文',cues:[{start:0,end:2,text:'教材 <img onerror=alert(1)>'},{start:1,end:3,text:'重疊'}]},
    {id:'en',language:'en',label:'English',cues:[{start:0,end:3,text:'Lesson'}]}
  ]}});
test('interface language resolves browser variants, configured defaults and restricted subsets',()=>{
  for(const [browser,want] of [['zh-CN','zh-TW'],['zh-HK','zh-TW'],['ja-JP','ja'],['en-US','en'],['fr-FR','en'],[undefined,'zh-TW']]){
    assert.equal(setup(subtitleLesson(),{},browser)['ui-language'].value,want);
  }
  const codes=['zh-TW','en','ja'];
  for(let mask=1;mask<8;mask++){
    const enabled=codes.filter((_,i)=>mask&(1<<i));
    for(const configured of ['auto',...codes]){
      const lesson=subtitleLesson();lesson.uiLanguages={enabledLanguages:enabled,defaultLanguage:configured};
      for(const remembered of [null,'obsolete',...codes]){
        const ids=setup(lesson,{'opencam.uiLanguage':remembered},'ja-JP');
        const preferred=enabled.includes(remembered)?remembered:enabled.includes(configured)?configured:'ja';
        const want=enabled.includes(preferred)?preferred:['en','zh-TW','ja'].find(x=>enabled.includes(x));
        assert.equal(ids['ui-language'].value,want);
        assert.equal(ids['ui-language-control'].hidden,enabled.length===1);
      }
    }
  }
});
test('manual interface switch is remembered without changing media, captions, search or reading state',()=>{
  const saved={},ids=setup(subtitleLesson(),saved,'en-US');
  ids.video.currentTime=1;ids.video.play();ids['transcript-search'].value='教材';
  ids['reading-size'].value='175';ids['reading-size'].dispatch('input');
  const before=ids['transcript-list'].children[0];
  ids['ui-language'].value='ja';ids['ui-language'].dispatch('change');
  assert.equal(ids.document.documentElement.lang,'ja');
  assert.equal(ids.play.attributes['aria-label'],'一時停止');
  assert.equal(ids.video.currentTime,1);assert.equal(ids.video.paused,false);
  assert.equal(ids['caption-language'].value,'tw');assert.equal(ids['transcript-language'].value,'tw');
  assert.equal(ids['transcript-search'].value,'教材');assert.equal(ids['reading-size'].value,'175');
  assert.equal(ids['transcript-list'].children[0],before);
  assert.equal(setup(subtitleLesson(),saved,'en-US')['ui-language'].value,'ja');
});
test('localized toggle names describe their current action',async()=>{
  const ids=setup(subtitleLesson(),{},'en-US');
  ids.wide.dispatch('click');
  assert.equal(ids.wide.attributes['aria-label'],'Restore layout');
  ids.fullscreen.dispatch('click');await Promise.resolve();
  assert.equal(ids.fullscreen.title,'Exit full screen');
});
test('denied storage keeps language selection functional',()=>{
  const denied=new Proxy({},{get(){throw Error('denied')},set(){throw Error('denied')}});
  const ids=setup(subtitleLesson(),denied,'zh-CN');
  assert.equal(ids['ui-language'].value,'zh-TW');
  ids['ui-language'].value='ja';ids['ui-language'].dispatch('change');
  assert.equal(ids.play.attributes['aria-label'],'再生');
});
test('caption text keeps its content language independently of interface language',()=>{
  const ids=setup(subtitleLesson(),{},'en-US');
  assert.equal(ids['caption-text'].attributes.lang,'zh-TW');
  ids['caption-language'].value='en';ids['caption-language'].dispatch('change');
  ids['ui-language'].value='ja';ids['ui-language'].dispatch('change');
  assert.equal(ids['caption-text'].attributes.lang,'en');
});
test('reading preferences affect only transcript text and reject malformed stored values',()=>{
  const data=subtitleLesson(),saved={'opencam.readingAppearance':'{"size":999,"spacing":0,"theme":"url(evil)"}'};
  const ids=setup(data,saved);
  assert.equal(ids['transcript-list'].style.fontSize,'100%');
  ids['reading-size'].value='175';ids['reading-size'].dispatch('input');
  ids['reading-spacing'].value='2';ids['reading-spacing'].dispatch('change');
  ids['reading-theme'].value='dark';ids['reading-theme'].dispatch('change');
  assert.equal(ids['transcript-list'].style.fontSize,'175%');assert.equal(ids['transcript-list'].style.lineHeight,'2');
  assert.equal(ids['transcript-list'].dataset.theme,'dark');assert.equal(ids['caption-text'].style.fontSize,'100%');
  const again=setup(data,saved);assert.equal(again['reading-size'].value,'175');
  ids['reading-reset'].dispatch('click');assert.equal(ids['transcript-list'].style.fontSize,'100%');
  assert.equal(ids['transcript-list'].dataset.theme,'default');
});
test('downloads contain the whole selected language despite search and paging and never execute imported HTML',async()=>{
  const data=subtitleLesson();data.title='教材 </title><script>bad()</script>';
  data.subtitles.tracks[0].cues=Array.from({length:1005},(_,i)=>({start:i,end:i+1,text:i===0?'講者：<img onerror=bad()>':'段落 '+i}));
  const ids=setup(data);ids['transcript-search'].value='段落 999';ids['transcript-search'].dispatch('input');ids.runTimers();
  assert.equal(ids['transcript-list'].children.length,1);
  ids['download-html'].dispatch('click');const html=await ids.downloads[0].blob.text();
  assert.ok(html.includes('段落 1004'));assert.ok(html.includes('&lt;img onerror=bad()&gt;'));
  assert.ok(html.includes('lang="zh-TW"'));assert.ok(html.includes('<h2>Lesson</h2>'));
  assert.ok(!html.includes('<script>'));assert.ok(!html.includes('class="timestamp"'));
  ids['reading-timestamps'].checked=true;ids['download-txt'].dispatch('click');
  const txt=await ids.downloads[1].blob.text();assert.ok(txt.includes('[16:44]'));assert.ok(txt.includes('段落 1004'));
  ids['transcript-language'].value='en';ids['transcript-language'].dispatch('change');ids['download-txt'].dispatch('click');
  const english=await ids.downloads[2].blob.text();assert.ok(english.includes('Lesson'));assert.ok(!english.includes('段落 1004'));
  assert.ok(ids.downloads.every(d=>!/[<>/\\]/.test(d.name)));
});
test('missing captions disable downloads rather than producing an empty transcript',()=>{
  const ids=setup();assert.equal(ids['download-html'].disabled,true);assert.equal(ids['download-txt'].disabled,true);
});
test('without usable subtitles transcript tab and quick entry stay hidden and cannot be selected',()=>{
  for(const subtitles of [undefined,{tracks:[]},{tracks:[{id:'invalid'}]}]){
    const lesson=subtitleLesson();lesson.subtitles=subtitles;const ids=setup(lesson);
    assert.equal(ids['transcript-tab'].hidden,true);
    assert.equal(ids['skip-transcript'].hidden,true);
    ids['chapter-tab'].dispatch('keydown',{key:'End',preventDefault(){}});
    assert.equal(ids.document.activeElement,ids['chapter-tab']);
    ids['transcript-tab'].dispatch('click');
    assert.equal(ids['transcript-panel'].hidden,true);
    ids['skip-transcript'].dispatch('click',{preventDefault(){}});
    assert.equal(ids.document.activeElement,ids['chapter-tab']);
  }
});
test('subtitles keep transcript navigation available even when captions are off or search is empty',()=>{
  const ids=setup(subtitleLesson());
  assert.equal(ids['transcript-tab'].hidden,false);assert.equal(ids['skip-transcript'].hidden,false);
  ids['caption-language'].value='off';ids['caption-language'].dispatch('change');
  ids['transcript-search'].value='no matching cue';ids['transcript-search'].dispatch('input');ids.runTimers();
  assert.equal(ids['transcript-tab'].hidden,false);
  ids['skip-transcript'].dispatch('click',{preventDefault(){}});
  assert.equal(ids['transcript-panel'].hidden,false);
});
test('native accesskeys have unique focus destinations and keyboard help has no jump links',()=>{
  const keys=[...html.matchAll(/<a\b([^>]*\baccesskey="([^"]+)"[^>]*)>/g)];
  assert.deepEqual(keys.map(([,attrs,key])=>[key,/data-jump="([^"]+)"/.exec(attrs)?.[1]]),
    [['c','player'],['l','chapters'],['t','transcript']]);
  const help=html.match(/<details class="keyboard-help">([\s\S]*?)<\/details>/)[1];
  assert.ok(!/<a\b/.test(help));assert.ok(help.includes('class="access-platforms"'));
});
test('English credit always appears and localized credit follows interface language',()=>{
  const ids=setup(subtitleLesson(),{},'en-US');
  assert.equal(ids['localized-credit'].hidden,true);
  for(const language of ['zh-TW','ja','en']){
    ids['ui-language'].value=language;ids['ui-language'].dispatch('change');
    assert.equal(ids['localized-credit'].hidden,language==='en');
  }
  const footer=html.match(/<footer>([\s\S]*?)<\/footer>/)[1];
  assert.ok(footer.includes('Created with'));
  assert.equal([...footer.matchAll(/href="https:\/\/github.com\/kaoshou\/OpenCam"/g)].length,2);
});
test('playback never replaces or scrolls focused transcript rows',()=>{
  const data=subtitleLesson();data.subtitles.tracks[0].cues=Array.from({length:1000},(_,i)=>({start:i,end:i+1,text:'Cue '+i}));
  const ids=setup(data);ids['transcript-tab'].dispatch('click');const row=ids['transcript-list'].children[0];
  ids.document.activeElement=row;ids.video.currentTime=555;ids.video.dispatch('timeupdate');
  assert.equal(ids['transcript-list'].children[0],row);assert.equal(row.scrolled,true); // initial follow only
  ids.document.activeElement=null;ids.video.currentTime=556;ids.video.dispatch('timeupdate');
  assert.equal(ids['transcript-list'].children[0].children[1].textContent,'Cue 500');
});
test('caption close returns keyboard focus and tab navigation supports Home and End',()=>{
  const ids=setup(subtitleLesson());ids['caption-button'].dispatch('click');ids['caption-close'].dispatch('click');
  assert.equal(ids['caption-menu'].hidden,true);assert.equal(ids.document.activeElement,ids['caption-button']);
  ids['chapter-tab'].dispatch('keydown',{key:'End',preventDefault(){}});
  assert.equal(ids.document.activeElement,ids['transcript-tab']);
  ids['transcript-tab'].dispatch('keydown',{key:'Home',preventDefault(){}});
  assert.equal(ids.document.activeElement,ids['chapter-tab']);
});
test('skip transcript opens its panel even from wide mode and transfers focus',()=>{
  const ids=setup(subtitleLesson());ids.layout.classList.add('wide');
  ids['skip-transcript'].dispatch('click',{preventDefault(){}});
  assert.equal(ids.layout.classList.contains('wide'),false);
  assert.equal(ids['transcript-panel'].hidden,false);assert.equal(ids.document.activeElement,ids['transcript-tab']);
});
test('summary contains duration and chapter count without repeating product branding',()=>{
  assert.equal(setup().summary.textContent,'影片總長度 0:12 · 3 章節');
});
test('transcript searches plain text and seeks without changing paused or playing state',()=>{
  const ids=setup(subtitleLesson());ids['transcript-tab'].dispatch('click');
  ids['transcript-search'].value='重疊';ids['transcript-search'].dispatch('input');ids.runTimers();
  const rows=ids['transcript-list'].children;assert.equal(rows.length,1);
  rows[0].dispatch('click');assert.equal(ids.video.currentTime,1);assert.equal(ids.video.paused,true);
  ids.video.play();rows[0].dispatch('click');assert.equal(ids.video.paused,false);
  ids['caption-language'].value='off';ids['caption-language'].dispatch('change');
  assert.equal(ids['transcript-list'].children.length,1);
  ids['transcript-search'].value='';ids['transcript-search'].dispatch('input');ids.runTimers();
  assert.equal(ids['transcript-list'].children[0].children[1].textContent,'教材 <img onerror=alert(1)>');
});
test('transcript language is independent, has empty states and bounded batches',()=>{
  const ids=setup(subtitleLesson());ids['transcript-language'].value='en';ids['transcript-language'].dispatch('change');
  assert.equal(ids['transcript-list'].children[0].children[1].textContent,'Lesson');
  assert.equal(ids['caption-text'].textContent,'教材 <img onerror=alert(1)>');
  ids['transcript-search'].value='missing';ids['transcript-search'].dispatch('input');ids.runTimers();
  assert.equal(ids['transcript-list'].children.length,0);assert.ok(ids['transcript-status'].textContent.includes('0'));
  const lesson=subtitleLesson();lesson.subtitles.tracks[0].cues=Array.from({length:1000},(_,i)=>({start:i,end:i+1,text:'Cue '+i}));
  const many=setup(lesson);assert.equal(many['transcript-list'].children.length,100);
  many['transcript-more'].dispatch('click');assert.equal(many['transcript-list'].children.length,200);
  assert.ok(setup()['transcript-status'].textContent.includes('字幕'));
});
test('transcript follows distant playback in batches but search and follow-off stop automatic scrolling',()=>{
  const lesson=subtitleLesson();lesson.subtitles.tracks[0].cues=Array.from({length:1000},(_,i)=>({start:i,end:i+1,text:'Cue '+i}));
  const ids=setup(lesson);ids['transcript-tab'].dispatch('click');
  ids.video.currentTime=555.5;ids.video.dispatch('timeupdate');
  assert.equal(ids['transcript-list'].children.length,100);
  const current=ids['transcript-list'].children[55];assert.equal(current.attributes['aria-current'],'true');assert.equal(current.scrolled,true);
  ids['transcript-follow'].checked=false;ids.video.currentTime=777;ids.video.dispatch('timeupdate');
  assert.equal(ids['transcript-list'].children[0].children[1].textContent,'Cue 500');
  ids['transcript-search'].value='Cue 12';ids['transcript-search'].dispatch('input');ids.runTimers();
  assert.equal(ids['transcript-list'].children.length,11);
  assert.equal(ids['transcript-list'].children[0].children[1].children[1].textContent,'Cue 12');
});
test('transcript seeks before metadata and preserves pause when metadata arrives',()=>{
  const ids=setup(subtitleLesson());ids.video.readyState=0;
  ids['transcript-list'].children[1].dispatch('click');assert.equal(ids.video.currentTime,0);assert.equal(ids.video.paused,true);
  ids.video.readyState=1;ids.video.dispatch('loadedmetadata');assert.equal(ids.video.currentTime,1);assert.equal(ids.video.paused,true);
});
test('transcript sidebar keeps the same bounded height when switching tabs and loading rows',()=>{
  const lesson=subtitleLesson();lesson.subtitles.tracks[0].cues=Array.from({length:1000},(_,i)=>({start:i,end:i+1,text:'Cue '+i}));
  const ids=setup(lesson);assert.equal(ids.sidebar.style.height,'480px');
  ids['transcript-tab'].dispatch('click');ids['transcript-more'].dispatch('click');
  assert.equal(ids.sidebar.style.height,'480px');
  ids['chapter-tab'].dispatch('click');assert.equal(ids.sidebar.style.height,'480px');
});
test('transcript reserves at least half the sidebar height without growing the player',()=>{
  const ids=setup(subtitleLesson());assert.equal(ids.sidebar.style['--transcript-min-height'],'240px');
  ids.player.getBoundingClientRect=()=>({height:650});ids['transcript-tab'].dispatch('click');
  assert.equal(ids.sidebar.style.height,'640px');assert.equal(ids.sidebar.style['--transcript-min-height'],'320px');
});
test('web subtitle menus show language and code without imported filenames',()=>{
  const data=subtitleLesson();data.subtitles.tracks[0].label='1. my-teaching-video.srt · zh-TW';
  data.subtitles.tracks[1]={...data.subtitles.tracks[1],language:'zh-CN',label:'2. my-teaching-video.srt · zh-CN'};
  const ids=setup(data);
  assert.deepEqual(ids['caption-language'].children.slice(1).map(o=>o.textContent),[
    '繁體中文（台灣） (zh-TW)','簡體中文（中國大陸） (zh-CN)']);
  assert.deepEqual(ids['transcript-language'].children.map(o=>o.textContent),[
    '繁體中文（台灣） (zh-TW)','簡體中文（中國大陸） (zh-CN)']);
});
test('subtitles default on, remain plain text, support overlaps and clear exactly at the end',()=>{
  const ids=setup(subtitleLesson());
  assert.equal(ids['caption-button'].hidden,false);
  assert.equal(ids['caption-text'].textContent,'教材 <img onerror=alert(1)>');
  ids.video.currentTime=1.5;ids.video.dispatch('timeupdate');
  assert.equal(ids['caption-text'].textContent,'教材 <img onerror=alert(1)>\n重疊');
  ids.video.currentTime=3;ids.video.dispatch('seeked');assert.equal(ids['caption-overlay'].hidden,true);
  ids.video.currentTime=0;ids.video.dispatch('seeked');assert.equal(ids['caption-overlay'].hidden,false);
});
test('subtitle language and off selections update the displayed text without changing author defaults',()=>{
  const data=subtitleLesson(),ids=setup(data);
  ids['caption-language'].value='en';ids['caption-language'].dispatch('change');
  assert.equal(ids['caption-text'].textContent,'Lesson');
  ids['caption-language'].value='off';ids['caption-language'].dispatch('change');
  assert.equal(ids['caption-overlay'].hidden,true);assert.equal(data.subtitles.defaultTrackId,'tw');
});
test('subtitle appearance works without local storage and can reset',()=>{
  const ids=setup(subtitleLesson());
  ids['caption-size'].value='150';ids['caption-size'].dispatch('input');
  assert.equal(ids['caption-text'].style.fontSize,'150%');
  ids['caption-position'].value='top';ids['caption-position'].dispatch('change');
  assert.equal(ids['caption-overlay'].dataset.position,'top');
  ids['caption-reset'].dispatch('click');assert.equal(ids['caption-text'].style.fontSize,'100%');
  assert.equal(ids['caption-overlay'].dataset.position,'bottom');
});
test('subtitle settings keep controls visible and still work on the full screen stage',async()=>{
  const ids=setup(subtitleLesson());
  ids.play.dispatch('click');ids['caption-button'].dispatch('click');ids.runTimers();
  assert.equal(ids.stage.classList.contains('controls-hidden'),false);
  ids.fullscreen.dispatch('click');await new Promise(resolve=>setImmediate(resolve));
  ids['caption-language'].value='en';ids['caption-language'].dispatch('change');
  assert.equal(ids.document.fullscreenElement,ids.stage);assert.equal(ids['caption-text'].textContent,'Lesson');
});
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
