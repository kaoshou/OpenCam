// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, readFile, rm, access} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join, resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';
const root=resolve(import.meta.dirname,'../..');
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');

// Break caught: missing artwork or mutated/re-encoded assets, unexpected overwrite,
// or a packager which writes/activates a system extension instead of a fresh output.
test('optional extension stages original artwork and an accurate inert package inventory',async t=>{
    const temp=await mkdtemp(join(tmpdir(),'opencam-gnome-package-'));
    t.after(()=>rm(temp,{recursive:true,force:true}));
    const output=join(temp,'extension');
    const result=spawnSync(process.execPath,[join(root,'scripts/linux/package-gnome-extension.mjs'),output,'a'.repeat(40)],{encoding:'utf8'});
    assert.equal(result.status,0,result.stderr);
    const manifest=JSON.parse(await readFile(join(output,'OPENCAM-EXTENSION-INFO.json'),'utf8'));
    assert.equal(manifest.commit,'a'.repeat(40));
    assert.equal(manifest.status,'FEASIBILITY ONLY — NOT ENABLED IN OPENCAM');
    const meta=JSON.parse(await readFile(join(output,'metadata.json'),'utf8'));
    assert.deepEqual(meta['shell-version'],['46']);
    for(const style of ['WarmIvory','TealSilver','RoundShort','HighContrast']){
        const original=await readFile(join(root,'src/ScreenRecorder.Core/Assets/TeachingPointers',style+'.png'));
        const packaged=await readFile(join(output,'artwork',style+'.png'));
        assert.equal(sha(packaged),sha(original));
        assert.equal(manifest.files['artwork/'+style+'.png'],sha(original));
    }
    assert.equal(sha(await readFile(join(output,'LICENSE'))),sha(await readFile(join(root,'LICENSE'))));
    assert.equal(spawnSync(process.execPath,[join(root,'scripts/linux/package-gnome-extension.mjs'),output,'b'.repeat(40)],{encoding:'utf8'}).status,1);
    assert.equal(JSON.parse(await readFile(join(output,'OPENCAM-EXTENSION-INFO.json'))).commit,'a'.repeat(40));
});

// Break caught: invalid style, non-finite coordinates or scale causing oversized
// Shell actors; wrong hotspot/scale making a pointer inaccurate.
test('pointer geometry matches existing teaching sizes and clips to the actual source',async()=>{
    const file=join(root,'linux/gnome-extension/geometry.js');
    await assert.doesNotReject(access(file),'Wayland pointer geometry is missing');
    const {pointerGeometry,pointerPlacement,supportsDesktop}=await import(pathToFileURL(file));
    assert.deepEqual(pointerGeometry('WarmIvory','Standard'),{style:'WarmIvory',width:140,height:120,hotspotX:16,hotspotY:16});
    assert.deepEqual(pointerGeometry('RoundShort','Small'),{style:'RoundShort',width:115,height:99,hotspotX:13.12,hotspotY:13.12});
    assert.deepEqual(pointerGeometry('HighContrast','Large'),{style:'HighContrast',width:173,height:148,hotspotX:19.68,hotspotY:19.68});
    assert.throws(()=>pointerGeometry('../../bad','Standard'));
    assert.throws(()=>pointerGeometry('WarmIvory',Infinity));
    const g=pointerGeometry('WarmIvory','Standard'),bounds={x:-1920,y:100,width:1920,height:1080};
    assert.deepEqual(pointerPlacement(g,-1900,120,bounds),{x:-1916,y:104});
    assert.equal(pointerPlacement(g,0,120,bounds),null);
    assert.equal(pointerPlacement(g,NaN,120,bounds),null);
    assert.equal(pointerPlacement(g,-1900,120,{...bounds,width:0}),null);
    assert.equal(supportsDesktop('46.8','wayland'),true);
    for(const [v,session] of [['45.9','wayland'],['47','wayland'],['46.8','x11'],['garbage','wayland']])
        assert.equal(supportsDesktop(v,session),false);
});

test('capture verifier rejects duplicate cursors and absent or incorrectly positioned pointer pixels',async()=>{
    const file=join(root,'scripts/linux/gnome-teaching-pixels.mjs');
    await assert.doesNotReject(access(file),'Capture pixel verifier is missing');
    const {inspectPointerFrames}=await import(pathToFileURL(file));
    const art=Buffer.from([230,180,50,255,230,180,50,255,230,180,50,255,230,180,50,255]);
    const frames=Buffer.alloc(16*8*3*2,30);
    for(const [frame,x]of [[0,3],[1,9]])for(let y=2;y<4;y++)for(let dx=0;dx<2;dx++)
        frames.set([230,180,50],frame*16*8*3+(y*16+x+dx)*3);
    const options={width:16,height:8,art,artWidth:2,artHeight:2,hotspotX:1,hotspotY:1,positions:[[4,3],[10,3]]};
    assert.deepEqual(inspectPointerFrames(frames,options).positions,[0,1]);
    assert.equal(inspectPointerFrames(frames,options).magentaPixels,0);
    assert.deepEqual(inspectPointerFrames(Buffer.alloc(frames.length,30),options).positions,[]);
    assert.deepEqual(inspectPointerFrames(frames,{...options,hotspotX:5}).positions,[]);
    frames.set([255,0,255],0);
    assert.equal(inspectPointerFrames(frames,options).magentaPixels,1);
});
