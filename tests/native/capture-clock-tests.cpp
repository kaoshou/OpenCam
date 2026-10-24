// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../src/ScreenRecorder.Platform.Windows/Native/OpenCamScreenCapture/capture_protocol.h"
#include <cassert>
#include <sstream>
#include <iostream>
#include <future>
#include <chrono>

static void pointer_tests() {
    opencam::PointerButtons buttons;
    std::vector<unsigned> changes;
    const auto capture = [&](unsigned value) { changes.push_back(value); return true; };
    assert(buttons.apply(1, 1, capture)); // down and up delivered in one input packet
    assert(buttons.apply(6, 0, capture));
    assert(buttons.apply(0, 6, capture));
    assert((changes == std::vector<unsigned>{1, 0, 2, 6, 4, 0}));
    assert(buttons.value() == 0);
    // Coalescing adjacent samples would lose a click that completes between polls.
    opencam::PointerQueue<4> queue;
    assert(queue.push({100, 4, 5, true, 0}));
    assert(queue.push({100, 4, 5, true, 1}));
    assert(queue.push({99, 4, 5, true, 0}));
    queue.finish();
    opencam::Pointer point{};
    assert(queue.wait_pop(point) && point.host_us == 100 && point.buttons == 0);
    assert(queue.wait_pop(point) && point.host_us == 101 && point.buttons == 1);
    assert(queue.wait_pop(point) && point.host_us == 102 && point.buttons == 0);
    assert(!queue.wait_pop(point));
    assert(queue.error() == opencam::PointerError::none);
    assert(!queue.push({103, 4, 5, true, 1}));

    opencam::PointerQueue<2> overflow;
    assert(overflow.push({1, 0, 0, true, 0}));
    assert(overflow.push({2, 0, 0, true, 1}));
    assert(!overflow.push({3, 0, 0, true, 0}));
    assert(overflow.error() == opencam::PointerError::overflow);
    assert(!overflow.push({4, 0, 0, true, 1}));
    assert(overflow.wait_pop(point) && point.buttons == 0);
    assert(overflow.wait_pop(point) && point.buttons == 1);
    assert(!overflow.wait_pop(point));

    auto hidden = opencam::relative_pointer(20, -1820, -100, -1920, -200, 1920, 1080, false, 7);
    assert(!hidden.visible && hidden.x == 0 && hidden.y == 0 && hidden.buttons == 0);
    auto edge = opencam::relative_pointer(20, -1, 879, -1920, -200, 1920, 1080, true, 7);
    assert(edge.visible && edge.x == 1919 && edge.y == 1079 && edge.buttons == 7);
    for (const auto& xy : {std::pair<int, int>{0, 879}, {-1, 880}, {-1921, -200}, {-1920, -201}}) {
        auto outside = opencam::relative_pointer(20, xy.first, xy.second, -1920, -200, 1920, 1080, true, 7);
        assert(!outside.visible && outside.x == 0 && outside.y == 0 && outside.buttons == 0);
    }
}

static void callback_lifetime_test() {
    opencam::CallbackGate gate;
    auto active = gate.enter();
    assert(active);
    gate.stop();
    assert(!gate.enter()); // A queued callback cannot touch closed resources.
    auto drain = std::async(std::launch::async, [&] { gate.wait(); return true; });
    assert(drain.wait_for(std::chrono::milliseconds(25)) == std::future_status::timeout);
    active = {};
    assert(drain.wait_for(std::chrono::seconds(2)) == std::future_status::ready);
    assert(drain.get());
    gate.wait();
}

int main(int argc, char**) {
    pointer_tests();
    callback_lifetime_test();
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
