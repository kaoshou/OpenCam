// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../src/ScreenRecorder.Platform.Windows/Native/OpenCamScreenCapture/capture_protocol.h"
#include <cassert>
#include <sstream>
#include <iostream>

int main(int argc, char**) {
    opencam::FrameClock clock;
    assert(clock.present(8000000, 7990000).pts_us == 0);
    const auto second = clock.present(8033333, 8020000);
    assert(second.pts_us == 33333 && second.acquired_us == 8020000);
    assert(clock.present(8500000, 8020000).pts_us == 500000);
    bool rejected = false;
    try { clock.present(8400000, 8020000); } catch (const std::invalid_argument&) { rejected = true; }
    assert(rejected);
    auto point = opencam::relative_pointer(20, -1820, -100, -1920, -200, 1920, 1080, true, 1);
    assert(point.visible && point.x == 100 && point.y == 100 && point.buttons == 1);
    point = opencam::relative_pointer(20, 15, 10, -1920, -200, 1920, 1080, true, 1);
    assert(!point.visible && point.x == 0 && point.y == 0 && point.buttons == 0);
    if (argc > 1) {
        opencam::RawMatroska writer(std::cout, 32, 32, 30);
        for (int i = 0; i < 4; ++i) {
            std::vector<uint8_t> frame(32 * 32 * 4, 0);
            for (size_t p = 0; p < frame.size(); p += 4) { frame[p + 2] = static_cast<uint8_t>(i * 60); frame[p + 3] = 255; }
            writer.frame(frame, i == 3 ? 500000 : i * 33333);
        }
    }
}
