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
#include <memory>
#include "capture_protocol.h"

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
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

class MouseSampler {
    opencam::PointerQueue<> queue_;
    opencam::PointerButtons buttons_;
    std::atomic<bool> stopping_{false};
    std::thread input_, writer_;
    int left_, top_, width_, height_;
    bool sample(unsigned buttons) {
        CURSORINFO cursor{sizeof(CURSORINFO)};
        POINT position{};
        if (!GetCursorInfo(&cursor) || !GetPhysicalCursorPos(&position)) {
            queue_.finish(opencam::PointerError::unavailable);
            return false;
        }
        return queue_.push(opencam::relative_pointer(host_us(), position.x, position.y,
            left_, top_, width_, height_, (cursor.flags & CURSOR_SHOWING) != 0, buttons));
    }
    void raw_input(HRAWINPUT handle) {
        // Mouse-only registration permits a fixed-size buffer, never HID/keyboard data.
        RAWINPUT input{};
        UINT size = sizeof(input);
        const UINT read = GetRawInputData(handle, RID_INPUT, &input, &size, sizeof(RAWINPUTHEADER));
        if (read == UINT(-1) || read < sizeof(RAWINPUTHEADER) + sizeof(RAWMOUSE) || input.header.dwType != RIM_TYPEMOUSE) {
            queue_.finish(opencam::PointerError::unavailable);
            return;
        }
        const auto flags = input.data.mouse.usButtonFlags;
        const unsigned down = ((flags & RI_MOUSE_LEFT_BUTTON_DOWN) ? 1u : 0u) |
            ((flags & RI_MOUSE_RIGHT_BUTTON_DOWN) ? 2u : 0u) | ((flags & RI_MOUSE_MIDDLE_BUTTON_DOWN) ? 4u : 0u);
        const unsigned up = ((flags & RI_MOUSE_LEFT_BUTTON_UP) ? 1u : 0u) |
            ((flags & RI_MOUSE_RIGHT_BUTTON_UP) ? 2u : 0u) | ((flags & RI_MOUSE_MIDDLE_BUTTON_UP) ? 4u : 0u);
        buttons_.apply(down, up, [this](unsigned value) { return sample(value); });
    }
    static LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept {
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<const CREATESTRUCTW*>(lparam);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto self = reinterpret_cast<MouseSampler*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_INPUT && self) {
            try { self->raw_input(reinterpret_cast<HRAWINPUT>(lparam)); }
            catch (...) { self->queue_.finish(opencam::PointerError::unavailable); }
        }
        // Required WM_INPUT cleanup; this neither suppresses nor changes desktop input.
        return DefWindowProcW(window, message, wparam, lparam);
    }
    void collect() noexcept {
        const auto instance = GetModuleHandleW(nullptr);
        const wchar_t* name = L"OpenCamPointerInput";
        HWND window = nullptr;
        ATOM window_class = 0;
        bool registered = false;
        try {
            WNDCLASSW type{};
            type.lpfnWndProc = window_proc; type.hInstance = instance; type.lpszClassName = name;
            window_class = RegisterClassW(&type);
            if (!window_class) throw std::runtime_error("Mouse input window class failed");
            window = CreateWindowExW(0, name, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance, this);
            if (!window) throw std::runtime_error("Mouse input window failed");
            RAWINPUTDEVICE mouse{1, 2, RIDEV_INPUTSINK, window};
            if (!RegisterRawInputDevices(&mouse, 1, sizeof(mouse))) throw std::runtime_error("Mouse input unavailable");
            registered = true;
            // One initial state snapshot; subsequent button changes come from events.
            buttons_ = opencam::PointerButtons(((GetAsyncKeyState(VK_LBUTTON) & 0x8000) ? 1u : 0u) |
                ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) ? 2u : 0u) | ((GetAsyncKeyState(VK_MBUTTON) & 0x8000) ? 4u : 0u));
            auto next_sample = std::chrono::steady_clock::now();
            while (!stopping_.load() && queue_.error() == opencam::PointerError::none) {
                // Bound dispatch batches so continuous motion cannot starve stop/sampling.
                MSG message{};
                for (unsigned i = 0; i < 256 && PeekMessageW(&message, window, WM_INPUT, WM_INPUT, PM_REMOVE); ++i) {
                    DispatchMessageW(&message);
                    if (stopping_.load() || queue_.error() != opencam::PointerError::none) break;
                }
                const auto now = std::chrono::steady_clock::now();
                if (now >= next_sample) {
                    if (!sample(buttons_.value())) break;
                    next_sample = now + std::chrono::microseconds(8334);
                }
                const auto remaining = std::chrono::duration_cast<std::chrono::microseconds>(next_sample - std::chrono::steady_clock::now()).count();
                const DWORD wait_ms = remaining > 0 ? static_cast<DWORD>((remaining + 999) / 1000) : 0;
                const DWORD result = MsgWaitForMultipleObjectsEx(0, nullptr, wait_ms, QS_RAWINPUT, MWMO_INPUTAVAILABLE);
                if (result == WAIT_FAILED) throw std::runtime_error("Mouse input wait failed");
            }
        } catch (...) { queue_.finish(opencam::PointerError::unavailable); }
        // The window procedure runs only on this thread. Remove registration and
        // destroy the window before joining, so no callback can outlive this object.
        if (registered) {
            RAWINPUTDEVICE remove{1, 2, RIDEV_REMOVE, nullptr};
            if (!RegisterRawInputDevices(&remove, 1, sizeof(remove))) queue_.finish(opencam::PointerError::unavailable);
        }
        if (window && !DestroyWindow(window)) queue_.finish(opencam::PointerError::unavailable);
        if (window_class && !UnregisterClassW(name, instance)) queue_.finish(opencam::PointerError::unavailable);
        queue_.finish();
    }
