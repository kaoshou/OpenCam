// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-linux-probe/linux-sources.hpp"
#include <cassert>
#include <cstring>
int main()
{
    using opencam::LinuxSourceHealth;
    LinuxSourceHealth portal;
    portal.pipewire = true;
    portal.streamState("streaming");
    portal.arm();
    assert(portal.failure(100) == nullptr);
    portal.sessionClosed();
    portal.streamState("streaming");
    assert(std::strcmp(portal.failure(101), "portal_session_closed") == 0);
    LinuxSourceHealth stream;
    stream.pipewire = true;
    stream.streamState("streaming"); stream.arm(); stream.streamState("unconnected");
    assert(std::strcmp(stream.failure(100), "pipewire_transport_lost") == 0);
    LinuxSourceHealth audio;
    audio.systemSelected = true; audio.microphoneSelected = true;
    audio.noteSamples(false, 480, 100); audio.noteSamples(true, 480, 100); audio.arm();
    assert(audio.failure(1000000100ULL) == nullptr); // Silence is valid; no peak requirement.
    audio.noteSamples(false, 480, 2500000100ULL);
    assert(std::strcmp(audio.failure(2500000100ULL), "audio_health_unverifiable") == 0);
    audio.noteSamples(true, 0, 2500000100ULL); // Zero-length callbacks prove nothing.
    assert(audio.failure(2500000100ULL) != nullptr);
    audio.noteSamples(true, 480, 2500000100ULL);
    assert(audio.failure(2500000100ULL) == nullptr);
    audio.disarm();
    assert(audio.failure(9999999999ULL) == nullptr); // Deliberate shutdown is not source loss.
}
