// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <wayland-client.h>
#include <vector>
#include <memory>
#include <string>
#include <cstring>
namespace opencam {
struct WaylandDisplay { wl_output *output = nullptr; std::string name; int x = 0, y = 0, width = 0, height = 0, scale = 1; };
inline std::vector<WaylandDisplay> waylandDisplays(wl_display *display) {
    std::vector<std::unique_ptr<WaylandDisplay>> values;
    static const wl_output_listener outputListener = {
        [](void *p, wl_output *, int32_t x, int32_t y, int32_t, int32_t, int32_t, const char *, const char *, int32_t) { auto &v = *static_cast<WaylandDisplay *>(p); v.x = x; v.y = y; },
        [](void *p, wl_output *, uint32_t flags, int32_t w, int32_t h, int32_t) { if (flags & WL_OUTPUT_MODE_CURRENT) { auto &v = *static_cast<WaylandDisplay *>(p); v.width = w; v.height = h; } },
        [](void *, wl_output *) {},
        [](void *p, wl_output *, int32_t scale) { static_cast<WaylandDisplay *>(p)->scale = scale; },
        [](void *p, wl_output *, const char *name) { static_cast<WaylandDisplay *>(p)->name = name; },
        [](void *, wl_output *, const char *) {}
    };
    const wl_registry_listener registryListener = {
        [](void *p, wl_registry *registry, uint32_t name, const char *interface, uint32_t version) {
            if (std::strcmp(interface, wl_output_interface.name)) return;
            auto &list = *static_cast<decltype(values) *>(p); auto v = std::make_unique<WaylandDisplay>();
            v->output = static_cast<wl_output *>(wl_registry_bind(registry, name, &wl_output_interface, std::min(version, 4u)));
            v->name = std::to_string(name); wl_output_add_listener(v->output, &outputListener, v.get()); list.push_back(std::move(v));
        }, [](void *, wl_registry *, uint32_t) {}
    };
    auto *registry = wl_display_get_registry(display); wl_registry_add_listener(registry, &registryListener, &values);
    wl_display_roundtrip(display); wl_display_roundtrip(display);
    std::vector<WaylandDisplay> result;
    for (const auto &v : values) { if (v->width > 0 && v->height > 0) { auto d = *v; d.output = nullptr; result.push_back(d); } wl_output_destroy(v->output); }
    wl_registry_destroy(registry); return result;
}
}
