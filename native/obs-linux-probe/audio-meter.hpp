// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
namespace opencam {
class LinuxAudioMeter {
    std::atomic<float> value{0};
    std::atomic<uint64_t> timestamp{0};
public:
    void note(const float *const *channels, size_t count, uint32_t frames, bool muted, uint64_t now) {
        if (!frames) return;
        float maximum = 0;
        if (!muted) for (size_t channel = 0; channel < count; ++channel) if (channels[channel])
            for (uint32_t i = 0; i < frames; ++i) if (std::isfinite(channels[channel][i]))
                maximum = std::max(maximum, std::abs(channels[channel][i]));
        value.store(std::min(maximum, 1.f)); timestamp.store(now);
    }
    float peak() const { return value.load(); }
    int64_t ageUs(uint64_t now) const { auto at = timestamp.load(); return at && now >= at ? static_cast<int64_t>((now-at)/1000) : -1; }
};
}
