// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import {renderHome} from '../src/content.mjs';
import {renderLayout} from '../src/layout.mjs';
import {renderGuide} from '../src/guide.mjs';

test('both homepages expose inline image previews rather than new tabs', () => {
  for (const language of ['zh-TW', 'en-US']) {
    const html=renderHome(language);
    const links=[...html.matchAll(/<a\b[^>]*href="[^"]+\.png"[^>]*>/g)].map(m=>m[0]);
    assert.equal(links.length,10); // Hero, four screenshot/caption pairs, and audio preflight.
    for (const link of links) {
      assert.match(link,/\bdata-lightbox\b/);
      assert.doesNotMatch(link,/target=/);
    }
    const page=renderLayout({language,title:'OpenCam',description:'Preview',page:'home',body:html});
    assert.match(page,/<dialog\b[^>]*id="image-lightbox"[^>]*aria-label="[^"]+"/);
    assert.match(page,/<button\b[^>]*data-lightbox-close[^>]*>[\s\S]*?(?:關閉|Close)/);
  }
});

test('guide images and existing image links open the same lightbox without nested anchors', () => {
  const html=renderGuide('![Editor](images/preview_editor_enus.png)\n\n[![Main](images/preview_main_enus.png)](images/preview_main_enus.png)','en-US').html;
  assert.equal([...html.matchAll(/data-lightbox/g)].length,2);
  assert.equal([...html.matchAll(/<a\b/g)].length,2);
  assert.match(html,/<a[^>]*href="\/OpenCam\/assets\/preview_editor_enus.png"[^>]*><img/);
  assert.doesNotMatch(html,/<a[^>]*>\s*<a/);
});

// A minimal DOM boundary executes the shipped browser script, not a duplicate
// lightbox implementation. Native modal focus trapping remains browser-owned.
class Element extends EventTarget {
  attributes=new Map(); textContent=''; open=false; focused=false;
  getAttribute(name) { return this.attributes.get(name)??null; }
  setAttribute(name,value) { this.attributes.set(name,String(value)); }
  removeAttribute(name) { this.attributes.delete(name); }
  focus() { this.focused=true; }
  querySelector() { return this.thumbnail??null; }
  closest() { return this.figure??null; }
  showModal() { this.open=true; }
  close() { this.open=false; this.dispatchEvent(new Event('close')); }
}

async function fixture({supported=true}={}) {
  const dialog=new Element(), image=new Element(), caption=new Element(), close=new Element();
  const thumbnail=new Element(); thumbnail.alt='Editor <safe text>';
  const link=new Element(); link.thumbnail=thumbnail;
  link.setAttribute('href','/OpenCam/assets/preview_editor_enus.png');
  const body={style:{overflow:'auto'}};
  if (!supported) dialog.showModal=undefined;
  dialog.querySelector=selector=>selector==='img'?image:selector==='figcaption'?caption:close;
  const document={body,querySelector:()=>dialog,getElementById:()=>dialog,
    querySelectorAll:selector=>selector==='[data-language]'?[]:selector==='[data-lightbox]'?[link]:[]};
  vm.runInNewContext(await readFile(new URL('../src/site.js',import.meta.url),'utf8'),
    {document,location:{pathname:'/OpenCam/en-US/'},Event});
  return {dialog,image,caption,close,link,body};
}

test('image click opens in place, sets safe caption and locks background scrolling',async()=>{
  const f=await fixture();
  const click=new Event('click',{cancelable:true});
  f.link.dispatchEvent(click);
  assert.equal(click.defaultPrevented,true);
  assert.equal(f.dialog.open,true);
  assert.equal(f.image.getAttribute('src'),'/OpenCam/assets/preview_editor_enus.png');
  assert.equal(f.image.alt,'Editor <safe text>');
  assert.equal(f.caption.textContent,'Editor <safe text>');
  assert.equal(f.body.style.overflow,'hidden');
  assert.equal(f.close.focused,true);
});

for (const action of ['button','backdrop','escape']) {
  test(`${action} closes preview, restores scrolling and focus`,async()=>{
    const f=await fixture();
    f.link.dispatchEvent(new Event('click',{cancelable:true}));
    if (action==='button') f.close.dispatchEvent(new Event('click'));
    if (action==='backdrop') f.dialog.dispatchEvent(new Event('click'));
    if (action==='escape') f.dialog.dispatchEvent(new Event('cancel',{cancelable:true}));
    assert.equal(f.dialog.open,false);
    assert.equal(f.body.style.overflow,'auto');
    assert.equal(f.link.focused,true);
    assert.equal(f.image.getAttribute('src'),null);
  });
}

test('browsers without modal support retain the ordinary image link',async()=>{
  const f=await fixture({supported:false});
  const click=new Event('click',{cancelable:true});
  f.link.dispatchEvent(click);
  assert.equal(click.defaultPrevented,false);
  assert.equal(f.body.style.overflow,'auto');
});

test('clicking the enlarged image does not dismiss it',async()=>{
  const f=await fixture();
  f.link.dispatchEvent(new Event('click',{cancelable:true}));
  const click=new Event('click');
  Object.defineProperty(click,'target',{value:f.image});
  f.dialog.dispatchEvent(click);
  assert.equal(f.dialog.open,true);
  assert.equal(f.body.style.overflow,'hidden');
});

test('modified clicks retain intentional browser link behavior',async()=>{
  const f=await fixture();
  const click=new Event('click',{cancelable:true});
  Object.defineProperty(click,'ctrlKey',{value:true});
  f.link.dispatchEvent(click);
  assert.equal(click.defaultPrevented,false);
  assert.equal(f.dialog.open,false);
});
