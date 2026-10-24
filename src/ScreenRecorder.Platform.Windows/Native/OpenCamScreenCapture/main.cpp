// SPDX-License-Identifier: AGPL-3.0-or-later
// Opt-in editable-pointer producer. Not used by compatible capture.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <io.h>
#include <fcntl.h>
#include <atomic>
#include <chrono>
#include <iostream>
#include <mutex>
#include <thread>
#include <sstream>
#include <cstring>
#include "capture_protocol.h"

using namespace winrt;
using namespace Windows::Graphics::Capture;
using namespace Windows::Graphics::DirectX;
using namespace Windows::Graphics::DirectX::Direct3D11;
namespace {
std::mutex diagnostic_mutex;
void diagnostic(const std::string& value) {
    std::lock_guard<std::mutex> guard(diagnostic_mutex);
    std::cerr << value << std::endl;
    if (!std::cerr) ExitProcess(2);
}
[[noreturn]] void fail(const char* message) { diagnostic(std::string("ERROR ") + message); ExitProcess(1); }
int64_t host_us() {
    LARGE_INTEGER count{}, frequency{};
    if (!QueryPerformanceCounter(&count) || !QueryPerformanceFrequency(&frequency) || frequency.QuadPart <= 0)
        throw std::runtime_error("QPC unavailable");
    return static_cast<int64_t>(static_cast<long double>(count.QuadPart) * 1000000 / frequency.QuadPart);
}
bool same_rect(const RECT& a, const RECT& b) { return a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom; }
}

