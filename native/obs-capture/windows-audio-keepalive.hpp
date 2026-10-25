// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#ifdef _WIN32
#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include <atomic>
#include <condition_variable>
#include <future>
#include <mutex>
#include <string>
#include <thread>
#include <chrono>
#include <stdexcept>

// Keep the selected render endpoint clock running. This does not generate or
// replace captured audio: OBS must still deliver its own real WASAPI callbacks.
// All COM interfaces stay on the owning MTA thread and are released there.
class WindowsAudioKeepalive {
    template<class T> using Com = Microsoft::WRL::ComPtr<T>;
    std::thread worker;
    std::atomic<bool> stopping{false};
    std::atomic<long> failure{S_OK};
    std::mutex mutex;
    std::condition_variable wake;
    static void check(HRESULT hr) { if (FAILED(hr)) throw hr; }
    static std::wstring wide(const std::string &text) {
        int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0);
        if(!count) throw HRESULT(E_INVALIDARG);
        std::wstring result(count,L'\0');
        MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),result.data(),count);
        return result;
    }
    static std::wstring identity(IMMDevice *device) {
        LPWSTR value=nullptr; check(device->GetId(&value));
        std::wstring result(value); CoTaskMemFree(value); return result;
    }
    static std::string utf8(const std::wstring &text) {
        int count=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0,nullptr,nullptr);
        if(!count) throw HRESULT(E_INVALIDARG);
        std::string result(count,'\0');
        WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),result.data(),count,nullptr,nullptr);
        return result;
    }
public:
    ~WindowsAudioKeepalive() { stop(); }
    void stop() { stopping=true; wake.notify_all(); if(worker.joinable()) worker.join(); }
    long error() const { return failure.load(); }
    std::string start(const std::string &requested) {
        std::promise<std::string> ready;
        auto result=ready.get_future();
        worker=std::thread([this,requested,ready=std::move(ready)]() mutable {
            const HRESULT apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
            bool acknowledged=false;
            try {
                check(apartment);
                // Destruct these before uninitializing COM, including on failure.
                Com<IMMDeviceEnumerator> enumerator;
                check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)));
                Com<IMMDevice> device;
                const bool followDefault=requested=="default";
                // Match NAudio 2.2.1 WasapiLoopbackCapture used by preflight.
                if(followDefault) check(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&device));
                else check(enumerator->GetDevice(wide(requested).c_str(),&device));
                const auto id=identity(device.Get());
                Com<IAudioClient> client;
                check(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void **>(client.GetAddressOf())));
                WAVEFORMATEX *format=nullptr;
                check(client->GetMixFormat(&format));
                const HRESULT initialized=client->Initialize(AUDCLNT_SHAREMODE_SHARED,0,1000000,0,format,nullptr);
                CoTaskMemFree(format); check(initialized);
                UINT32 capacity=0; check(client->GetBufferSize(&capacity));
                Com<IAudioRenderClient> render; check(client->GetService(IID_PPV_ARGS(&render)));
                BYTE *buffer=nullptr;
                check(render->GetBuffer(capacity,&buffer));
                check(render->ReleaseBuffer(capacity,AUDCLNT_BUFFERFLAGS_SILENT));
                check(client->Start());
                ready.set_value(utf8(id)); acknowledged=true;
                unsigned tick=0;
                try {
                    while(!stopping) {
                        UINT32 padding=0; check(client->GetCurrentPadding(&padding));
                        if(padding>capacity) throw HRESULT(E_UNEXPECTED);
                        if(capacity>padding) {
                            check(render->GetBuffer(capacity-padding,&buffer));
                            check(render->ReleaseBuffer(capacity-padding,AUDCLNT_BUFFERFLAGS_SILENT));
                        }
                        if(tick++%10==0) {
                            DWORD status=0; check(device->GetState(&status));
                            if(status!=DEVICE_STATE_ACTIVE) throw HRESULT(AUDCLNT_E_DEVICE_INVALIDATED);
                            if(followDefault) {
                                Com<IMMDevice> current;
                                check(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&current));
                                // Never silently record an obsolete or different endpoint mid-segment.
                                if(identity(current.Get())!=id) throw HRESULT(AUDCLNT_E_DEVICE_INVALIDATED);
                            }
                        }
                        std::unique_lock<std::mutex> lock(mutex);
                        wake.wait_for(lock,std::chrono::milliseconds(10),[this]{return stopping.load();});
                    }
                } catch(...) { client->Stop(); throw; }
                client->Stop();
            } catch(HRESULT hr) {
                failure=hr;
                if(!acknowledged) ready.set_exception(std::make_exception_ptr(std::runtime_error("audio_keepalive_init")));
            } catch(...) {
                failure=E_FAIL;
                if(!acknowledged) ready.set_exception(std::current_exception());
            }
            if(SUCCEEDED(apartment)) CoUninitialize();
        });
        return result.get();
    }
};
#endif
