// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <atomic>
#include <cstdint>
#include <cstring>
namespace opencam {
// A diagnostic guard, not a claim that a quiet endpoint is disconnected.
class LinuxSourceHealth {
    std::atomic<bool> armed{false}, closed{false}, transportLost{false}, active{false};
    std::atomic<uint64_t> systemAt{0}, microphoneAt{0};
public:
    bool pipewire = false, systemSelected = false, microphoneSelected = false;
    void arm() { armed = true; }
    void disarm() { armed = false; }
    void sessionClosed() { closed = true; }
    void streamState(const char *state) {
        bool streaming = state && std::strcmp(state, "streaming") == 0;
        active = streaming;
        if (armed && !streaming) transportLost = true;
    }
    bool streaming() const { return active; }
    void noteSamples(bool microphone, uint32_t frames, uint64_t at) {
        if (frames) (microphone ? microphoneAt : systemAt).store(at);
    }
    const char *terminalFailure() const {
        if (closed) return "portal_session_closed";
        if (transportLost) return "pipewire_transport_lost";
        return nullptr;
    }
    const char *failure(uint64_t now) const {
        if (!armed) return nullptr;
        if (auto *reason = terminalFailure()) return reason;
        if (pipewire && !active) return "pipewire_health_unverifiable";
        auto stale = [now](uint64_t at) { return !at || now < at || now - at > 2000000000ULL; };
        if ((systemSelected && stale(systemAt)) || (microphoneSelected && stale(microphoneAt)))
            return "audio_health_unverifiable";
        return nullptr;
    }
};
}
