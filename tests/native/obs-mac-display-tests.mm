// SPDX-License-Identifier: AGPL-3.0-or-later
// Exercise the production native identity boundary without starting OBS,
// requesting screen permission, or capturing any screen/audio content.
#define main opencam_capture_main
#include "../../native/obs-capture/main.cpp"
#undef main
#include <cassert>

int main() {
    uint32_t count=0; CGDirectDisplayID ids[64];
    assert(CGGetActiveDisplayList(64,ids,&count)==kCGErrorSuccess && count>0);
    for(uint32_t i=0;i<count;++i) {
        auto uuid=CGDisplayCreateUUIDFromDisplayID(ids[i]); assert(uuid);
        auto text=CFUUIDCreateString(nullptr,uuid); CFRelease(uuid);
        char buffer[128]{}; assert(CFStringGetCString(text,buffer,sizeof(buffer),kCFStringEncodingUTF8)); CFRelease(text);
        auto b=CGDisplayBounds(ids[i]);
        Request r{}; r.monitor=buffer;
        r.monitorWidth=static_cast<int>(CGDisplayPixelsWide(ids[i]));
        r.monitorHeight=static_cast<int>(CGDisplayPixelsHigh(ids[i]));
        double scale=r.monitorWidth/b.size.width;
        r.x=static_cast<int>(std::round(b.origin.x*scale));
        r.y=static_cast<int>(std::round(b.origin.y*scale));
        // Managed display enumeration sends CGDisplayPixelsWide/High, not
        // CGDisplayModeGetPixelWidth/Height (2x on a scaled Retina display).
        try {
            auto selected=monitorId(r);assert(selected.id==r.monitor);
            auto mode=CGDisplayCopyDisplayMode(ids[i]);assert(mode);
            assert(selected.width==CGDisplayModeGetPixelWidth(mode));
            assert(selected.height==CGDisplayModeGetPixelHeight(mode));CGDisplayModeRelease(mode);
        }
        catch(const std::exception &e) {
            std::cerr<<"FAIL: current managed display geometry rejected: "<<e.what()<<'\n'; return 1;
        }
        r.monitor="unknown-display";
        bool rejected=false;try{monitorId(r);}catch(const std::exception &){rejected=true;}assert(rejected);
        r.monitor=buffer; ++r.monitorWidth;
        rejected=false;try{monitorId(r);}catch(const std::exception &){rejected=true;}assert(rejected);
    }
    Request region{};region.monitorWidth=2240;region.monitorHeight=1260;
    region.width=2240;region.height=1260;
    auto full=captureCrop(region,4480,2520);
    assert(full.left==0&&full.top==0&&full.right==0&&full.bottom==0);
    region.cropX=100;region.cropY=50;region.width=640;region.height=360;
    auto retina=captureCrop(region,4480,2520);
    assert(retina.left==200&&retina.top==100&&retina.right==3000&&retina.bottom==1700);
    auto standard=captureCrop(region,2240,1260);
    assert(standard.left==100&&standard.top==50&&standard.right==1500&&standard.bottom==850);
    // Fractional mode scaling rounds the same edge consistently.
    auto fractional=captureCrop(region,3360,1890);
    assert(fractional.left==150&&fractional.top==75&&fractional.right==2250&&fractional.bottom==1275);
    region.cropX=101;region.cropY=51;
    auto rounded=captureCrop(region,3360,1890);
    assert(rounded.left==152&&rounded.top==77&&rounded.right==2248&&rounded.bottom==1273);
    std::cout<<"Native Mac display identity tests passed\n";
}
