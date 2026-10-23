// SPDX-License-Identifier: AGPL-3.0-or-later
// Real browser integration checks and synthetic-only screenshots; never captures the desktop.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFile,writeFile,mkdir,mkdtemp,stat} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import {createServer} from 'node:http';
import {resolve,join,extname,sep} from 'node:path';
import {tmpdir} from 'node:os';
import {pathToFileURL} from 'node:url';
import {buildSite} from '../../website/build.mjs';
const require=createRequire(process.env.OPENCAM_BROWSER_PACKAGE||import.meta.url);
const {chromium}=require('playwright');
const axePath=require.resolve('axe-core/axe.min.js');
const repo=resolve(import.meta.dirname,'../..');
const out=process.env.OPENCAM_A11Y_OUTPUT||await mkdtemp(join(tmpdir(),'opencam-reading-'));
await mkdir(out,{recursive:true});await mkdir(join(out,'player'),{recursive:true});await mkdir(join(out,'screens'),{recursive:true});
await buildSite({repositoryRoot:repo,outputRoot:join(out,'OpenCam')});
const lesson={format:'opencam.web-player',schemaVersion:1,title:'讓每一位學習者，都能自在閱讀',
 chapters:[{title:'從聲音到理解',start:0,end:4},{title:'選擇適合自己的閱讀方式',start:4,end:8},{title:'帶走完整的學習內容',start:8,end:12}],
 subtitles:{defaultTrackId:'tw',tracks:[
 {id:'tw',language:'zh-TW',label:'繁體中文',cues:[
 {start:0,end:4,text:'每個人，都有適合自己的學習節奏。'},
 {start:4,end:8,text:'調整字級、行距與背景，讓閱讀更輕鬆。'},
 {start:8,end:12,text:'下載完整逐字稿，離線閱讀也很方便。'}]},
 {id:'en',language:'en',label:'English',cues:[{start:0,end:4,text:'Everyone learns at their own pace.'},{start:4,end:8,text:'Choose text, spacing and contrast that work for you.'},{start:8,end:12,text:'Take the full transcript with you.'}]}]}};
const template=await readFile(join(repo,'src/ScreenRecorder.Recorder/WebPlayer/player.html'),'utf8');
const safeData=data=>'window.OpenCamPlayerData = '+JSON.stringify(data).replaceAll('<','\\u003c')+';';
await writeFile(join(out,'player/index.html'),template.replaceAll('%%TITLE%%',lesson.title));
await writeFile(join(out,'player/opencam-player.js'),safeData(lesson));
const ffmpeg=process.env.FFMPEG_PATH||'ffmpeg';
const browser=await chromium.launch({headless:true});
const poster=await browser.newPage({viewport:{width:1280,height:720}});
await poster.setContent('<body style="margin:0;padding:64px 70px;box-sizing:border-box;width:1280px;height:720px;background:#14253f;color:white;font-family:system-ui"><article style="height:100%;box-sizing:border-box;padding:46px 38px;background:#1e3554;border-radius:16px"><div style="color:#93c5fd;letter-spacing:3px;border-left:4px solid #7dd3fc;padding-left:20px">OPENCAM / LEARNING FOR EVERYONE</div><h1 style="margin:90px 0 30px;font-size:60px;letter-spacing:-2px">Read at your own pace.</h1><p style="font-size:30px;color:#cbd5e1">Listen. Read. Explore.</p><hr style="margin:62px 0 30px;border:0;background:#60a5fa;width:400px;height:3px"><p style="color:#93c5fd">01 / A more comfortable learning experience · Synthetic demo</p></article>');
await poster.screenshot({path:join(out,'poster.png')});await poster.close();
execFileSync(ffmpeg,['-hide_banner','-loglevel','error','-y','-loop','1','-i',join(out,'poster.png'),'-t','12','-r','10',
 '-an','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-movflags','+faststart',join(out,'player/media.mp4')]);
const baseline=execFileSync('git',['show','018aee5:src/ScreenRecorder.Recorder/WebPlayer/player.html'],{cwd:repo,encoding:'utf8'});
await writeFile(join(out,'player/before.html'),baseline.replaceAll('%%TITLE%%',lesson.title));
const server=createServer(async(req,res)=>{
 try{const url=new URL(req.url,'http://localhost');let path=resolve(out,'.'+decodeURIComponent(url.pathname));
 if(!path.startsWith(out+sep))throw Error('outside');if((await stat(path)).isDirectory())path=join(path,'index.html');
 res.setHeader('Content-Type',({'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css','.mp4':'video/mp4','.png':'image/png','.txt':'text/plain; charset=utf-8'})[extname(path)]||'application/octet-stream');
 res.end(await readFile(path));}catch{res.writeHead(404);res.end();}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));const base='http://127.0.0.1:'+server.address().port;
