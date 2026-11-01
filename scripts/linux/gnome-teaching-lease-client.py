# SPDX-License-Identifier: AGPL-3.0-or-later
"""Owned disposable CI client; an actual unique bus connection, no fake sender."""
import json
import os
import sys
import gi
from gi.repository import Gio, GLib
if os.environ.get("OPENCAM_DISPOSABLE_CI_DESKTOP") != "1" or len(sys.argv) != 3:
    sys.exit(2)
bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
nonce = os.urandom(32).hex()

def request(action, **fields):
    payload = json.dumps(dict(protocol=1, action=action, nonce=nonce, **fields))
    result = bus.call_sync("org.gnome.Shell", "/io/github/kaoshou/OpenCam/TeachingFixture",
                           "io.github.kaoshou.OpenCam.TeachingFixture", "Request",
                           GLib.Variant("(s)", [payload]), GLib.VariantType.new("(b)"),
                           Gio.DBusCallFlags.NONE, 5000, None)
    return result.unpack()[0]

try:
    if sys.argv[2].startswith("consent-"):
        result = bus.call_sync("org.gnome.Shell", "/io/github/kaoshou/OpenCam/TeachingFixture",
                               "io.github.kaoshou.OpenCam.TeachingFixture", "Consent", None,
                               GLib.VariantType.new("(b)"), Gio.DBusCallFlags.NONE, 15000, None)
        print("allowed" if result.unpack()[0] else "denied", flush=True)
        sys.exit(0)
    if not request("begin", source=json.loads(sys.argv[1]), options=dict(style="WarmIvory", size="Standard")):
        sys.exit(1)
    print("ready", flush=True)
    if sys.argv[2] == "renew":
        def renew():
            try:
                return request("renew")
            except Exception:
                return False
        GLib.timeout_add(1000, renew)
    GLib.MainLoop().run()
except Exception:
    print("lease_client_failed", file=sys.stderr)
    sys.exit(1)
