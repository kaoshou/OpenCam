// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-linux-probe/audio-meter.hpp"
#include <cassert>
int main() {
    opencam::LinuxAudioMeter meter;
    assert(meter.ageUs(1000000000) == -1);
    const float silence[] = {0, 0, 0}; const float audible[] = {.2f, -.8f, .3f};
    const float *channels[] = {silence, nullptr};
    meter.note(channels, 2, 3, false, 1000000000);
    assert(meter.peak() == 0 && meter.ageUs(1000100000) == 100);
    channels[0] = audible; meter.note(channels, 2, 3, false, 2000000000);
    assert(meter.peak() > .79 && meter.peak() < .81);
    meter.note(channels, 2, 3, true, 3000000000);
    assert(meter.peak() == 0 && meter.ageUs(5000000000) == 2000000);
    meter.note(channels, 2, 0, false, 6000000000);
    assert(meter.ageUs(6000000000) == 3000000); // zero frames never fake health
}
