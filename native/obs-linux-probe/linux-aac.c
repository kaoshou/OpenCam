// SPDX-License-Identifier: AGPL-3.0-or-later
// Diagnostic-only module entrypoint. Encoder implementation is compiled from
// unchanged pinned OBS sources, preserving its original copyright/license.
#include <obs-module.h>
OBS_DECLARE_MODULE()
OBS_MODULE_USE_DEFAULT_LOCALE("linux-aac", "en-US")
extern struct obs_encoder_info aac_encoder_info;
bool obs_module_load(void)
{
    obs_register_encoder(&aac_encoder_info);
    return true;
}
