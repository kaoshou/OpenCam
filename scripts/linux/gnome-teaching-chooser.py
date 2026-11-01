# SPDX-License-Identifier: AGPL-3.0-or-later
"""Select exactly one real monitor in the disposable GNOME portal chooser."""
import os
import sys
import time
import pyatspi
if os.environ.get("OPENCAM_DISPOSABLE_CI_DESKTOP") != "1":
    sys.exit(2)

def walk(node, depth=0):
    if depth > 30:
        return []
    result = [node]
    for child in node:
        if child:
            result.extend(walk(child, depth + 1))
    return result

def screen_choice(control):
    # This fixture requests monitor-only capture. GNOME exposes its monitor
    # choices as toggles; the Entire Screen label is not their AT-SPI ancestor.
    return control.getRoleName() == "toggle button"

for attempt in range(12):
    for app in pyatspi.Registry.getDesktop(0):
        if not app or "portal" not in (app.name or "").lower():
            continue
        controls = walk(app)
        if attempt == 0:
            for control in controls:
                print(control.getRoleName(), repr(control.name), flush=True)
        chosen = False
        for control in controls:
            name = (control.name or "").lower()
            if control.getRoleName() == "check box" and "remember" in name:
                if not control.getState().contains(pyatspi.STATE_CHECKED):
                    control.queryAction().doAction(0)
            if screen_choice(control):
                selected = control.getState().contains(pyatspi.STATE_CHECKED)
                desired = not chosen
                if selected != desired:
                    control.queryAction().doAction(0)
                chosen = True
        for control in controls:
            if (control.name or "").lower().replace("_", "") in ("share", "allow") and control.getRoleName() in ("push button", "button"):
                if chosen and control.getState().contains(pyatspi.STATE_SENSITIVE):
                    control.queryAction().doAction(0)
                    sys.exit(0)
    time.sleep(.5)
sys.exit(1)
