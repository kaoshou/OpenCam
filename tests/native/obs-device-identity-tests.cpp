// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../native/obs-capture/device-identity.hpp"
#include <cassert>
#include <iostream>
int main() {
    using namespace opencam;
    std::vector<AudioDeviceIdentity> devices{
        {"{0.0.1.00000000}.{12345678-1234-1234-1234-123456789abc}","Microphone"},
        {"{0.0.1.00000000}.{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}","Microphone"}};
    auto selected=resolveAudioIdentity(devices,"@device_cm_{33d9a762-90c8-11d0-bd43-00a0c911ce86}\\wave_{12345678-1234-1234-1234-123456789ABC}",true);
    assert(selected==devices[0].id);
    assert(resolveAudioIdentity(devices,devices[1].id,true)==devices[1].id);
    assert(resolveAudioIdentity(devices,"default_mic",true)=="default");
    assert(resolveAudioIdentity(devices,"",true)=="default");
    for(const auto &requested:std::vector<std::string>{"Microphone","missing","@device_cm_{bad}\\wave_{12345678}"}) {
        bool rejected=false;try{resolveAudioIdentity(devices,requested,true);}catch(const std::exception &){rejected=true;}assert(rejected);
    }
    bool rejected=false;try{resolveAudioIdentity(devices,"@device_cm_{id}\\wave_{12345678-1234-1234-1234-123456789abc}",false);}catch(const std::exception &){rejected=true;}assert(rejected);
    assert(resolveAudioIdentity({{"stable-uid","麥克風"}},"麥克風",true)=="stable-uid");
    std::cout<<"Endpoint identity tests passed\n";
}
