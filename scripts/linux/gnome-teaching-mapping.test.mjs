// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {access} from 'node:fs/promises';
const url=new URL('../../linux/gnome-extension/mapping.js',import.meta.url),owner=':1.25',id='portal:'+'d'.repeat(64);
const monitors=[{x:-960,y:0,width:960,height:540},{x:0,y:0,width:1920,height:1080}];
async function fixture(){
    await assert.doesNotReject(access(url),'Verified Wayland source mapping is missing');
    const {SourceMapping}=await import(url);const model=new SourceMapping();
    const values=['0123456789abcdef','fedcba9876543210'];
    const challenge=model.challenge(owner,monitors,()=>values.shift());
    return {model,challenge};
}
function proof(generation,extra={}){return {generation,sourceId:id,sourceWidth:1920,sourceHeight:1080,
    observations:[{nonce:'0123456789abcdef',width:960,height:540}],...extra};}
function source(generation,extra={}){return {id,generation,monitorWidth:1920,monitorHeight:1080,x:100,y:200,width:640,height:480,...extra};}
test('an observed random marker identifies the monitor, not equal physical dimensions',async()=>{
    const {model,challenge}=await fixture(),generation=challenge.view.generation;
    assert.equal(JSON.stringify(challenge.view).includes('0123456789abcdef'),false);
    assert.equal(challenge.markers.length,2);
    assert.ok(model.bind(owner,proof(generation)));
    assert.deepEqual(model.resolve(owner,source(generation)),{x:-910,y:100,width:320,height:240});
});
test('wrong owner, changed source, wrong dimensions or stale generation cannot map a crop',async()=>{
    const {model,challenge}=await fixture(),generation=challenge.view.generation;
    assert.equal(model.bind(':1.26',proof(generation)),null);
    assert.ok(model.bind(owner,proof(generation)));
    for(const [peer,value]of [[':1.26',source(generation)],[owner,source(generation,{id:'portal:'+'e'.repeat(64)})],
        [owner,source(generation+1)],[owner,source(generation,{monitorWidth:2560})],[owner,source(generation,{x:1800})]])
        assert.equal(model.resolve(peer,value),null);
    model.invalidate();assert.equal(model.resolve(owner,source(generation)),null);
});
test('ambiguous, unknown or reused visual proofs fail closed',async()=>{
    for(const observations of [[{nonce:'aaaaaaaaaaaaaaaa',width:960,height:540}],
        [{nonce:'0123456789abcdef',width:960,height:540},{nonce:'fedcba9876543210',width:1920,height:1080}]]){
        const {model,challenge}=await fixture();
        assert.equal(model.bind(owner,proof(challenge.view.generation,{observations})),null);
        assert.equal(model.bind(owner,proof(challenge.view.generation)),null);
    }
    const {model,challenge}=await fixture();assert.ok(model.bind(owner,proof(challenge.view.generation)));
    assert.equal(model.bind(owner,proof(challenge.view.generation)),null);
});
test('mapping resets on actual owner loss but unrelated peers cannot clear it',async()=>{
    const {model,challenge}=await fixture(),generation=challenge.view.generation;
    model.bind(owner,proof(generation));model.ownerLost(':1.26');assert.ok(model.resolve(owner,source(generation)));
    model.ownerLost(owner);assert.equal(model.resolve(owner,source(generation)),null);
});
test('bounds and marker count are limited before creating any desktop actors',async()=>{
    const {model}=await fixture();
    assert.throws(()=>model.challenge(owner,Array(65).fill(monitors[0]),()=>''),/mapping_unavailable/);
    assert.throws(()=>model.challenge(owner,[{...monitors[0],width:NaN}],()=>''),/mapping_unavailable/);
    assert.throws(()=>model.challenge(owner,monitors,()=> '0123456789abcdef'),/mapping_unavailable/);
    assert.equal(model.bind(owner,proof(1)),null);
});
