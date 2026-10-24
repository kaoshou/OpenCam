// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <cstdint>
#include <vector>
#include <ostream>
#include <stdexcept>
#include <limits>
#include <string>

namespace opencam {
struct FrameTiming { int64_t pts_us; int64_t acquired_us; int64_t presented_us; };
class FrameClock {
    int64_t origin_ = -1, last_ = -1;
public:
    FrameTiming present(int64_t now, int64_t acquired) {
        if (now < 0 || acquired < 0 || acquired > now || now <= last_)
            throw std::invalid_argument("Invalid native frame clock");
        if (origin_ < 0) origin_ = now;
        last_ = now;
        return {now - origin_, acquired, now};
    }
};
struct Pointer { int64_t host_us; int x, y; bool visible; unsigned buttons; };
inline Pointer relative_pointer(int64_t time, int x, int y, int left, int top, int width, int height, bool visible, unsigned buttons) {
    if (time < 0 || width <= 0 || height <= 0 || buttons > 7) throw std::invalid_argument("Invalid pointer");
    if (!visible || x < left || y < top || int64_t(x) >= int64_t(left) + width || int64_t(y) >= int64_t(top) + height)
        return {time, 0, 0, false, 0};
    return {time, x - left, y - top, true, buttons};
}

using Bytes = std::vector<uint8_t>;
inline Bytes number(uint64_t n) {
    Bytes result;
    do { result.insert(result.begin(), uint8_t(n)); n >>= 8; } while (n);
    return result;
}
inline Bytes size(uint64_t n) {
    for (unsigned count = 1; count <= 8; ++count) {
        if (n < (uint64_t(1) << (7 * count)) - 1) {
            auto result = number(n | (uint64_t(1) << (7 * count)));
            return result;
        }
    }
    throw std::invalid_argument("EBML size overflow");
}
inline void append(Bytes& target, const Bytes& source) { target.insert(target.end(), source.begin(), source.end()); }
inline Bytes element(uint64_t id, const Bytes& data) {
    auto result = number(id); append(result, size(data.size())); append(result, data); return result;
}
inline Bytes uint_element(uint64_t id, uint64_t n) { return element(id, number(n)); }
inline Bytes text(uint64_t id, const std::string& value) { return element(id, Bytes(value.begin(), value.end())); }
inline void le32(Bytes& bytes, size_t offset, uint32_t value) {
    for (unsigned i = 0; i < 4; ++i) bytes.at(offset + i) = uint8_t(value >> (8 * i));
}

// Raw BGRA transport, not the final recording: downstream FFmpeg encodes it.
// Unknown segment size and complete per-frame clusters permit bounded streaming.
// One-microsecond timebase preserves native presentation timing through transport.
class RawMatroska {
    std::ostream& out_;
    size_t frame_bytes_;
    int64_t last_ = -1;
    void write(const Bytes& value) {
        out_.write(reinterpret_cast<const char*>(value.data()), static_cast<std::streamsize>(value.size()));
        if (!out_) throw std::runtime_error("Native video pipe write failed");
    }
public:
    RawMatroska(std::ostream& out, int width, int height, int fps) : out_(out), frame_bytes_(0) {
        if (width < 2 || height < 2 || width % 2 || height % 2 || fps < 1 || fps > 120 ||
            int64_t(width) * height > 16777216) throw std::invalid_argument("Invalid video geometry");
        frame_bytes_ = size_t(width) * size_t(height) * 4;
        Bytes ebml;
        for (auto value : {uint_element(0x4286, 1), uint_element(0x42f7, 1), uint_element(0x42f2, 4),
             uint_element(0x42f3, 8), text(0x4282, "matroska"), uint_element(0x4287, 4), uint_element(0x4285, 2)}) append(ebml, value);
        write(element(0x1a45dfa3, ebml)); write(number(0x18538067));
        write(Bytes{1,255,255,255,255,255,255,255});
        Bytes info = uint_element(0x2ad7b1, 1000);
        append(info, text(0x4d80, "OpenCam")); append(info, text(0x5741, "OpenCam"));
        write(element(0x1549a966, info));
        Bytes bitmap(40, 0);
        le32(bitmap, 0, 40); le32(bitmap, 4, uint32_t(width)); le32(bitmap, 8, uint32_t(-height));
        bitmap[12] = 1; bitmap[14] = 32; le32(bitmap, 20, static_cast<uint32_t>(frame_bytes_));
        Bytes video = uint_element(0xb0, width); append(video, uint_element(0xba, height));
        Bytes track;
        for (auto value : {uint_element(0xd7, 1), uint_element(0x73c5, 1), uint_element(0x83, 1),
             uint_element(0x9c, 0), text(0x86, "V_MS/VFW/FOURCC"), element(0x63a2, bitmap),
             uint_element(0x23e383, 1000000000 / fps), element(0xe0, video)}) append(track, value);
        write(element(0x1654ae6b, element(0xae, track)));
    }
    void frame(const Bytes& pixels, int64_t pts_us) {
        if (pixels.size() != frame_bytes_ || pts_us < 0 || pts_us <= last_)
            throw std::invalid_argument("Invalid native video frame");
        const auto time = uint_element(0xe7, uint64_t(pts_us));
        const auto block_size = size(pixels.size() + 4);
        write(number(0x1f43b675)); write(size(time.size() + 1 + block_size.size() + 4 + pixels.size()));
        write(time); write(Bytes{0xa3}); write(block_size); write(Bytes{0x81,0,0,0x80}); write(pixels);
        out_.flush(); if (!out_) throw std::runtime_error("Native video pipe flush failed");
        last_ = pts_us;
    }
};
}
