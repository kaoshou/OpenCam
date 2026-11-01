// SPDX-License-Identifier: AGPL-3.0-or-later
import {validOwner,validSource} from './protocol.js';
const nonce=value=>typeof value==='string'&&/^[a-f0-9]{16}$/.test(value);
const integer=(value,min,max)=>Number.isSafeInteger(value)&&value>=min&&value<=max;
const exact=(value,keys)=>value!==null&&typeof value==='object'&&!Array.isArray(value)&&
    Object.keys(value).length===keys.length&&keys.every(key=>Object.hasOwn(value,key));
const monitor=value=>exact(value,['x','y','width','height'])&&integer(value.x,-1000000,1000000)&&integer(value.y,-1000000,1000000)&&
    integer(value.width,256,32768)&&integer(value.height,148,32768);
const sourceId=value=>typeof value==='string'&&/^portal:[a-f0-9]{64}$/.test(value);

// Logical monitor geometry is not identity. Only a fresh marker observed in an
// authorized source frame can bind the public source hash to that geometry.
export class SourceMapping {
    #generation=0;#challenge=null;#binding=null;
    challenge(owner,monitors,randomNonce){
        this.invalidate();
        if(!validOwner(owner)||!Array.isArray(monitors)||monitors.length<1||monitors.length>64||!monitors.every(monitor))
            throw new Error('mapping_unavailable');
        const markers=[];
        for(const m of monitors){
            const value=randomNonce();
            if(!nonce(value)||markers.some(marker=>marker.nonce===value))throw new Error('mapping_unavailable');
            markers.push({...m,nonce:value});
        }
        const generation=this.#generation;
        this.#challenge={owner,generation,markers};
        const candidates=[...new Map(monitors.map(m=>[`${m.width}:${m.height}`,{width:m.width,height:m.height}])).values()];
        return {view:{generation,candidates,marker:{left:64,top:100,cell:12,columns:16,rows:4}},markers:markers.map(m=>({...m}))};
    }
    bind(owner,proof){
        const challenge=this.#challenge;
        if(!challenge||challenge.owner!==owner)return null;
        this.#challenge=null; // One attempt; an invalid proof cannot be replayed.
        if(!exact(proof,['generation','sourceId','sourceWidth','sourceHeight','observations'])||proof.generation!==challenge.generation||
            !sourceId(proof.sourceId)||!integer(proof.sourceWidth,2,16384)||!integer(proof.sourceHeight,2,16384)||
            !Array.isArray(proof.observations)||proof.observations.length<1||proof.observations.length>64||
            !proof.observations.every(o=>exact(o,['nonce','width','height'])&&nonce(o.nonce)&&integer(o.width,256,32768)&&integer(o.height,148,32768)))return null;
        const matches=challenge.markers.filter(m=>proof.observations.some(o=>o.nonce===m.nonce&&o.width===m.width&&o.height===m.height));
        if(matches.length!==1)return null;
        const {x,y,width,height}=matches[0];
        this.#binding={owner,generation:challenge.generation,sourceId:proof.sourceId,sourceWidth:proof.sourceWidth,sourceHeight:proof.sourceHeight,
            x,y,width,height};
        const {owner:_owner,...view}=this.#binding;return view;
    }
    resolve(owner,source){
        const b=this.#binding;
        if(!b||b.owner!==owner||!validSource(source)||source.id!==b.sourceId||source.generation!==b.generation||
            source.monitorWidth!==b.sourceWidth||source.monitorHeight!==b.sourceHeight)return null;
        return {x:b.x+source.x*b.width/b.sourceWidth,y:b.y+source.y*b.height/b.sourceHeight,
            width:source.width*b.width/b.sourceWidth,height:source.height*b.height/b.sourceHeight};
    }
    cancelChallenge(owner){if(this.#challenge?.owner===owner)this.#challenge=null;}
    ownerLost(owner){if(this.#challenge?.owner===owner||this.#binding?.owner===owner)this.invalidate();}
    invalidate(){
        this.#challenge=null;this.#binding=null;
        if(this.#generation>=2147483647)throw new Error('mapping_unavailable');
        this.#generation++;
    }
}
