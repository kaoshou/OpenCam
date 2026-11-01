// SPDX-License-Identifier: AGPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import {access} from 'node:fs/promises';
const url=new URL('../../linux/gnome-extension/auth.js',import.meta.url);
const peer={uid:1000,pid:123,executable:'/opt/opencam-beta/OpenCam'};
async function fixture(overrides={}){
    await assert.doesNotReject(access(url),'Explicit peer authorization is missing');
    const {PeerAuthorization}=await import(url);let prompts=0,time=0;const shown=[];
    const auth=new PeerAuthorization({uid:1000,now:()=>time,lookup:async()=>({...peer}),
        confirm:async identity=>{prompts++;shown.push(identity);return true;},...overrides});
    return {auth,shown,get prompts(){return prompts;},advance:ms=>time+=ms};
}
test('different UID or invalid process identity cannot request desktop consent',async()=>{
    for(const value of [{...peer,uid:0},{...peer,pid:0},{...peer,executable:'relative'}, {...peer,executable:'/bad\nAllow'}]){
        const f=await fixture({lookup:async()=>value});
        assert.equal(await f.auth.request(':1.2'),false);assert.equal(f.prompts,0);
    }
});
test('consent displays independently looked-up executable and PID, then rechecks identity',async()=>{
    let lookups=0;const f=await fixture({lookup:async owner=>{assert.equal(owner,':1.2');lookups++;return {...peer};}});
    assert.equal(await f.auth.request(':1.2'),true);assert.equal(lookups,2);
    assert.deepEqual(f.shown,[peer]);assert.equal(f.prompts,1);
    assert.equal(await f.auth.request(':1.2'),true);assert.equal(f.prompts,1);
});
test('process identity changed during consent is not trusted',async()=>{
    let calls=0;const f=await fixture({lookup:async()=>({...peer,pid:++calls===1?123:456})});
    assert.equal(await f.auth.request(':1.2'),false);
});
test('pending consent is exclusive; owner loss cancels it and cannot approve a late result',async()=>{
    let allow,canceled=0;
    const f=await fixture({confirm:(_peer,setCancel)=>new Promise(resolve=>{allow=resolve;setCancel(()=>{canceled++;resolve(false);});})});
    const request=f.auth.request(':1.2');await Promise.resolve();await Promise.resolve();
    assert.equal(await f.auth.request(':1.3'),false);
    f.auth.ownerLost(':1.2');allow(true);assert.equal(await request,false);assert.equal(canceled,1);
});
test('a new connection needs consent; disposal revokes cached identity',async()=>{
    const f=await fixture();assert.equal(await f.auth.request(':1.2'),true);
    f.auth.ownerLost(':1.2');f.advance(1000);
    assert.equal(await f.auth.request(':1.3'),true);assert.equal(f.prompts,2);
    f.auth.dispose();assert.equal(await f.auth.request(':1.3'),false);
});
test('denial is not cached as permission and prompt rate is bounded',async()=>{
    let asked=0;const f=await fixture({confirm:async()=>{asked++;return false;}});
    assert.equal(await f.auth.request(':1.2'),false);
    assert.equal(await f.auth.request(':1.3'),false);assert.equal(asked,1);
    f.advance(1000);assert.equal(await f.auth.request(':1.3'),false);assert.equal(asked,2);
});