public:
    MouseSampler(int left, int top, int width, int height) : left_(left), top_(top), width_(width), height_(height) {}
    void start() {
        try {
            writer_ = std::thread([this] {
                try {
                    opencam::Pointer p{};
                    while (queue_.wait_pop(p)) {
                        std::ostringstream line;
                        line << "POINTER v1 " << p.host_us << ' ' << p.x << ' ' << p.y << ' ' << p.visible << ' ' << p.buttons;
                        diagnostic(line.str());
                    }
                } catch (...) { queue_.finish(opencam::PointerError::unavailable); }
                switch (queue_.error()) {
                    case opencam::PointerError::none: break;
                    case opencam::PointerError::overflow: diagnostic("POINTER_ERROR queue-overflow"); break;
                    case opencam::PointerError::invalid_clock: diagnostic("POINTER_ERROR invalid-clock"); break;
                    default: diagnostic("POINTER_ERROR mouse-input-unavailable"); break;
                }
            });
            input_ = std::thread([this] { collect(); });
        } catch (...) {
            queue_.finish(opencam::PointerError::unavailable);
            if (!writer_.joinable()) diagnostic("POINTER_ERROR mouse-thread-unavailable");
        }
    }
    void stop() {
        stopping_.store(true);
        if (input_.joinable()) input_.join();
        queue_.finish();
        if (writer_.joinable()) writer_.join();
    }
    ~MouseSampler() { stop(); }
};

struct FrameState {
    opencam::CallbackGate callbacks;
    std::mutex mutex;
    opencam::Bytes latest;
    com_ptr<ID3D11DeviceContext> context;
    com_ptr<ID3D11Texture2D> staging;
    int64_t acquired_us = -1;
    uint64_t sequence = 0;
    explicit FrameState(size_t bytes) : latest(bytes) {}
};

