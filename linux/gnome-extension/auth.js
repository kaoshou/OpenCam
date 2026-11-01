// SPDX-License-Identifier: AGPL-3.0-or-later
import {validOwner} from './protocol.js';
function validPeer(peer,uid){
    return peer?.uid===uid&&Number.isSafeInteger(peer.pid)&&peer.pid>0&&peer.pid<=2147483647&&
        typeof peer.executable==='string'&&peer.executable.startsWith('/')&&peer.executable.length<=4096&&
        !/[\u0000-\u001f\u007f]/.test(peer.executable);
}
const equalPeer=(a,b)=>a?.uid===b?.uid&&a?.pid===b?.pid&&a?.executable===b?.executable;
export class PeerAuthorization {
    #uid;#now;#lookup;#confirm;#pending=null;#approved=null;#disposed=false;#nextPrompt=0;
    constructor({uid,now,lookup,confirm}){this.#uid=uid;this.#now=now;this.#lookup=lookup;this.#confirm=confirm;}
    async check(owner){
        const approved=this.#approved;
        if(this.#disposed||!approved||approved.owner!==owner)return false;
        try{
            const peer=await this.#lookup(owner);
            if(!this.#disposed&&this.#approved===approved&&validPeer(peer,this.#uid)&&equalPeer(approved.peer,peer))return true;
        }catch{/* A missing/disconnected process is not renewed authorization. */}
        if(this.#approved===approved)this.#approved=null;
        return false;
    }
    async request(owner){
        if(this.#disposed||this.#pending||!validOwner(owner))return false;
        const pending={owner,cancel:null};this.#pending=pending;
        try{
            const peer=await this.#lookup(owner);
            if(this.#disposed||this.#pending!==pending||!validPeer(peer,this.#uid))return false;
            if(this.#approved?.owner===owner&&equalPeer(this.#approved.peer,peer))return true;
            if(this.#now()<this.#nextPrompt)return false;
            this.#nextPrompt=this.#now()+1000;
            const accepted=await this.#confirm({...peer},cancel=>{
                if(typeof cancel!=='function')return;
                if(this.#pending!==pending){try{cancel();}catch{}return;}
                pending.cancel=cancel;
            });
            if(accepted!==true||this.#disposed||this.#pending!==pending)return false;
            const rechecked=await this.#lookup(owner);
            if(this.#disposed||this.#pending!==pending||!validPeer(rechecked,this.#uid)||!equalPeer(peer,rechecked))return false;
            this.#approved={owner,peer:{...rechecked}};return true;
        }catch{return false;}
        finally{if(this.#pending===pending)this.#pending=null;}
    }
    ownerLost(owner){
        if(this.#approved?.owner===owner)this.#approved=null;
        if(this.#pending?.owner===owner){
            const pending=this.#pending;this.#pending=null;
            try{pending.cancel?.();}catch{/* Consent cancellation must not retain ownership. */}
        }
    }
    invalidate(){this.#approved=null;const pending=this.#pending;if(pending)this.ownerLost(pending.owner);}
    dispose(){if(this.#disposed)return;this.#disposed=true;this.invalidate();}
}
