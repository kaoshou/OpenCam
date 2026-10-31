# SPDX-License-Identifier: AGPL-3.0-or-later
"""Controlled CI desktop only: activate actual accessible portal chooser controls."""
import os
import sys
import time
import pyatspi

if os.environ.get("OPENCAM_DISPOSABLE_CI_DESKTOP") != "1":
    sys.exit(2)

def walk(node, depth=0):
    if depth > 30:
        return []
    found = [node]
    for child in node:
        if child:
            found.extend(walk(child, depth + 1))
    return found

desktop = pyatspi.Registry.getDesktop(0)
for attempt in range(8):
    for app in desktop:
        if not app or "portal" not in (app.name or "").lower():
            continue
        controls = walk(app)
        for control in controls:
            print(control.getRoleName(), repr(control.name), flush=True)
        for control in controls:
            name = (control.name or "").lower().replace("_", "")
            if control.getRoleName() in ("check box", "radio button") and ("monitor" in name or "screen" in name or "remember" in name):
                try:
                    if not control.getState().contains(pyatspi.STATE_CHECKED):
                        control.queryAction().doAction(0)
                except (NotImplementedError, AttributeError):
                    pass
        for control in controls:
            if (control.name or "").lower().replace("_", "") in ("share", "allow") and control.getRoleName() in ("push button", "button"):
                if control.getState().contains(pyatspi.STATE_SENSITIVE):
                    control.queryAction().doAction(0)
                    sys.exit(0)
    time.sleep(.5)
sys.exit(1)
