/* SPDX-License-Identifier: AGPL-3.0-or-later */
#include <obs-module.h>
#include <util/windows/win-version.h>

/* Compile only the pinned OBS display/cursor implementation. The upstream
 * plugin entry also initializes game hooks and an updater and is not linked. */
OBS_DECLARE_MODULE()
OBS_MODULE_USE_DEFAULT_LOCALE("win-capture", "en-US")

extern struct obs_source_info duplicator_capture_info;
bool graphics_uses_d3d11 = false;
bool wgc_supported = false;

MODULE_EXPORT const char *obs_module_description(void)
{
    return "OpenCam display-only capture (pinned OBS implementation)";
}

bool obs_module_load(void)
{
    struct win_version_info version;
    const struct win_version_info minimum_wgc = {10, 0, 18362, 0};
    get_win_ver(&version);
    obs_enter_graphics();
    graphics_uses_d3d11 = gs_get_device_type() == GS_DEVICE_DIRECT3D_11;
    obs_leave_graphics();
    wgc_supported = graphics_uses_d3d11 && win_version_compare(&version, &minimum_wgc) >= 0;
    obs_register_source(&duplicator_capture_info);
    return true;
}
