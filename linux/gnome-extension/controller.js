// SPDX-License-Identifier: AGPL-3.0-or-later
import {SourceMapping} from './mapping.js';
import {TeachingSession} from './session.js';
import {parseRequest} from './protocol.js';
import {disposeAll} from './cleanup.js';

// Combines consent, source proof and one renderer owner. Transport supplies the
// actual unique sender; never use a request field as the caller's identity.
export class TeachingController {
    #authorization;#mapping=new SourceMapping();#session;#epoch=0;#disposed=false;
    #readMonitors;#randomNonce;#showMarkers;#clearMarkers;#fault;
    #markerOwner=null;#binding=null;#beginning=null;#commands;
    constructor({authorization,now,readMonitors,randomNonce,showMarkers,clearMarkers,activate,onFault=()=>{},commands=false}){
        this.#authorization=authorization;this.#readMonitors=readMonitors;this.#randomNonce=randomNonce;
        this.#showMarkers=showMarkers;this.#clearMarkers=clearMarkers;this.#commands=commands;
        this.#fault=code=>{try{onFault(code);}catch{}};
        this.#session=new TeachingSession({now,onFault:this.#fault,
            authorize:async(owner,source)=>await this.#authorization.check(owner)&&this.#mapping.resolve(owner,source)!==null,
            activate:async(source,options,owner)=>{
                const bounds=this.#mapping.resolve(owner,source);
                if(!bounds)throw new Error('mapping_unavailable');
                return activate(bounds,options,owner);
            }});
    }
    get active(){return this.#session.active;}
    tick(){this.#session.tick();}
    async connect(owner){
        if(this.#disposed)return false;
        const epoch=this.#epoch;
        if(this.#session.active||this.#beginning)return this.#authorization.check(owner);
        const accepted=await this.#authorization.request(owner);
        return accepted===true&&!this.#disposed&&this.#epoch===epoch;
    }
    #clearOwnedMarkers(owner){
        if(this.#markerOwner!==owner)return;
        this.#markerOwner=null;
        try{this.#clearMarkers();}catch{this.#fault('marker_cleanup_unconfirmed');}
    }
    async challenge(owner){
        const epoch=this.#epoch;
        if(this.#disposed||this.#session.active||this.#beginning||this.#markerOwner||!await this.#authorization.check(owner)||
            this.#disposed||epoch!==this.#epoch)return null;
        this.#binding=null;
        try{
            const {view,markers}=this.#mapping.challenge(owner,this.#readMonitors(),this.#randomNonce);
            this.#markerOwner=owner;this.#showMarkers(markers);
            return view;
        }catch{this.#mapping.cancelChallenge(owner);this.#clearOwnedMarkers(owner);return null;}
    }
    async bind(owner,proof){
        const epoch=this.#epoch;
        if(this.#disposed||this.#markerOwner!==owner||!await this.#authorization.check(owner)||this.#disposed||epoch!==this.#epoch)return null;
        try{
            const view=this.#mapping.bind(owner,proof);
            this.#binding=view?{...view,owner}:null;
            return view;
        }finally{this.#clearOwnedMarkers(owner);}
    }
    cancelChallenge(owner){this.#mapping.cancelChallenge(owner);this.#clearOwnedMarkers(owner);}
    async request(owner,text){
        if(this.#disposed)return false;
        let request;
        try{request=parseRequest(text);}catch{return false;}
        const epoch=this.#epoch;
        if(!await this.#authorization.check(owner)||this.#disposed||epoch!==this.#epoch)return false;
        switch(request.action){
        case 'begin': {
            if(this.#beginning)return false;
            const pending={owner};this.#beginning=pending;
            try{return await this.#session.begin(owner,request.nonce,request.source,request.options);}
            finally{if(this.#beginning===pending)this.#beginning=null;}
        }
        case 'renew':return this.#session.renew(owner,request.nonce);
        case 'end':return this.#session.end(owner,request.nonce);
        case 'command':return this.#commands?this.#session.command(owner,request.nonce,request.command):false;
        default:return false;
        }
    }
    async status(owner){
        if(this.#disposed||!await this.#authorization.check(owner))return null;
        const binding=this.#binding?.owner===owner?this.#binding:null;
        return {active:binding!==null&&this.#session.active,sourceId:binding?.sourceId??null,generation:binding?.generation??null};
    }
    ownerLost(owner){
        if(this.#markerOwner===owner||this.#binding?.owner===owner||this.#beginning?.owner===owner){
            this.#epoch++;if(this.#binding?.owner===owner)this.#binding=null;
            if(this.#beginning?.owner===owner)this.#beginning=null;
        }
        this.#authorization.ownerLost(owner);this.#session.ownerLost(owner);this.#mapping.ownerLost(owner);this.#clearOwnedMarkers(owner);
    }
    invalidate(){
        this.#epoch++;this.#binding=null;this.#beginning=null;
        const markerOwner=this.#markerOwner;
        if(!disposeAll([()=>this.#authorization.invalidate(),()=>this.#session.invalidate(),()=>this.#mapping.invalidate(),
            ()=>{if(markerOwner)this.#clearOwnedMarkers(markerOwner);}]))this.#fault('cleanup_unconfirmed');
    }
    dispose(){
        if(this.#disposed)return;this.#disposed=true;this.invalidate();
        if(!disposeAll([()=>this.#authorization.dispose(),()=>this.#session.dispose()]))this.#fault('cleanup_unconfirmed');
    }
}
