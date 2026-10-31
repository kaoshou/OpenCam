// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-linux-probe/source-preview.hpp"
#include <cassert>
#include <iostream>
int main() {
    auto size = opencam::previewSize(3840, 2160);
    assert(size.first == 480 && size.second == 270);
    size = opencam::previewSize(1080, 1920);
    assert(size.first == 151 && size.second == 270);
    size = opencam::previewSize(100, 50);
    assert(size.first == 100 && size.second == 50);
    assert(opencam::previewSize(0, 50).first == 0);
    assert(opencam::previewSize(16385, 50).first == 0);
    const unsigned char noTexture[] = {0, 0, 0, 0, 255, 255, 255, 0};
    const unsigned char realBlack[] = {0, 0, 0, 255, 0, 0, 0, 255};
    assert(!opencam::hasDrawnPixels(noTexture, sizeof noTexture));
    assert(opencam::hasDrawnPixels(realBlack, sizeof realBlack));
    assert(!opencam::hasDrawnPixels(nullptr, 4));
    assert(!opencam::hasDrawnPixels(realBlack, 7));
    std::cout << "PASS: bounded source-local thumbnail and opaque black frame acceptance\n";
}
