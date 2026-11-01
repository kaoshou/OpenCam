// SPDX-License-Identifier: AGPL-3.0-or-later
import {pointerGeometry} from './geometry.js';
const record=value=>value!==null&&typeof value==='object'&&!Array.isArray(value)&&Object.getPrototypeOf(value)===Object.prototype;
function keys(value,expected){return record(value)&&Object.keys(value).length===expected.length&&expected.every(k=>Object.hasOwn(value,k));}
const integer=(value,min,max)=>Number.isSafeInteger(value)&&value>=min&&value<=max;
export const validOwner=value=>typeof value==='string'&&/^:[0-9]{1,10}\.[0-9]{1,10}$/.test(value);
export const validNonce=value=>typeof value==='string'&&/^[a-f0-9]{64}$/.test(value);
export function sameNonce(a,b){
    if(!validNonce(a)||!validNonce(b))return false;
    let difference=0;for(let i=0;i<64;i++)difference|=a.charCodeAt(i)^b.charCodeAt(i);
    return difference===0; // Bounded comparison; no timing-security claim about JIT.
}
export function validSource(value){
    return keys(value,['id','generation','monitorWidth','monitorHeight','x','y','width','height'])&&
        typeof value.id==='string'&&/^portal:[a-f0-9]{64}$/.test(value.id)&&integer(value.generation,1,2147483647)&&
        integer(value.monitorWidth,1,16384)&&integer(value.monitorHeight,1,16384)&&
        integer(value.x,0,16383)&&integer(value.y,0,16383)&&integer(value.width,1,16384)&&integer(value.height,1,16384)&&
        value.x+value.width<=value.monitorWidth&&value.y+value.height<=value.monitorHeight;
}
export function validOptions(value){
    if(!keys(value,['style','size']))return false;
    try{pointerGeometry(value.style,value.size);return true;}catch{return false;}
}
export function validCommand(value){
    if(!record(value))return false;
    if(['toggleToolbar','togglePen','finish','clear','undo','redo'].includes(value.name))return keys(value,['name']);
    if(value.name==='tool')return keys(value,['name','tool'])&&['pen','line','arrow','rectangle','ellipse','eraser'].includes(value.tool);
    if(value.name==='color')return keys(value,['name','color'])&&typeof value.color==='string'&&/^#[a-fA-F0-9]{6}$/.test(value.color);
    if(value.name==='width')return keys(value,['name','width'])&&[2,4,8].includes(value.width);
    return false;
}
export function parseRequest(text){
    try{
        if(typeof text!=='string'||text.length>65536)throw new Error();
        let bytes=0;
        for(const c of text){const n=c.codePointAt(0);bytes+=n<128?1:n<2048?2:n<65536?3:4;if(bytes>65536)throw new Error();}
        const value=JSON.parse(text);
        if(!record(value)||value.protocol!==1||!validNonce(value.nonce))throw new Error();
        if(value.action==='begin'){
            if(!keys(value,['protocol','action','nonce','source','options'])||!validSource(value.source)||!validOptions(value.options))throw new Error();
        }else if(value.action==='command'){
            if(!keys(value,['protocol','action','nonce','command'])||!validCommand(value.command))throw new Error();
        }else if(value.action==='renew'||value.action==='end'){
            if(!keys(value,['protocol','action','nonce']))throw new Error();
        }else throw new Error();
        return value;
    }catch{throw new Error('invalid_request');}
}
