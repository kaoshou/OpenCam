// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {access} from 'node:fs/promises';
import {PeerAuthorization} from '../../linux/gnome-extension/auth.js';
const url=new URL('../../linux/gnome-extension/controller.js',import.meta.url),owner=':1.42',id='portal:'+'a'.repeat(64),nonce='b'.repeat(64);
async function fixture(allowed=true){
    await assert.doesNotReject(access(url),'Authorized source/controller integration is missing');
    const {TeachingController}=await import(url);
    let time=0,marks=0,cleared=0,disposed=0,random=0,prompts=0;const layers=[];
    const authorization=new PeerAuthorization({uid:1000,now:()=>time,lookup:async()=>({uid:1000,pid:100,executable:'/opt/opencam/OpenCam'}),
        confirm:async()=>{prompts++;return allowed;}});
    const controller=new TeachingController({authorization,now:()=>time,readMonitors:()=>[{x:0,y:0,width:640,height:360}],
        randomNonce:()=> (++random).toString(16).padStart(16,'0'),showMarkers:()=>marks++,clearMarkers:()=>cleared++,
        activate:async bounds=>{layers.push(bounds);return {command:()=>{},dispose:()=>disposed++};}});
    return {controller,layers,get marks(){return marks;},get cleared(){return cleared;},get disposed(){return disposed;},get prompts(){return prompts;},advance:ms=>time+=ms};
}
const proof=generation=>({generation,sourceId:id,sourceWidth:640,sourceHeight:360,observations:[{nonce:'0000000000000001',width:640,height:360}]});
const start=(generation,token=nonce)=>JSON.stringify({protocol:1,action:'begin',nonce:token,source:{id,generation,monitorWidth:640,monitorHeight:360,x:10,y:20,width:200,height:100},options:{style:'WarmIvory',size:'Standard'}});
const end=JSON.stringify({protocol:1,action:'end',nonce});
test('neither markers nor pointer can appear before explicit connection consent and verified source proof',async()=>{
    const f=await fixture();
    assert.equal(await f.controller.challenge(owner),null);assert.equal(f.prompts,0);
    assert.equal(await f.controller.request(owner,start(1)),false);assert.equal(f.layers.length,0);assert.equal(f.marks,0);
    assert.equal(await f.controller.connect(owner),true);assert.equal(f.prompts,1);
    assert.equal(await f.controller.request(owner,start(1,'c'.repeat(64))),false);
    const challenge=await f.controller.challenge(owner);assert.equal(f.marks,1);
    assert.equal(JSON.stringify(challenge).includes('0000000000000001'),false);
    assert.ok(await f.controller.bind(owner,proof(challenge.generation)));
    assert.equal(await f.controller.request(owner,start(challenge.generation)),true);
    assert.deepEqual(f.layers,[{x:10,y:20,width:200,height:100}]);assert.equal(f.controller.active,true);
    assert.equal(f.prompts,1);assert.equal(await f.controller.challenge(owner),null);
    assert.equal(await f.controller.request(':1.43',end),false);assert.equal(f.controller.active,true);
    assert.equal(await f.controller.request(owner,end),true);assert.equal(f.disposed,1);
});
test('denial cannot be converted into permission by requesting a source or calling Begin',async()=>{
    const f=await fixture(false);assert.equal(await f.controller.connect(owner),false);
    assert.equal(await f.controller.challenge(owner),null);assert.equal(await f.controller.bind(owner,proof(1)),null);
    assert.equal(await f.controller.request(owner,start(1)),false);assert.equal(f.marks,0);assert.equal(f.layers.length,0);
});
test('owner loss and desktop invalidation discard the verified source and active layer',async()=>{
    for(const action of ['ownerLost','invalidate']){
        const f=await fixture();await f.controller.connect(owner);const challenge=await f.controller.challenge(owner);
        await f.controller.bind(owner,proof(challenge.generation));await f.controller.request(owner,start(challenge.generation));
        if(action==='ownerLost')f.controller.ownerLost(owner);else f.controller.invalidate();
        assert.equal(f.controller.active,false);assert.equal(f.disposed,1);
        assert.equal(await f.controller.request(owner,start(challenge.generation)),false);
        assert.equal(await f.controller.challenge(owner),null);assert.equal(f.prompts,1);
    }
});
test('invalid proof clears only its owned marker layer and never starts a renderer',async()=>{
    const f=await fixture();await f.controller.connect(owner);const challenge=await f.controller.challenge(owner);
    assert.equal(await f.controller.bind(':1.43',proof(challenge.generation)),null);assert.equal(f.cleared,0);
    assert.equal(await f.controller.bind(owner,{...proof(challenge.generation),sourceId:'wrong'}),null);
    assert.equal(f.cleared,1);assert.equal(await f.controller.request(owner,start(challenge.generation)),false);
    assert.equal(f.layers.length,0);
});
