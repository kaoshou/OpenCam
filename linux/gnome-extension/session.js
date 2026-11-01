// SPDX-License-Identifier: AGPL-3.0-or-later
import {validOwner,validNonce,validSource,validOptions,validCommand,sameNonce} from './protocol.js';

// Transport-free policy: the adapter supplies actual sender credentials/consent
// and validated source mapping. A peer-provided owner or geometry is never proof.
export class TeachingSession {
    #current=null;#pending=null;#disposed=false;#seen=[];#queued=0;#chain=Promise.resolve();
    #now;#authorize;#activate;#onFault;
    constructor({now,authorize,activate,onFault=()=>{}}){
        this.#now=now;this.#authorize=authorize;this.#activate=activate;this.#onFault=onFault;
    }
    get active(){this.tick();return this.#current!==null;}
    #fault(code){try{this.#onFault(code);}catch{/* Never leak arbitrary observer exceptions. */}}
    #release(resource){try{resource.dispose();}catch{this.#fault('cleanup_unconfirmed');}}
    #close(){const current=this.#current;this.#current=null;if(current)this.#release(current.resource);}
    tick(){if(this.#current&&this.#now()>=this.#current.deadline)this.#close();}
    #matches(owner,nonce){this.tick();return !!this.#current&&this.#current.owner===owner&&sameNonce(this.#current.nonce,nonce);}
    async begin(owner,nonce,source,options){
        this.tick();
        if(this.#disposed||this.#pending||this.#current||!validOwner(owner)||!validNonce(nonce)||!validSource(source)||!validOptions(options)||
            this.#seen.some(n=>sameNonce(n,nonce)))return false;
        const pending={owner,nonce};this.#pending=pending;
        // Remember bounded recent nonces. Authorization still pins the actual
        // unique bus sender, not a replayable caller-supplied identity.
        this.#seen.push(nonce);if(this.#seen.length>128)this.#seen.shift();
        const screen={...source},pointer={...options};
        try{
            if(await this.#authorize(owner,screen)!==true||this.#disposed||this.#pending!==pending)return false;
            const resource=await this.#activate(screen,pointer);
            if(!resource||typeof resource.dispose!=='function'||typeof resource.command!=='function'){
                if(resource&&typeof resource.dispose==='function')this.#release(resource);
                this.#fault('renderer_failed');return false;
            }
            if(this.#disposed||this.#pending!==pending){this.#release(resource);return false;}
            this.#current={owner,nonce,source:screen,resource,deadline:this.#now()+3000};
            return true;
        }catch{this.#fault('activation_failed');return false;}
        finally{if(this.#pending===pending)this.#pending=null;}
    }
    renew(owner,nonce){if(!this.#matches(owner,nonce))return false;this.#current.deadline=this.#now()+3000;return true;}
    command(owner,nonce,command){
        if(!validCommand(command)||!this.#matches(owner,nonce)||this.#queued>=128)return Promise.resolve(false);
        const current=this.#current,copy={...command};this.#queued++;
        const result=this.#chain.then(()=>{
            if(!this.#matches(owner,nonce)||this.#current!==current)return false;
            try{
                const value=current.resource.command(copy);
                // Drawing updates are synchronous compositor work; never let an
                // unowned late continuation draw after cleanup.
                if(value&&typeof value.then==='function')throw new Error('async_renderer');
                return true;
            }catch{this.#close();this.#fault('renderer_failed');return false;}
        });
        this.#chain=result.then(()=>{},()=>{});
        return result.finally(()=>this.#queued--);
    }
    end(owner,nonce){if(!this.#matches(owner,nonce))return false;this.#close();return true;}
    ownerLost(owner){
        if(this.#pending?.owner===owner)this.#pending=null;
        if(this.#current?.owner===owner)this.#close();
    }
    invalidate(){this.#pending=null;this.#close();}
    dispose(){if(this.#disposed)return;this.#disposed=true;this.invalidate();this.#seen=[];}
}
