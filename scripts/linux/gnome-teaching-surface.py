# SPDX-License-Identifier: AGPL-3.0-or-later
"""A generated Wayland client surface with a distinctive native cursor."""
import os
import sys
import gi
if os.environ.get("OPENCAM_DISPOSABLE_CI_DESKTOP") != "1":
    sys.exit(2)
gi.require_version("Gtk", "4.0")
from gi.repository import Gtk, Gdk, GLib

class Surface(Gtk.Application):
    def do_activate(self):
        window = Gtk.ApplicationWindow(application=self, title="OpenCam Generated Test Surface")
        area = Gtk.DrawingArea()
        area.set_draw_func(lambda widget, cr, width, height: (cr.set_source_rgb(.12, .25, .38), cr.paint()))
        texture = Gdk.MemoryTexture.new(16, 16, Gdk.MemoryFormat.R8G8B8A8, GLib.Bytes.new(bytes([255, 0, 255, 255]) * 256), 64)
        area.set_cursor(Gdk.Cursor.new_from_texture(texture, 0, 0, None))
        window.set_child(area)
        window.fullscreen()
        window.present()

Surface(application_id="io.github.kaoshou.OpenCam.TestSurface").run(sys.argv)
