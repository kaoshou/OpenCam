// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <algorithm>
#include <cctype>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>
namespace opencam {
struct AudioDeviceIdentity {std::string id,name;};
inline std::string asciiLower(std::string text) {
    for(auto &c:text)if(c>='A'&&c<='Z')c=static_cast<char>(c-'A'+'a');
    return text;
}
inline std::string resolveAudioIdentity(const std::vector<AudioDeviceIdentity> &devices,const std::string &requested,bool windows) {
    if(requested.empty()||requested=="default"||requested=="default_mic"||requested=="default_system")return "default";
    std::set<std::string> matches;
    for(const auto &d:devices)if(d.id==requested)matches.insert(d.id);
    if(matches.size()==1)return *matches.begin();
    if(windows) {
        auto lower=asciiLower(requested);
        if(lower.rfind("@device_",0)==0) {
            const auto at=lower.rfind("\\wave_{");
            if(at!=std::string::npos) {
                auto guid=lower.substr(at+7);
                if(guid.size()==37&&guid.back()=='}') {
                    guid.pop_back();
                    bool valid=true;
                    for(size_t i=0;i<guid.size();++i) {
                        if(i==8||i==13||i==18||i==23)valid=valid&&guid[i]=='-';
                        else valid=valid&&std::isxdigit(static_cast<unsigned char>(guid[i]));
                    }
                    if(valid)for(const auto &d:devices) {
                        auto id=asciiLower(d.id);auto suffix=".{"+guid+"}";
                        if(id.size()>=suffix.size()&&id.compare(id.size()-suffix.size(),suffix.size(),suffix)==0)matches.insert(d.id);
                    }
                }
            }
        } else for(const auto &d:devices)if(d.name==requested)matches.insert(d.id);
    }
    if(matches.size()!=1)throw std::runtime_error("audio_device_identity");
    return *matches.begin();
}
}
