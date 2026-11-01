// SPDX-License-Identifier: AGPL-3.0-or-later
import Clutter from 'gi://Clutter';
import Cogl from 'gi://Cogl';
import GdkPixbuf from 'gi://GdkPixbuf';
import Gio from 'gi://Gio';
import Meta from 'gi://Meta';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {pointerGeometry,pointerPlacement} from './geometry.js';

// Owns only its actor and balanced cursor/unfocus operations. Local compositor
// calls only: no timers, IPC, input injection or captured media in this module.
export function createPointer({style,size,artworkDirectory,bounds}) {
    const geometry=pointerGeometry(style,size);
    const file=Gio.File.new_for_path(artworkDirectory).get_child(`${style}.png`);
    const pixbuf=GdkPixbuf.Pixbuf.new_from_file(file.get_path());
    if(pixbuf.get_width()!==280||pixbuf.get_height()!==240||!pixbuf.get_has_alpha())
        throw new Error('invalid_pointer_artwork');
    const content=new Clutter.Image();
    content.set_data(pixbuf.get_pixels(),Cogl.PixelFormat.RGBA_8888,280,240,pixbuf.get_rowstride());
    const actor=new St.Widget({reactive:false,can_focus:false,width:geometry.width,height:geometry.height,content,visible:false});
    Main.uiGroup.add_child(actor);
    const tracker=Meta.CursorTracker.get_for_display(global.display);
    const seat=Clutter.get_default_backend().get_default_seat();
    let hidden=false,signal=0,destroyed=false;
    function restore() {
        if(signal){tracker.disconnect(signal);signal=0;}
        if(hidden){hidden=false;tracker.set_pointer_visible(true);seat.uninhibit_unfocus();}
    }
    function hide() {
        if(hidden)return;
        // GNOME's own magnifier uses the same balanced unfocus inhibition.
        // Never take ownership of another component's already-hidden cursor.
        if(!tracker.get_pointer_visible())throw new Error('cursor_owned_elsewhere');
        seat.inhibit_unfocus();hidden=true;
        signal=tracker.connect('visibility-changed',()=>{
            if(hidden&&tracker.get_pointer_visible())tracker.set_pointer_visible(false);
        });
        tracker.set_pointer_visible(false);
    }
    return {
        showAt(x,y) {
            if(destroyed)return;
            const position=pointerPlacement(geometry,x,y,bounds);
            if(!position){actor.hide();restore();return;}
            actor.set_position(position.x,position.y);actor.show();
            try{hide();}catch(error){actor.hide();restore();throw error;}
        },
        destroy() {
            if(destroyed)return;
            destroyed=true;
            try{restore();}finally{actor.destroy();}
        },
    };
}
