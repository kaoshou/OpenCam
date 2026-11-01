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
const xml=`<node><interface name="io.github.kaoshou.OpenCam.TeachingFixture">
<method name="Phase"><arg type="s" direction="in"/><arg type="s" direction="out"/></method>
<method name="Status"><arg type="s" direction="out"/></method>
</interface></node>`;
export default class TeachingFixture extends Extension {
    enable() {
        if(GLib.getenv('OPENCAM_DISPOSABLE_CI_DESKTOP')!=='1')throw new Error('disposable_desktop_required');
        this._actors=[];this._phase='inert';this._ticks=0;this._error=null;
        this._device=Clutter.get_default_backend().get_default_seat().create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        this._object=Gio.DBusExportedObject.wrapJSObject(xml,this);
        this._object.export(Gio.DBus.session,'/io/github/kaoshou/OpenCam/TeachingFixture');
        this._timer=GLib.timeout_add(GLib.PRIORITY_DEFAULT,16,()=>{
            try{
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
            markers:this._markers??[],ticks:this._ticks});
    }
    Phase(phase) {
        if(!['baseline','pointer','stroke','outside','cleanup','markers','inert'].includes(phase))throw new Error('invalid_phase');
        Main.overview.hide();
        this._pointer?.destroy();this._pointer=null;
        for(const actor of this._actors)actor.destroy();this._actors=[];this._markers=[];
        this._phase=phase;
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
        this._pointer?.destroy();this._pointer=null;
        for(const actor of this._actors??[])actor.destroy();this._actors=[];
        this._device=null;
    }
}
