// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <utility>

namespace opencam {
inline std::pair<unsigned, unsigned> previewSize(unsigned width, unsigned height) {
    if (!width || !height || width > 16384 || height > 16384) return {0, 0};
    const double scale = std::min({1.0, 480.0 / width, 270.0 / height});
    return {std::max(1u, static_cast<unsigned>(width * scale)),
            std::max(1u, static_cast<unsigned>(height * scale))};
}
inline bool hasDrawnPixels(const std::uint8_t *rgba, std::size_t size) {
    if (!rgba || size % 4) return false;
    for (std::size_t i = 3; i < size; i += 4)
        if (rgba[i]) return true;
    return false;
}
} // namespace opencam
