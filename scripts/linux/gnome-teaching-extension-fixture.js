// SPDX-License-Identifier: AGPL-3.0-or-later
// Test extension entry point, NEVER included in the product extension package.
// Creates synthetic input only in the disposable CI compositor, not user desktops.
import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import {createPointer} from './pointer.js';
import {TeachingSession} from './session.js';
import {parseRequest} from './protocol.js';
import {createAuthorization} from './gnome-auth.js';
const xml=`<node><interface name="io.github.kaoshou.OpenCam.TeachingFixture">
<method name="Phase"><arg type="s" direction="in"/><arg type="s" direction="out"/></method>
<method name="Status"><arg type="s" direction="out"/></method>
<method name="Request"><arg type="s" direction="in"/><arg type="b" direction="out"/></method>
<method name="Consent"><arg type="b" direction="out"/></method>
</interface></node>`;
export default class TeachingFixture extends Extension {
    enable() {
        if(GLib.getenv('OPENCAM_DISPOSABLE_CI_DESKTOP')!=='1')throw new Error('disposable_desktop_required');
        this._actors=[];this._phase='inert';this._ticks=0;this._error=null;
        this._authorization=createAuthorization(Gio.DBus.session);
        this._session=new TeachingSession({now:()=>GLib.get_monotonic_time()/1000,
            // Test-only authorization boundary, NOT the product consent UI.
            // Actual caller identity comes from the bus invocation below.
            authorize:async()=>true,
            activate:async()=>{
                const pointer=createPointer({style:'WarmIvory',size:'Standard',artworkDirectory:`${this.path}/artwork`,bounds:Main.layoutManager.monitors[0]});
                this._pointer=pointer;
                return {command:()=>{},dispose:()=>{pointer.destroy();if(this._pointer===pointer)this._pointer=null;}};
            },onFault:code=>this._error=code});
        this._ownerSignal=Gio.DBus.session.signal_subscribe('org.freedesktop.DBus','org.freedesktop.DBus','NameOwnerChanged',
            '/org/freedesktop/DBus',null,Gio.DBusSignalFlags.NONE,(_connection,_sender,_path,_iface,_signal,parameters)=>{
                const [name,_oldOwner,newOwner]=parameters.deep_unpack();
                if(name.startsWith(':')&&newOwner==='')this._session.ownerLost(name);
                if(name.startsWith(':')&&newOwner==='')this._authorization.ownerLost(name);
            });
        this._device=Clutter.get_default_backend().get_default_seat().create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        this._object=Gio.DBusExportedObject.wrapJSObject(xml,this);
        this._object.export(Gio.DBus.session,'/io/github/kaoshou/OpenCam/TeachingFixture');
        this._timer=GLib.timeout_add(GLib.PRIORITY_DEFAULT,16,()=>{
            try{
                this._session.tick();
                if(this._phase.startsWith('consent-')){
                    this._clickConsentButton();return GLib.SOURCE_CONTINUE;
                }
                if(this._phase!=='inert'){
                    const positions=[[180,150],[240,150],[300,150]];
                    let [x,y]=positions[Math.floor(this._ticks++/25)%positions.length];
                    if(this._phase==='outside')x+=Main.layoutManager.monitors[1].x;
                    this._device.notify_absolute_motion(GLib.get_monotonic_time(),x,y);
                    const [px,py]=global.get_pointer();this._pointer?.showAt(px,py);
                }
            }catch{this._error='pointer_operation_failed';this._pointer?.destroy();this._pointer=null;}
            return GLib.SOURCE_CONTINUE;
        });
    }
    Status() {
        return JSON.stringify({phase:this._phase,error:this._error,visible:Meta.CursorTracker.get_for_display(global.display).get_pointer_visible(),
            monitors:Main.layoutManager.monitors.map(m=>({x:m.x,y:m.y,width:m.width,height:m.height})),
            markers:this._markers??[],ticks:this._ticks,leaseActive:this._session.active,modalCount:Main.modalCount,
            consentPressed:this._consentPressed??false,consentReleased:this._consentReleased??false,consentButton:this._consentButton??null});
    }
    async ConsentAsync(_params,invocation){
        const allowed=await this._authorization.request(invocation.get_sender());
        invocation.return_value(new GLib.Variant('(b)',[allowed]));
    }
    _clickConsentButton(){
        // Real synthetic input into the actual owned modal, not a consent bypass.
        if(this._consentReleased||!Main.layoutManager.modalDialogGroup)return;
        if(this._consentPressed){
            this._device.notify_button(GLib.get_monotonic_time(),1,Clutter.ButtonState.RELEASED);
            this._consentReleased=true;return;
        }
        const desired=this._phase==='consent-allow'?'Allow':'Deny';
        let visited=0;
        const find=actor=>{
            if(++visited>1000)return null;
            if(actor instanceof St.Button&&actor.label===desired&&actor.mapped&&actor.get_paint_opacity()>=250)return actor;
            for(const child of actor.get_children()){const button=find(child);if(button)return button;}
            return null;
        };
        const button=find(Main.layoutManager.modalDialogGroup);
        if(button){
            const [x,y]=button.get_transformed_position(),[width,height]=button.get_transformed_size();
            this._consentButton={x,y,width,height};
            this._device.notify_absolute_motion(GLib.get_monotonic_time(),x+width/2,y+height/2);
            this._device.notify_button(GLib.get_monotonic_time(),1,Clutter.ButtonState.PRESSED);this._consentPressed=true;
        }
    }
    async RequestAsync([text],invocation) {
        let accepted=false;
        try{
            const request=parseRequest(text),owner=invocation.get_sender();
            if(request.action==='begin'&&this._phase.startsWith('lease-'))
                accepted=await this._session.begin(owner,request.nonce,request.source,request.options);
            else if(request.action==='renew')accepted=this._session.renew(owner,request.nonce);
            else if(request.action==='end')accepted=this._session.end(owner,request.nonce);
            else if(request.action==='command')accepted=await this._session.command(owner,request.nonce,request.command);
        }catch{/* Do not return arbitrary request/exception contents. */}
        invocation.return_value(new GLib.Variant('(b)',[accepted]));
    }
    Phase(phase) {
        if(!['baseline','pointer','stroke','outside','cleanup','markers','inert','lease-owner','lease-expire','consent-deny','consent-allow'].includes(phase))throw new Error('invalid_phase');
        Main.overview.hide();
        this._session.invalidate();
        this._pointer?.destroy();this._pointer=null;
        for(const actor of this._actors)actor.destroy();this._actors=[];this._markers=[];
        this._phase=phase;
        this._consentPressed=false;this._consentReleased=false;this._consentButton=null;
        if(['pointer','stroke','outside'].includes(phase)){
            this._pointer=createPointer({style:'WarmIvory',size:'Standard',artworkDirectory:`${this.path}/artwork`,bounds:Main.layoutManager.monitors[0]});
            if(phase==='stroke'){
                const stroke=new St.Widget({reactive:false,x:80,y:250,width:400,height:8,style:'background-color: rgb(240,40,40);'});
                Main.uiGroup.add_child(stroke);this._actors.push(stroke);
            }
        }else if(phase==='markers'){
            // Random per-monitor visual challenge, observed through authorized capture.
            for(const monitor of Main.layoutManager.monitors){
                const nonce=GLib.uuid_string_random().replaceAll('-','').slice(0,16);
                this._markers.push(nonce);
                for(let bit=0;bit<64;bit++){
                    const on=(parseInt(nonce[Math.floor(bit/4)],16)&(1<<(bit%4)))!==0;
                    const actor=new St.Widget({reactive:false,x:monitor.x+64+(bit%16)*12,y:monitor.y+100+Math.floor(bit/16)*12,width:12,height:12,
                        style:`background-color: ${on?'rgb(32,220,32)':'rgb(32,32,220)'};`});
                    Main.uiGroup.add_child(actor);this._actors.push(actor);
                }
            }
        }
        return this.Status();
    }
    disable() {
        if(this._timer){GLib.source_remove(this._timer);this._timer=0;}
        this._object?.unexport();this._object=null;
        if(this._ownerSignal){Gio.DBus.session.signal_unsubscribe(this._ownerSignal);this._ownerSignal=0;}
        this._session?.dispose();this._session=null;
        this._authorization?.dispose();this._authorization=null;
        this._pointer?.destroy();this._pointer=null;
        for(const actor of this._actors??[])actor.destroy();this._actors=[];
        this._device=null;
    }
}