const checks=[];
async function check(name,fn){try{await fn();checks.push({name,result:'PASS'});console.log('PASS '+name);}catch(e){checks.push({name,result:'FAIL',error:e.stack});console.error('FAIL '+name+'\n'+e.stack);}finally{await page.unroute('**/opencam-player.js');}}
const context=await browser.newContext({viewport:{width:1440,height:1000},acceptDownloads:true});
const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
async function goto(path){await page.goto(base+path);await page.waitForTimeout(150);}
async function axe(){await page.addScriptTag({path:axePath});return page.evaluate(async()=>{const r=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa','wcag22aa']}});return r.violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>n.target)}));});}
async function noOverflow(){assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);}
try{
 await check('reading overlay is dismissible and usable on phones tablets and desktop',async()=>{
  for(const [width,height] of [[1440,1000],[768,1024],[1024,768],[390,844],[844,390],[320,568],[320,225]]){
   await page.setViewportSize({width,height});await goto('/player/');await page.locator('#transcript-tab').click();
   const button=page.getByRole('button',{name:'閱讀設定 / Reading settings',exact:true}),dialog=page.getByRole('dialog',{name:'閱讀設定 / Reading settings',exact:true});
   assert(await page.locator('#reading-menu').isHidden());
   const before=await page.locator('#transcript-list').boundingBox();
   await button.click();await dialog.waitFor({state:'visible'});
   assert.equal(await button.getAttribute('aria-expanded'),'true');
   assert.equal(await page.locator('#transcript-list').evaluate(el=>el.getBoundingClientRect().height),before.height);
   assert(await page.locator('#reading-spacing').isVisible());assert(await page.locator('#reading-theme').isVisible());
   const box=await dialog.boundingBox();assert(box.x>=0&&box.y>=0&&box.x+box.width<=width+1&&box.y+box.height<=height+1,JSON.stringify({width,height,box}));
   await page.locator('#download-txt').scrollIntoViewIfNeeded();
   const d=page.waitForEvent('download');await page.locator('#download-txt').click();assert((await d).suggestedFilename().endsWith('.txt'));
   assert(await dialog.isVisible(),'downloading does not dismiss reading settings');
   await page.keyboard.press('Escape');assert(await page.locator('#reading-menu').isHidden());assert(await button.evaluate(el=>el===document.activeElement));
   await button.click();await page.locator('#reading-close').click();assert(await button.evaluate(el=>el===document.activeElement));
   await button.click();await page.mouse.click(2,2);assert(await page.locator('#reading-menu').isHidden());
   await noOverflow();
   if(width===390||width===768||width===1440){await button.click();await page.locator('#reading-close').focus();await page.screenshot({path:join(out,'screens/reading-overlay-'+width+'.png'),fullPage:true});await page.keyboard.press('Escape');}
  }
  await page.setViewportSize({width:1440,height:1000});
 });
 await check('touch phones and tablets can change settings and download in both orientations',async()=>{
  for(const [width,height] of [[390,844],[844,390],[768,1024],[1024,768]]){
   const touch=await browser.newContext({viewport:{width,height},isMobile:true,hasTouch:true,acceptDownloads:true});
   try{
    const p=await touch.newPage();await p.goto(base+'/player/');await p.locator('#transcript-tab').tap();await p.locator('#reading-button').tap();
    await p.locator('#reading-theme').selectOption('warm');assert.equal(await p.locator('#transcript-list').getAttribute('data-theme'),'warm');
    await p.setViewportSize({width:height,height:width});
    await p.waitForFunction(()=>{const r=document.querySelector('#reading-menu').getBoundingClientRect();return r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1});
    await p.locator('#download-html').scrollIntoViewIfNeeded();const d=p.waitForEvent('download');await p.locator('#download-html').tap();assert((await d).suggestedFilename().endsWith('.html'));
    await p.locator('#reading-close').tap();assert(await p.locator('#reading-menu').isHidden());
    assert(await p.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));
   }finally{await touch.close();}
  }
 });
 await check('reading settings start closed and remain keyboard optional',async()=>{
  await goto('/player/');await page.locator('#transcript-tab').click();
  assert(await page.locator('#reading-menu').isHidden());
  assert(await page.locator('#transcript-language').isVisible());assert(await page.locator('#transcript-search').isVisible());
  for(const id of ['transcript-follow','reading-size','download-html','download-txt'])assert(await page.locator('#'+id).isHidden(),id+' stays hidden until More settings is opened');
  await page.screenshot({path:join(out,'screens/player-minimal-transcript.png')});
  await page.locator('#reading-button').click();
  assert(await page.locator('#transcript-follow').isVisible());
  assert(await page.locator('#download-html').isVisible());assert(await page.locator('#reading-size').isVisible());
  assert(await page.locator('#reading-spacing').isVisible());
  assert(await page.locator('#reading-spacing').isVisible());await page.locator('#reading-theme').selectOption('warm');
  await page.keyboard.press('Escape');assert(await page.locator('#reading-menu').isHidden());
  await page.locator('#reading-button').focus();await page.keyboard.press('Enter');assert(await page.locator('#reading-spacing').isVisible());
  await page.locator('#download-txt').focus();await page.keyboard.press('Tab');assert(await page.locator('#reading-menu').isHidden());
  await page.reload();await page.locator('#transcript-tab').click();assert(await page.locator('#reading-menu').isHidden());await page.locator('#reading-button').click();
  assert.equal(await page.locator('#reading-theme').inputValue(),'warm');
  await page.screenshot({path:join(out,'screens/player-compact-settings.png')});
  await page.locator('#reading-reset').click();
 });
 await check('default player preserves original layout',async()=>{
  await goto('/player/before.html');const before=await page.locator('#player').boundingBox();await page.screenshot({path:join(out,'screens/player-before.png')});
  await goto('/player/');assert.deepEqual(await page.locator('#player').boundingBox(),before);
  await page.screenshot({path:join(out,'screens/player-default.png')});
 });
 await check('player reading, safe full-track downloads, language and storage',async()=>{
  await page.locator('#transcript-tab').click();await page.locator('#reading-button').click();
  await page.locator('#reading-size').fill('150');await page.locator('#reading-spacing').selectOption('1.8');await page.locator('#reading-theme').selectOption('dark');
  assert.equal(await page.locator('#transcript-list').evaluate(el=>getComputedStyle(el).fontSize),'24px');
  await page.screenshot({path:join(out,'screens/player-reading.png')});
  await page.locator('#download-html').scrollIntoViewIfNeeded();await page.screenshot({path:join(out,'screens/player-downloads.png')});
  await page.reload();await page.locator('#transcript-tab').click();assert.equal(await page.locator('#reading-size').inputValue(),'150');
  const data=structuredClone(lesson);data.title='安全 </title><script>bad()</script>';
  data.subtitles.tracks[0].cues=Array.from({length:1005},(_,i)=>({start:i,end:i+1,text:i===0?'講者：<img src=x onerror=bad()>':'完整段落 '+i}));
  await page.route('**/opencam-player.js',route=>route.fulfill({body:safeData(data),contentType:'text/javascript'}));
  await page.reload();await page.locator('#transcript-tab').click();await page.locator('#transcript-search').fill('完整段落 999');await page.waitForTimeout(200);
  assert.equal(await page.locator('.transcript-row').count(),1);await page.locator('#reading-button').click();
  const promise=page.waitForEvent('download');await page.locator('#download-html').click();const download=await promise;
  await download.saveAs(join(out,'reading.html'));const html=await readFile(join(out,'reading.html'),'utf8');
  assert(html.includes('完整段落 1004'));assert(html.includes('&lt;img src=x onerror=bad()&gt;'));assert(!html.includes('<script>'));
  await page.keyboard.press('Escape');await page.locator('#transcript-language').selectOption('en');await page.locator('#reading-button').click();
  const next=page.waitForEvent('download');await page.locator('#download-txt').click();const txtDownload=await next;
  const txt=await readFile(await txtDownload.path(),'utf8');assert(txt.includes('Everyone learns'));assert(!txt.includes('完整段落'));
  await page.unroute('**/opencam-player.js');
 });
 await check('keyboard menus, tabs and focused transcript follow',async()=>{
  await goto('/player/');await page.locator('#caption-button').focus();await page.keyboard.press('Enter');assert(await page.locator('#caption-language').evaluate(el=>el===document.activeElement));
  await page.keyboard.press('Escape');assert(await page.locator('#caption-menu').isHidden());assert(await page.locator('#caption-button').evaluate(el=>el===document.activeElement));
  await page.locator('#chapter-tab').focus();await page.keyboard.press('End');assert(await page.locator('#transcript-tab').evaluate(el=>el===document.activeElement));
  await page.keyboard.press('Home');assert(await page.locator('#chapter-tab').evaluate(el=>el===document.activeElement));
  await page.locator('#transcript-tab').click();await page.locator('.transcript-row').first().focus();
  await page.evaluate(()=>{document.querySelector('#video').currentTime=8;});assert(await page.locator('.transcript-row').first().evaluate(el=>el===document.activeElement));
  await page.locator('#transcript-search').focus();await page.keyboard.type('f');assert.equal(await page.locator('#fullscreen').getAttribute('aria-pressed'),'false');
  await goto('/player/');await page.locator('#wide').click();await page.locator('#skip-transcript').focus();await page.keyboard.press('Enter');
  assert(await page.locator('#transcript-panel').isVisible());assert(await page.locator('#transcript-tab').evaluate(el=>el===document.activeElement));
 });
 await check('caption settings keyboard, contrast and fullscreen',async()=>{
  await goto('/player/');await page.locator('#caption-button').click();await page.locator('#caption-size').fill('200');await page.locator('#caption-opacity').fill('1');
  assert.deepEqual(await axe(),[]);await page.screenshot({path:join(out,'screens/caption-settings.png')});
  await page.locator('#caption-close').click();await page.locator('#fullscreen').click();await page.locator('#caption-button').click();
  assert(await page.locator('#caption-menu').isVisible());await page.keyboard.press('Escape');assert(await page.locator('#caption-menu').isHidden());
  await page.locator('#fullscreen').click();
 });
 await check('player storage denial and absent captions remain usable',async()=>{
  const denied=await browser.newContext();await denied.addInitScript(()=>{Object.defineProperty(window,'localStorage',{get(){throw Error('blocked')}})});
  const p=await denied.newPage();await p.goto(base+'/player/');await p.locator('#transcript-tab').click();await p.locator('#reading-button').click();await p.locator('#reading-size').fill('200');
  assert.equal(await p.locator('#reading-size').inputValue(),'200');
  await p.route('**/opencam-player.js',r=>r.fulfill({body:safeData({...lesson,subtitles:null})}));await p.reload();await p.locator('#transcript-tab').click();
  assert(await p.locator('#download-html').isDisabled());await denied.close();
 });
 await check('player axe and narrow reading layout',async()=>{
  await goto('/player/');await page.locator('#transcript-tab').click();await page.locator('#reading-button').click();
  assert.deepEqual(await axe(),[]);
  const long=structuredClone(lesson);long.subtitles.tracks[0].cues=Array.from({length:1005},(_,i)=>({start:i,end:i+1,text:'教材閱讀 '+i}));
  await page.route('**/opencam-player.js',r=>r.fulfill({body:safeData(long),contentType:'text/javascript'}));await page.reload();
  await page.locator('#transcript-tab').click();await page.locator('#reading-button').click();
  for(const [width,height] of [[720,900],[360,900],[320,900],[320,225]]){
   await page.setViewportSize({width,height});await page.locator('#reading-size').fill('200');await noOverflow();
   // Viewport resize and ResizeObserver delivery are asynchronous. Wait for
   // the actual required geometry, not an arbitrary delay or relaxed bounds.
   await page.waitForFunction(()=>{const more=document.querySelector('#transcript-more').getBoundingClientRect(),panel=document.querySelector('#transcript-panel').getBoundingClientRect();return more.height>=32&&more.top>=panel.top&&more.bottom<=panel.bottom+1;},{},{timeout:2000});
   assert((await page.locator('#transcript-list').boundingBox()).height>=80);
   const {more,panel}=await page.evaluate(()=>({more:document.querySelector('#transcript-more').getBoundingClientRect().toJSON(),panel:document.querySelector('#transcript-panel').getBoundingClientRect().toJSON()}));
   assert(more.height>=32&&more.y>=panel.y&&more.y+more.height<=panel.y+panel.height+1,'Load more is fully visible with expanded reading controls: '+JSON.stringify({width,height,more,panel,tools:await page.locator('.transcript-tools').boundingBox()}));
  }
  await page.setViewportSize({width:320,height:900});await page.keyboard.press('Escape');await page.locator('#transcript-more').click();assert.equal(await page.locator('.transcript-row').count(),200);
  await page.unroute('**/opencam-player.js');
  await page.screenshot({path:join(out,'screens/player-mobile.png'),fullPage:true});
  await page.emulateMedia({forcedColors:'active',reducedMotion:'reduce'});await noOverflow();await page.emulateMedia({forcedColors:'none'});
 });
 await check('transcript owns at least half the sidebar in every settings state',async()=>{
  const long=structuredClone(lesson);long.subtitles.tracks[0].cues=Array.from({length:1005},(_,i)=>({start:i,end:i+1,text:'閱讀內容 '+i}));
  await page.route('**/opencam-player.js',r=>r.fulfill({body:safeData(long),contentType:'text/javascript'}));
  await goto('/player/');await page.locator('#transcript-tab').click();
  for(const [width,height] of [[1440,1000],[720,900],[320,900],[320,225]]){
   await page.setViewportSize({width,height});
   for(const state of ['closed','open']){
    await page.keyboard.press('Escape');if(state==='open')await page.locator('#reading-button').click();
    await page.waitForFunction(()=>{
     const list=document.querySelector('#transcript-list').getBoundingClientRect(),side=document.querySelector('#sidebar').getBoundingClientRect(),more=document.querySelector('#transcript-more').getBoundingClientRect(),panel=document.querySelector('#transcript-panel').getBoundingClientRect();
     return list.height>=side.height/2&&more.height>=32&&more.top>=panel.top&&more.bottom<=panel.bottom+1;
    },{},{timeout:2000});await noOverflow();
   }
  }
  await page.setViewportSize({width:1440,height:1000});await page.locator('#reading-button').click();
  await page.screenshot({path:join(out,'screens/player-half-height.png')});
 });
 await check('reading document is offline, safe and printable',async()=>{
  await page.setViewportSize({width:1100,height:900});await goto('/reading.html');assert.equal(await page.locator('script').count(),0);assert.equal(await page.locator('img').count(),0);
  assert.equal(await page.locator('p').count(),1007);assert.deepEqual(await axe(),[]);await page.setViewportSize({width:320,height:900});await noOverflow();
  // Save a readable normal lesson, not the security stress fixture, for the visual handoff.
  await page.setViewportSize({width:1440,height:1000});await goto('/player/');await page.locator('#transcript-tab').click();await page.locator('#reading-button').click();
  const d=page.waitForEvent('download');await page.locator('#download-html').click();await (await d).saveAs(join(out,'reading-preview.html'));
  await goto('/reading-preview.html');await page.screenshot({path:join(out,'screens/reading-document.png'),fullPage:true});await page.emulateMedia({media:'print'});await noOverflow();await page.emulateMedia({media:'screen'});
 });
 await check('exported player and downloaded reading document work from local files',async()=>{
  await page.goto(pathToFileURL(join(out,'player/index.html')).href);await page.locator('#transcript-tab').click();
  assert.equal(await page.locator('.transcript-row').count(),3);await page.locator('#reading-button').click();
  const ready=page.waitForEvent('download');await page.locator('#download-html').click();assert((await ready).suggestedFilename().endsWith('.html'));
  await page.goto(pathToFileURL(join(out,'reading-preview.html')).href);assert.equal(await page.locator('h2').count(),3);
 });
 for(const language of ['zh-TW','en-US'])for(const suffix of ['','guide/'])await check('website '+language+'/'+suffix+' keyboard, axe, reflow',async()=>{
  await page.setViewportSize({width:1440,height:1000});await goto('/OpenCam/'+language+'/'+suffix);
  await page.keyboard.press('Tab');await page.keyboard.press('Enter');assert.equal(await page.evaluate(()=>document.activeElement.id),'main');
  const other=page.locator('.language-switch');assert.equal(await other.getAttribute('lang'),language==='zh-TW'?'en-US':'zh-TW');
  assert.deepEqual(await axe(),[]);
  if(!suffix&&language==='zh-TW'){await page.evaluate(()=>{document.activeElement.blur();window.scrollTo(0,0)});await page.screenshot({path:join(out,'screens/website-desktop.png')});}
  for(const width of [720,360,320]){await page.setViewportSize({width,height:900});await noOverflow();}
  await page.evaluate(()=>document.documentElement.style.fontSize='200%');await noOverflow();
  if(language==='zh-TW'&&!suffix)await page.screenshot({path:join(out,'screens/website-zoom.png'),fullPage:true});
 });
 assert.deepEqual(errors,[]);
}finally{await writeFile(join(out,'results.json'),JSON.stringify(checks,null,2));await browser.close();server.close();console.log('Evidence: '+out);}
if(checks.some(c=>c.result==='FAIL'))process.exitCode=1;