struct CaptureLifetime {
    std::shared_ptr<FrameState> state;
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    GraphicsCaptureItem item{nullptr};
    event_token arrived{}, closed{};
    bool has_arrived = false, has_closed = false, stopped = false;
    void stop() noexcept {
        if (stopped) return;
        stopped = true;
        state->callbacks.stop();
        try { if (has_arrived) pool.FrameArrived(arrived); }
        catch (...) { diagnostic("ERROR WGC frame unsubscribe failed"); }
        try { if (has_closed) item.Closed(closed); }
        catch (...) { diagnostic("ERROR WGC target unsubscribe failed"); }
        state->callbacks.wait();
        try { if (session) session.Close(); }
        catch (...) { diagnostic("ERROR WGC session close failed"); }
        try { if (pool) pool.Close(); }
        catch (...) { diagnostic("ERROR WGC pool close failed"); }
    }
    ~CaptureLifetime() { stop(); }
};
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
        auto frames = std::make_shared<FrameState>(size_t(width) * height * 4);
        frames->context = context; frames->staging = staging;
        CaptureLifetime lifetime{frames, pool, session, item};
        const int display_width = static_cast<int>(args[5]), display_height = static_cast<int>(args[6]);
        const D3D11_BOX box{static_cast<UINT>(args[1]), static_cast<UINT>(args[2]), 0,
            static_cast<UINT>(args[1] + width), static_cast<UINT>(args[2] + height), 1};
        lifetime.arrived = pool.FrameArrived([frames, width, height, display_width, display_height, box](const auto& sender, const auto&) {
            auto lease = frames->callbacks.enter();
            if (!lease) return;
            try {
                auto frame = sender.TryGetNextFrame(); if (!frame) return;
                if (frame.ContentSize().Width != display_width || frame.ContentSize().Height != display_height)
                    throw std::runtime_error("Capture dimensions changed");
                auto access = frame.Surface().template as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                com_ptr<ID3D11Texture2D> texture;
                check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
                std::lock_guard<std::mutex> guard(frames->mutex);
                frames->context->CopySubresourceRegion(frames->staging.get(), 0, 0, 0, 0, texture.get(), 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped{};
                check_hresult(frames->context->Map(frames->staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
                for (int y = 0; y < height; ++y)
                    std::memcpy(frames->latest.data() + size_t(y) * width * 4, static_cast<uint8_t*>(mapped.pData) + size_t(y) * mapped.RowPitch, size_t(width) * 4);
                frames->context->Unmap(frames->staging.get(), 0);
                frames->acquired_us = frame.SystemRelativeTime().count() / 10; // compositor QPC in 100ns
                ++frames->sequence;
            } catch (...) { fail("WGC frame acquisition failed"); }
        });
        lifetime.has_arrived = true;
        lifetime.closed = item.Closed([frames](const auto&, const auto&) {
            auto lease = frames->callbacks.enter();
            if (lease) fail("Capture target closed");
        });
        lifetime.has_closed = true;
        _setmode(_fileno(stdout), _O_BINARY);
        diagnostic("READY bgra-matroska-clock-v1");
        std::string command;
        if (!std::getline(std::cin, command) || command != "start") {
            lifetime.stop(); return 0;
        }
        std::atomic<bool> stopping{false};
        MouseSampler pointer(left, top, width, height);
        pointer.start();
        std::thread video([&] {
            try {
                opencam::RawMatroska writer(std::cout, width, height, fps);
                opencam::FrameClock clock;
                opencam::Bytes frame(frames->latest.size());
                while (!stopping.load()) {
                    const auto tick = std::chrono::steady_clock::now();
                    MONITORINFO current{sizeof(MONITORINFO)};
                    if (!GetMonitorInfoW(monitor, &current) || !same_rect(original, current.rcMonitor))
                        throw std::runtime_error("Display geometry changed");
                    int64_t acquired; uint64_t index;
                    { std::lock_guard<std::mutex> guard(frames->mutex); acquired = frames->acquired_us; index = frames->sequence; if (acquired >= 0) frame = frames->latest; }
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
        pointer.stop(); video.join();
        lifetime.stop();
        diagnostic("STOP");
        return 0;
    } catch (const hresult_error& error) {
        diagnostic("ERROR Windows capture initialization HRESULT=" + std::to_string(error.code().value));
    } catch (const std::exception& error) { diagnostic(std::string("ERROR ") + error.what()); }
    return 1;
}