int main(int argc, char** argv) {
    try {
        // monitor handle, crop left/top, width/height, display width/height, FPS.
        if (argc != 9) throw std::invalid_argument("Expected monitor crop and rate arguments");
        int64_t args[8]{};
        for (int i = 0; i < 8; ++i) {
            size_t consumed = 0; args[i] = std::stoll(argv[i + 1], &consumed);
            if (consumed != std::strlen(argv[i + 1])) throw std::invalid_argument("Invalid numeric argument");
        }
        if (args[0] <= 0 || args[1] < 0 || args[2] < 0 || args[3] < 2 || args[4] < 2 ||
            args[3] > 16384 || args[4] > 16384 || args[3] % 2 || args[4] % 2 ||
            args[5] < args[3] || args[6] < args[4] || args[5] > 32768 || args[6] > 32768 ||
            args[1] > args[5] - args[3] || args[2] > args[6] - args[4] || args[7] < 1 || args[7] > 120 ||
            args[3] * args[4] > 16777216) throw std::invalid_argument("Invalid capture geometry");
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            throw std::runtime_error("Cannot establish physical-pixel coordinates");
        const auto monitor = reinterpret_cast<HMONITOR>(static_cast<intptr_t>(args[0]));
        MONITORINFO info{sizeof(MONITORINFO)};
        if (!GetMonitorInfoW(monitor, &info) || info.rcMonitor.right - info.rcMonitor.left != args[5] ||
            info.rcMonitor.bottom - info.rcMonitor.top != args[6]) throw std::runtime_error("Display changed");
        const auto original = info.rcMonitor;
        const int width = static_cast<int>(args[3]), height = static_cast<int>(args[4]), fps = static_cast<int>(args[7]);
        const int left = original.left + static_cast<int>(args[1]), top = original.top + static_cast<int>(args[2]);
        init_apartment(apartment_type::multi_threaded);
        if (!GraphicsCaptureSession::IsSupported()) throw std::runtime_error("WGC unavailable");
        com_ptr<ID3D11Device> device;
        com_ptr<ID3D11DeviceContext> context;
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
        auto dxgi = device.as<IDXGIDevice>();
        com_ptr<IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
        auto direct_device = inspectable.as<IDirect3DDevice>();
        auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        GraphicsCaptureItem item{nullptr};
        check_hresult(factory->CreateForMonitor(monitor, guid_of<GraphicsCaptureItem>(), put_abi(item)));
        auto pool = Direct3D11CaptureFramePool::CreateFreeThreaded(direct_device,
            DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, item.Size());
        auto session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled(false); // never alter the user's desktop cursor
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = width; desc.Height = height; desc.MipLevels = 1; desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        com_ptr<ID3D11Texture2D> staging;
        check_hresult(device->CreateTexture2D(&desc, nullptr, staging.put()));
        std::mutex frame_mutex;
        opencam::Bytes latest(size_t(width) * height * 4);
        int64_t acquired_us = -1; uint64_t sequence = 0;
        auto arrived = pool.FrameArrived([&](const auto& sender, const auto&) {
            try {
                auto frame = sender.TryGetNextFrame(); if (!frame) return;
                if (frame.ContentSize().Width != args[5] || frame.ContentSize().Height != args[6])
                    throw std::runtime_error("Capture dimensions changed");
                auto access = frame.Surface().template as<IDirect3DDxgiInterfaceAccess>();
                com_ptr<ID3D11Texture2D> texture;
                check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
                const D3D11_BOX box{static_cast<UINT>(args[1]), static_cast<UINT>(args[2]), 0,
                    static_cast<UINT>(args[1] + width), static_cast<UINT>(args[2] + height), 1};
                std::lock_guard<std::mutex> guard(frame_mutex);
                context->CopySubresourceRegion(staging.get(), 0, 0, 0, 0, texture.get(), 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped{};
                check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
                for (int y = 0; y < height; ++y)
                    std::memcpy(latest.data() + size_t(y) * width * 4, static_cast<uint8_t*>(mapped.pData) + size_t(y) * mapped.RowPitch, size_t(width) * 4);
                context->Unmap(staging.get(), 0);
                acquired_us = frame.SystemRelativeTime().count() / 10; // compositor QPC in 100ns
                ++sequence;
            } catch (...) { fail("WGC frame acquisition failed"); }
        });
        auto closed = item.Closed([](const auto&, const auto&) { fail("Capture target closed"); });
        _setmode(_fileno(stdout), _O_BINARY);
        diagnostic("READY bgra-matroska-clock-v1");
        std::string command;
        if (!std::getline(std::cin, command) || command != "start") {
            pool.FrameArrived(arrived); item.Closed(closed); session.Close(); pool.Close(); return 0;
        }
        std::atomic<bool> stopping{false};
        // Separate bounded producer threads. No whole-recording buffers and no coordinate logging.
        std::thread pointer([&] {
            try {
                while (!stopping.load()) {
                    CURSORINFO cursor{sizeof(CURSORINFO)};
                    const auto time = host_us();
                    const bool visible = GetCursorInfo(&cursor) && (cursor.flags & CURSOR_SHOWING);
                    unsigned buttons = ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) ? 1u : 0u) |
                        ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) ? 2u : 0u) | ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) ? 4u : 0u);
                    const auto p = opencam::relative_pointer(time, cursor.ptScreenPos.x, cursor.ptScreenPos.y,
                        left, top, width, height, visible, buttons);
                    std::ostringstream line;
                    line << "POINTER v1 " << p.host_us << ' ' << p.x << ' ' << p.y << ' ' << p.visible << ' ' << p.buttons;
                    diagnostic(line.str());
                    std::this_thread::sleep_for(std::chrono::microseconds(8334));
                }
            } catch (...) { fail("Pointer sampling failed"); }
        });
        std::thread video([&] {
            try {
                opencam::RawMatroska writer(std::cout, width, height, fps);
                opencam::FrameClock clock;
                opencam::Bytes frame(latest.size());
                while (!stopping.load()) {
                    const auto tick = std::chrono::steady_clock::now();
                    MONITORINFO current{sizeof(MONITORINFO)};
                    if (!GetMonitorInfoW(monitor, &current) || !same_rect(original, current.rcMonitor))
                        throw std::runtime_error("Display geometry changed");
                    int64_t acquired; uint64_t index;
                    { std::lock_guard<std::mutex> guard(frame_mutex); acquired = acquired_us; index = sequence; if (acquired >= 0) frame = latest; }
                    if (acquired >= 0) {
                        const auto timing = clock.present(host_us(), acquired);
                        writer.frame(frame, timing.pts_us);
                        std::ostringstream line;
                        line << "FRAME_CLOCK v1 " << timing.pts_us << ' ' << timing.presented_us << ' ' << timing.acquired_us << ' ' << index;
                        diagnostic(line.str());
                    }
                    // No catch-up bursts after backpressure; timestamps preserve actual elapsed time.
                    std::this_thread::sleep_until(tick + std::chrono::microseconds(1000000 / fps));
                }
            } catch (...) { fail("Native video transport failed"); }
        });
        try { session.StartCapture(); } catch (...) { fail("WGC start failed"); }
        std::getline(std::cin, command); // explicit stop or parent EOF
        stopping.store(true);
        video.join(); pointer.join();
        pool.FrameArrived(arrived); item.Closed(closed); session.Close(); pool.Close();
        diagnostic("STOP");
        return 0;
    } catch (const hresult_error& error) {
        diagnostic("ERROR Windows capture initialization HRESULT=" + std::to_string(error.code().value));
    } catch (const std::exception& error) { diagnostic(std::string("ERROR ") + error.what()); }
    return 1;
}
