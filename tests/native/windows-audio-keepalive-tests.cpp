// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-capture/windows-audio-keepalive.hpp"
#include <iostream>

int main() {
    const HRESULT apartment=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    if(FAILED(apartment)) return 1;
    int result=1;
    try {
        WindowsAudioKeepalive invalid;
        bool rejected=false;
        try { invalid.start("OpenCam-invalid-endpoint-for-regression"); }
        catch(...) { rejected=true; }
        if(!rejected || SUCCEEDED(invalid.error())) throw std::runtime_error("Invalid endpoint falsely initialized");
        invalid.stop();
        Microsoft::WRL::ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT hr=CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator));
        if(FAILED(hr)) throw std::runtime_error("Enumerator unavailable");
        Microsoft::WRL::ComPtr<IMMDevice> device;
        if(FAILED(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&device))) {
            std::cout << "SKIP physical WASAPI: runner has no render endpoint; invalid endpoint rejection passed\n";
            result=77;
        } else {
            WindowsAudioKeepalive keepalive;
            auto id=keepalive.start("default");
            if(id.empty() || FAILED(keepalive.error())) throw std::runtime_error("Keepalive initialization failed");
            Microsoft::WRL::ComPtr<IAudioClient> client;
            if(FAILED(device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void **>(client.GetAddressOf())))) throw std::runtime_error("Capture activation failed");
            WAVEFORMATEX *format=nullptr;
            if(FAILED(client->GetMixFormat(&format))) throw std::runtime_error("Mix format failed");
            hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK,1000000,0,format,nullptr);
            CoTaskMemFree(format);
            if(FAILED(hr)) throw std::runtime_error("Capture initialization failed");
            Microsoft::WRL::ComPtr<IAudioCaptureClient> capture;
            if(FAILED(client->GetService(IID_PPV_ARGS(&capture))) || FAILED(client->Start())) throw std::runtime_error("Capture start failed");
            UINT64 previous=0, packets=0;
            auto end=std::chrono::steady_clock::now()+std::chrono::seconds(3);
            while(std::chrono::steady_clock::now()<end) {
                UINT32 size=0;
                if(FAILED(capture->GetNextPacketSize(&size)) || FAILED(keepalive.error())) throw std::runtime_error("Capture/keepalive lost");
                if(size) {
                    BYTE *data=nullptr; UINT32 frames=0; DWORD flags=0; UINT64 position=0, timestamp=0;
                    if(FAILED(capture->GetBuffer(&data,&frames,&flags,&position,&timestamp))) throw std::runtime_error("Capture read failed");
                    if(timestamp<previous || !frames) throw std::runtime_error("Invalid packet clock");
                    previous=timestamp; ++packets;
                    if(FAILED(capture->ReleaseBuffer(frames))) throw std::runtime_error("Capture release failed");
                } else Sleep(5);
            }
            client->Stop(); keepalive.stop(); keepalive.stop();
            if(packets<10) throw std::runtime_error("Silent renderer did not sustain real loopback packets");
            std::cout << "PASS physical WASAPI loopback packets=" << packets << "; monotonic clock; invalid endpoint rejected; cleanup joined\n";
            result=0;
        }
    } catch(const std::exception &e) { std::cerr << "FAIL: " << e.what() << '\n'; }
    CoUninitialize(); return result;
}
