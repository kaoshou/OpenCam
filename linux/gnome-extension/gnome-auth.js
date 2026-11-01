// SPDX-License-Identifier: AGPL-3.0-or-later
import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import * as Dialog from 'resource:///org/gnome/shell/ui/dialog.js';
import * as ModalDialog from 'resource:///org/gnome/shell/ui/modalDialog.js';
import {PeerAuthorization} from './auth.js';
import {validOwner} from './protocol.js';
import {disposeAll} from './cleanup.js';

// Credentials are obtained from the bus daemon, never a request's JSON fields.
export function lookupPeer(connection,owner){
    if(!validOwner(owner))return Promise.reject(new Error('peer_unavailable'));
    return new Promise((resolve,reject)=>{
        connection.call('org.freedesktop.DBus','/org/freedesktop/DBus','org.freedesktop.DBus','GetConnectionCredentials',
            new GLib.Variant('(s)',[owner]),new GLib.VariantType('(a{sv})'),Gio.DBusCallFlags.NONE,3000,null,(_connection,result)=>{
                try{
                    const values=connection.call_finish(result).deep_unpack()[0];
                    const number=value=>value instanceof GLib.Variant?value.deep_unpack():value;
                    const uid=number(values.UnixUserID),pid=number(values.ProcessID);
                    if(uid!==new Gio.Credentials().get_unix_user()||!Number.isSafeInteger(pid)||pid<=0||pid>2147483647)throw new Error();
                    const executable=GLib.file_read_link(`/proc/${pid}/exe`);
                    resolve({uid,pid,executable});
                }catch{reject(new Error('peer_unavailable'));}
            });
    });
}

function confirmPeer(peer,setCancel){
    return new Promise(resolve=>{
        let dialog=null,timer=0,finished=false;
        const finish=allowed=>{
            if(finished)return;finished=true;
            if(timer){GLib.source_remove(timer);timer=0;}
            const confirmed=disposeAll([()=>dialog?.popModal(),()=>dialog?.close(),()=>dialog?.destroy()]);
            dialog=null;resolve(allowed===true&&confirmed);
        };
        try{
            dialog=new ModalDialog.ModalDialog({styleClass:'access-dialog',destroyOnClose:false});
            const content=new Dialog.MessageDialogContent({title:'Connect to OpenCam teaching tools?',
                description:`This process can show a teaching pointer and receive drawing input while annotations are active.\n\nProcess: ${peer.executable}\nPID: ${peer.pid}\n\nOnly allow a process you trust. This does not grant remote control.`});
            dialog.contentLayout.add_child(content);
            dialog.addButton({label:'Deny',action:()=>finish(false),key:Clutter.KEY_Escape,default:true});
            dialog.addButton({label:'Allow',action:()=>finish(true)});
            timer=GLib.timeout_add(GLib.PRIORITY_DEFAULT,30000,()=>{timer=0;finish(false);return GLib.SOURCE_REMOVE;});
            setCancel(()=>finish(false));
            if(!finished&&!dialog.open())finish(false);
        }catch{finish(false);}
    });
}
export function createAuthorization(connection){
    return new PeerAuthorization({uid:new Gio.Credentials().get_unix_user(),now:()=>GLib.get_monotonic_time()/1000,
        lookup:owner=>lookupPeer(connection,owner),confirm:confirmPeer});
}
