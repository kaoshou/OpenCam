// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {access} from 'node:fs/promises';
const moduleUrl=new URL('../../linux/gnome-extension/session.js',import.meta.url);
const source={id:'portal:'+'a'.repeat(64),generation:1,monitorWidth:640,monitorHeight:360,x:0,y:0,width:640,height:360};
const options={style:'WarmIvory',size:'Standard'},nonce='b'.repeat(64),otherNonce='c'.repeat(64),owner=':1.42';
async function load(){await assert.doesNotReject(access(moduleUrl),'Owned Wayland session is missing');return import(moduleUrl);}
async function fixture(overrides={}){
    const {TeachingSession}=await load();
    let clock=0,started=0,disposed=0;const applied=[],faults=[];
    const session=new TeachingSession({now:()=>clock,authorize:async()=>true,
        activate:async()=>{started++;return {command:c=>applied.push(c),dispose:()=>disposed++};},onFault:c=>faults.push(c),...overrides});
    return {session,applied,faults,get started(){return started;},get disposed(){return disposed;},advance:ms=>clock+=ms};
}
test('no actors before explicit authorization; denial never activates a session',async()=>{
    const f=await fixture({authorize:async()=>false});
    assert.equal(f.started,0);
    assert.equal(await f.session.begin(owner,nonce,source,options),false);
    assert.equal(f.started,0);assert.equal(f.session.active,false);
});
test('one unique owner and fresh nonce control a single live renderer',async()=>{
    const f=await fixture();assert.equal(await f.session.begin(owner,nonce,source,options),true);
    assert.equal(await f.session.begin(':1.43',otherNonce,source,options),false);
    assert.equal(f.session.renew(':1.43',nonce),false);
    assert.equal(f.session.renew(owner,otherNonce),false);
    assert.equal(await f.session.command(':1.43',nonce,{name:'clear'}),false);
    assert.equal(f.session.end(':1.43',nonce),false);
    assert.equal(f.started,1);assert.equal(f.disposed,0);
    assert.equal(await f.session.command(owner,nonce,{name:'clear'}),true);
    assert.deepEqual(f.applied,[{name:'clear'}]);
    assert.equal(f.session.end(owner,nonce),true);assert.equal(f.disposed,1);
    assert.equal(await f.session.command(owner,nonce,{name:'clear'}),false);
    assert.equal(await f.session.begin(owner,nonce,source,options),false);
    assert.equal(await f.session.begin(owner,otherNonce,source,options),true);
    f.session.dispose();f.session.dispose();assert.equal(f.disposed,2);
});
test('a 3-second monotonic lease expires even while drawing; a valid renewal extends it',async()=>{
    const f=await fixture();await f.session.begin(owner,nonce,source,options);
    f.advance(2999);assert.equal(f.session.renew(owner,nonce),true);
    f.advance(2999);f.session.tick();assert.equal(f.session.active,true);
    f.advance(1);f.session.tick();assert.equal(f.session.active,false);assert.equal(f.disposed,1);
    assert.equal(f.session.renew(owner,nonce),false);assert.equal(f.session.end(owner,nonce),false);
});
test('owner loss during pending consent cannot create a late overlay',async()=>{
    let allow;const consent=new Promise(resolve=>allow=resolve),f=await fixture({authorize:()=>consent});
    const pending=f.session.begin(owner,nonce,source,options);
    f.session.ownerLost(':1.43');f.session.ownerLost(owner);allow(true);
    assert.equal(await pending,false);assert.equal(f.started,0);
});
test('a resource created after cancellation is disposed instead of becoming active',async()=>{
    let release,disposed=0;
    const f=await fixture({activate:()=>new Promise(resolve=>release=()=>resolve({command:()=>{},dispose:()=>disposed++}))});
    const pending=f.session.begin(owner,nonce,source,options);
    await Promise.resolve();await Promise.resolve();
    f.session.dispose();release();assert.equal(await pending,false);assert.equal(disposed,1);
});
test('at most 128 queued commands; late queued work cannot draw after owner loss',async()=>{
    const f=await fixture();await f.session.begin(owner,nonce,source,options);
    const pending=Array.from({length:128},()=>f.session.command(owner,nonce,{name:'clear'}));
    assert.equal(await f.session.command(owner,nonce,{name:'clear'}),false);
    f.session.ownerLost(owner);
    await Promise.all(pending);assert.equal(f.session.active,false);assert.equal(f.disposed,1);
    assert.ok(f.applied.length<128);
});
test('renderer command failure closes the session and reports no raw exception or nonce',async()=>{
    let cleaned=0;
    const f=await fixture({activate:async()=>({command:()=>{throw new Error(nonce);},dispose:()=>cleaned++})});
    await f.session.begin(owner,nonce,source,options);
    assert.equal(await f.session.command(owner,nonce,{name:'clear'}),false);
    assert.equal(cleaned,1);assert.equal(f.session.active,false);assert.deepEqual(f.faults,['renderer_failed']);
});
test('cleanup exceptions never keep ownership alive or surface arbitrary error text',async()=>{
    const f=await fixture({activate:async()=>({command:()=>{},dispose:()=>{throw new Error(nonce);}})});
    await f.session.begin(owner,nonce,source,options);f.session.ownerLost(owner);
    assert.equal(f.session.active,false);assert.deepEqual(f.faults,['cleanup_unconfirmed']);
    f.session.dispose();assert.deepEqual(f.faults,['cleanup_unconfirmed']);
});
test('strict protocol rejects unknown fields, invalid geometry and oversized messages before side effects',async()=>{
    await load();
    const {parseRequest}=await import('../../linux/gnome-extension/protocol.js');
    const start={protocol:1,action:'begin',nonce,source,options};
    assert.deepEqual(parseRequest(JSON.stringify(start)),start);
    const invalid=[{...start,protocol:2},{...start,eval:'x'},{...start,nonce:'bad'},
        {...start,source:{...source,x:-1}}, {...start,source:{...source,x:10}},
        {...start,source:{...source,height:NaN}}, {...start,source:{...source,monitorWidth:Infinity}},
        {...start,options:{...options,style:'../../x'}}, {...start,options:{...options,size:'Huge'}},
        {protocol:1,action:'command',nonce,command:{name:'execute',text:'x'}},
        {protocol:1,action:'command',nonce,command:{name:'clear',extra:true}}];
    for(const request of invalid)assert.throws(()=>parseRequest(JSON.stringify(request)),/invalid_request/);
    assert.throws(()=>parseRequest(' '.repeat(65537)),/invalid_request/);
    assert.throws(()=>parseRequest('"'+'界'.repeat(24000)+'"'),/invalid_request/);
    assert.throws(()=>parseRequest('{not-json'),/invalid_request/);
});
test('invalid direct begin calls never reach authorization or renderer startup',async()=>{
    let requested=0;const f=await fixture({authorize:async()=>{requested++;return true;}});
    for(const [peer,token,screen]of [['not-a-bus-owner',nonce,source],[owner,'bad',source],[owner,nonce,{...source,x:1}]])
        assert.equal(await f.session.begin(peer,token,screen,options),false);
    assert.equal(requested,0);assert.equal(f.started,0);
});
test('all owned cleanup operations are attempted even if an earlier native release throws',async()=>{
    const file=new URL('../../linux/gnome-extension/cleanup.js',import.meta.url);
    await assert.doesNotReject(access(file),'All-resource cleanup is missing');
    const {disposeAll}=await import(file),released=[];
    assert.equal(disposeAll([()=>{released.push('cursor');throw new Error('native');},()=>released.push('input'),()=>released.push('actor')]),false);
    assert.deepEqual(released,['cursor','input','actor']);
    assert.equal(disposeAll([()=>{}]),true);
});
