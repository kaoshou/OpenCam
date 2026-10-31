// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include "source-preview.hpp"
#include <obs.h>
#include <graphics/vec4.h>
#include <cstring>
#include <vector>

namespace opencam {
struct SourceSnapshot {
    unsigned width = 0, height = 0, sourceWidth = 0, sourceHeight = 0;
    std::vector<std::uint8_t> rgba;
};
// Startup-only readback. Negotiated PipeWire dimensions alone do not establish
// a received video frame: pinned OBS draws nothing until its texture exists.
inline SourceSnapshot snapshotSource(obs_source_t *source) {
    SourceSnapshot result;
    if (!source) return result;
    obs_enter_graphics();
    result.sourceWidth = obs_source_get_width(source);
    result.sourceHeight = obs_source_get_height(source);
    const auto size = previewSize(result.sourceWidth, result.sourceHeight);
    result.width = size.first; result.height = size.second;
    gs_texrender_t *render = nullptr;
    gs_stagesurf_t *stage = nullptr;
    try {
        if (result.width && result.height) {
            result.rgba.resize(static_cast<std::size_t>(result.width) * result.height * 4);
            render = gs_texrender_create(GS_RGBA, GS_ZS_NONE);
            stage = gs_stagesurface_create(result.width, result.height, GS_RGBA);
            if (render && stage && gs_texrender_begin(render, result.width, result.height)) {
                vec4 clear{};
                gs_clear(GS_CLEAR_COLOR, &clear, 0, 0);
                gs_ortho(0, static_cast<float>(result.sourceWidth), 0, static_cast<float>(result.sourceHeight), -100, 100);
                gs_blend_state_push();
                gs_blend_function(GS_BLEND_ONE, GS_BLEND_INVSRCALPHA);
                obs_source_video_render(source);
                gs_blend_state_pop();
                gs_texrender_end(render);
                gs_stage_texture(stage, gs_texrender_get_texture(render));
                std::uint8_t *pixels = nullptr; std::uint32_t stride = 0;
                if (gs_stagesurface_map(stage, &pixels, &stride)) {
                    if (stride >= result.width * 4)
                        for (unsigned y = 0; y < result.height; ++y)
                            std::memcpy(result.rgba.data() + static_cast<std::size_t>(y) * result.width * 4,
                                        pixels + static_cast<std::size_t>(y) * stride, result.width * 4);
                    gs_stagesurface_unmap(stage);
                }
            }
        }
    } catch (...) {
        if (stage) gs_stagesurface_destroy(stage);
        if (render) gs_texrender_destroy(render);
        obs_leave_graphics();
        throw;
    }
    if (stage) gs_stagesurface_destroy(stage);
    if (render) gs_texrender_destroy(render);
    obs_leave_graphics();
    if (!hasDrawnPixels(result.rgba.data(), result.rgba.size())) result.rgba.clear();
    return result;
}
} // namespace opencam
