// SPDX-License-Identifier: AGPL-3.0-or-later
#include "mux.hpp"
#include "device-identity.hpp"
#include "windows-audio-keepalive.hpp"
#include <util/platform.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>
#include <csignal>
#ifndef _WIN32
#include <poll.h>
#include <unistd.h>
#endif
#ifdef __APPLE__
#import <Cocoa/Cocoa.h>
#include <CoreGraphics/CoreGraphics.h>
#else
#include <windows.h>
#include <fcntl.h>
#include <io.h>
#endif

namespace fs = std::filesystem;
static std::mutex diagnostics;
static bool syntheticDiagnostics = false;
static void event(const std::string &json) {std::lock_guard<std::mutex> hold(diagnostics); std::cerr << "OCOBS " << json << '\n';}
static void logger(int level, const char *format, va_list arguments, void *) {
    // Raw upstream diagnostics are opt-in for generated fixtures only. Normal
    // capture never logs user paths, titles or endpoint names from libobs.
    if (syntheticDiagnostics && level <= LOG_INFO) {std::lock_guard<std::mutex> hold(diagnostics);std::vfprintf(stderr,format,arguments);std::fputc('\n',stderr);}
    // Retain bounded machine-readable WASAPI failures without endpoint names,
    // window titles, paths or arbitrary upstream text in normal recordings.
#ifdef _WIN32
    if(!syntheticDiagnostics && level==LOG_INFO && std::string(format).find("initialized (source: %s)")!=std::string::npos) {
        char text[4096]{}; va_list copy; va_copy(copy,arguments);
        std::vsnprintf(text,sizeof(text),format,copy); va_end(copy);
        std::string message(text); const auto suffix=message.rfind("(source: ");
        const auto role=suffix==std::string::npos?std::string():message.substr(suffix);
        if(role=="(source: system)" || role=="(source: microphone)")
            event("{\"event\":\"audio_transport\",\"state\":\"source_initialized\",\"source\":\""+
                std::string(role=="(source: system)"?"system":"microphone")+"\",\"hresult\":0}");
    }
    if(!syntheticDiagnostics && level<=LOG_WARNING &&
        (std::string(format).find("WASAPISource::TryInitialize")!=std::string::npos ||
         std::string(format).find("WASAPISource::ProcessCaptureData")!=std::string::npos)) {
        char text[4096]{}; va_list copy; va_copy(copy,arguments);
        std::vsnprintf(text,sizeof(text),format,copy); va_end(copy);
        std::string message(text); auto split=message.find_last_of(" \r\n\t");
        auto code=split==std::string::npos?std::string():message.substr(split+1);
        uint32_t hr=0;
        if(!code.empty()&&code.size()<=8&&code.find_first_not_of("0123456789abcdefABCDEF")==std::string::npos)
            hr=static_cast<uint32_t>(std::stoul(code,nullptr,16));
        event("{\"event\":\"audio_transport\",\"state\":\"source_warning\",\"hresult\":"+std::to_string(hr)+"}");
    }
#endif
}
static std::string startupDetails();
static void error(const char *code) {event(std::string("{\"event\":\"error\",\"code\":\"") + code + "\"" + startupDetails() + "}");}
static void pump() {
#ifdef __APPLE__
    @autoreleasepool {NSEvent *ev=[NSApp nextEventMatchingMask:NSEventMaskAny untilDate:[NSDate dateWithTimeIntervalSinceNow:0.005] inMode:NSDefaultRunLoopMode dequeue:YES];if(ev)[NSApp sendEvent:ev];}
#else
    os_sleep_ms(5);
#endif
}
static bool line(std::string &result) {
    result.clear(); char ch;
    for(;;) {
#ifdef _WIN32
        DWORD count=0;if(!ReadFile(GetStdHandle(STD_INPUT_HANDLE),&ch,1,&count,nullptr)||count==0)break;
#else
        if(::read(STDIN_FILENO,&ch,1)!=1)break;
#endif
        if (ch == '\n') return true; if (result.size() >= 65536) return false; result.push_back(ch);
    }
    return !result.empty();
}
static bool stopRequested() {
    // No detached reader can outlive State during an output/device failure.
    static std::string command;
    for(;;) {
        char ch;
#ifdef _WIN32
        DWORD available=0,count=0;
        if(!PeekNamedPipe(GetStdHandle(STD_INPUT_HANDLE),nullptr,0,nullptr,&available,nullptr))return true;
        if(!available)return false;
        if(!ReadFile(GetStdHandle(STD_INPUT_HANDLE),&ch,1,&count,nullptr)||count==0)return true;
#else
        pollfd p{STDIN_FILENO,POLLIN,0};if(poll(&p,1,0)<=0)return false;
        if(::read(STDIN_FILENO,&ch,1)!=1)return true;
#endif
        if(ch=='\n')return true; // STOP or malformed control closes the lease safely.
        if(command.size()>=16)return true;command.push_back(ch);
    }
}
struct Request {
    int width, height, fps, x, y, monitorWidth, monitorHeight, cropX, cropY, quality;
    bool synthetic, cursor; std::string audio, microphone, systemDevice, monitor, encoder;
    bool hasAudio() const {return audio != "none";}
    bool system() const {return audio == "system" || audio == "both";}
    bool mic() const {return audio == "microphone" || audio == "both";}
};
static Request request(obs_data_t *d) {
    if (!d || obs_data_get_int(d,"protocol") != 1) throw std::runtime_error("invalid_request");
    auto number = [&](const char *key) {double v = obs_data_get_double(d,key); if (!std::isfinite(v) || v < -1000000 || v > 1000000 || std::floor(v) != v) throw std::runtime_error("invalid_request"); return static_cast<int>(v);};
    Request r{number("width"),number("height"),number("fps"),number("x"),number("y"),number("monitorWidth"),number("monitorHeight"),number("cropX"),number("cropY"),number("quality"),obs_data_get_bool(d,"synthetic"),obs_data_get_bool(d,"cursor"),obs_data_get_string(d,"audio"),obs_data_get_string(d,"microphone"),obs_data_get_string(d,"systemDevice"),obs_data_get_string(d,"monitor"),obs_data_get_string(d,"encoder")};
    if (r.width < 2 || r.height < 2 || r.width > 16384 || r.height > 16384 || r.width % 2 || r.height % 2 || r.fps < 1 || r.fps > 120 || r.cropX < 0 || r.cropY < 0 || r.cropX + r.width > r.monitorWidth || r.cropY + r.height > r.monitorHeight || (r.quality != 18 && r.quality != 23 && r.quality != 28)) throw std::runtime_error("invalid_request");
    if (r.audio != "none" && r.audio != "system" && r.audio != "microphone" && r.audio != "both" && r.audio != "silence") throw std::runtime_error("invalid_audio");
    if (r.encoder != "cpu" && r.encoder != "nvenc" && r.encoder != "qsv" && r.encoder != "amf" && r.encoder != "apple") throw std::runtime_error("invalid_encoder");
    return r;
}
struct State {
    Request r; Mux mux; obs_output_t *output = nullptr;
    std::atomic<bool> stopping{false}, stopped{false}, commandStop{false}, failed{false};
    std::atomic<bool> sourcePropertiesChanged{true};
    std::atomic<bool> systemSamples{false}, micSamples{false};
    std::atomic<uint64_t> systemAt{0}, micAt{0};
    std::atomic<float> systemPeak{0}, micPeak{0};
    uint64_t epoch = 0;
    uint64_t startupAt = os_gettime_ns();
    const char *stage = "initializing";
    uint32_t sourceWidth = 0, sourceHeight = 0;
    uint32_t captureWidth, captureHeight;
    int64_t testFailureMs = -1;
    explicit State(Request request) : r(std::move(request)), captureWidth(r.monitorWidth), captureHeight(r.monitorHeight) {}
};
static State *state;
// Only fixed phase names, counters and readiness facts cross the process boundary.
// Never include source names, identifiers, paths, window titles or raw libobs logs.
static std::string startupDetails() {
    if(!state)return "";
    auto &s=*state;
    return std::string(",\"startup\":{\"stage\":\"")+s.stage+"\",\"elapsedMs\":"+std::to_string((os_gettime_ns()-s.startupAt)/1000000ULL)+
        ",\"sourceWidth\":"+std::to_string(s.sourceWidth)+",\"sourceHeight\":"+std::to_string(s.sourceHeight)+
        ",\"expectedWidth\":"+std::to_string(s.captureWidth)+",\"expectedHeight\":"+std::to_string(s.captureHeight)+
        ",\"systemSelected\":"+(s.r.system()?"true":"false")+",\"microphoneSelected\":"+(s.r.mic()?"true":"false")+
        ",\"systemSamples\":"+(s.systemSamples.load()?"true":"false")+",\"microphoneSamples\":"+(s.micSamples.load()?"true":"false")+
        ",\"frames\":"+std::to_string(s.mux.frames.load())+",\"bytes\":"+std::to_string(s.mux.bytes.load())+"}";
}
static const char *name(void *) {return "OpenCam owned MKV output";}
static void *create(obs_data_t *, obs_output_t *output) {state->output = output; return state;}
static void destroy(void *) {}
static bool start(void *data) {
    auto *s = static_cast<State *>(data);
    if (!obs_output_can_begin_data_capture(s->output, 0) || !obs_output_initialize_encoders(s->output, 0)) return false;
    try {s->mux.open(s->output, s->r.width, s->r.height, s->r.fps, s->r.hasAudio());}
    catch (...) {return false;}
    return obs_output_begin_data_capture(s->output, 0);
}
static void stop(void *data, uint64_t) {
    auto *s = static_cast<State *>(data); s->stopping = true;
    // Detach encoding callbacks asynchronously; final trailer is written only
    // after data capture deactivates (the main loop observes that signal).
    obs_output_end_data_capture(s->output);
}
static void packet(void *data, encoder_packet *p) {static_cast<State *>(data)->mux.packet(p);}
static void deactivate(void *data, calldata_t *) {static_cast<State *>(data)->stopped = true;}
static void outputError(void *data, calldata_t *cd) {
    if (calldata_int(cd,"code") != OBS_OUTPUT_SUCCESS) static_cast<State *>(data)->failed = true;
}
static void sample(void *data, obs_source_t *, const audio_data *audio, bool) {
    bool mic = reinterpret_cast<uintptr_t>(data) == 1;
    float peak = 0;
    // Callback audio has been converted to the configured float-planar mix.
    for (size_t channel = 0; channel < 2 && audio->data[channel]; ++channel) {
        auto *values = reinterpret_cast<const float *>(audio->data[channel]);
        for (uint32_t i=0;i<audio->frames;++i) if (std::isfinite(values[i])) peak=std::max(peak,std::abs(values[i]));
    }
    if (audio->frames) {if (mic) {state->micSamples = true;state->micAt=os_gettime_ns();} else {state->systemSamples = true;state->systemAt=os_gettime_ns();}}
    (mic ? state->micPeak : state->systemPeak) = std::min(1.0f,peak);
}
static void progress(State &s, uint64_t now) {
    auto age=[&](uint64_t captured)->int64_t{return captured?static_cast<int64_t>((now>=captured?now-captured:0)/1000):-1;};
    event("{\"event\":\"progress\",\"frames\":"+std::to_string(s.mux.frames.load())+",\"mediaUs\":"+std::to_string(s.mux.mediaUs.load())+",\"bytes\":"+std::to_string(s.mux.bytes.load())+",\"systemPeak\":"+std::to_string(s.systemPeak.load())+",\"microphonePeak\":"+std::to_string(s.micPeak.load())+",\"systemAgeUs\":"+std::to_string(age(s.systemAt.load()))+",\"microphoneAgeUs\":"+std::to_string(age(s.micAt.load()))+
        ",\"renderFrames\":"+std::to_string(obs_get_total_frames())+",\"laggedFrames\":"+std::to_string(obs_get_lagged_frames())+",\"averageRenderNs\":"+std::to_string(obs_get_average_frame_time_ns())+"}");
}
static void sourcePropertiesChanged(void *data, calldata_t *) {
    // ScreenCaptureKit signals fatal source loss while keeping its last texture
    // and geometry. Inspect its status later, never re-enter from its callback.
    static_cast<State *>(data)->sourcePropertiesChanged = true;
}
static bool sourceFailed(obs_source_t *screen, bool required) {
    auto *properties=obs_source_properties(screen);
    auto *reactivate=properties?obs_properties_get(properties,"reactivate_capture"):nullptr;
    bool failed=required&&(!reactivate||obs_property_enabled(reactivate));
    obs_properties_destroy(properties);return failed;
}
static bool wgcSupported() {
    // Pinned OBS substitutes DXGI when WGC is unsupported. Never misreport that
    // substitution as WGC or silently change a pinned recording backend.
    auto *properties=obs_get_source_properties("monitor_capture");
    auto *method=properties?obs_properties_get(properties,"method"):nullptr;bool supported=false;
    for(size_t i=0;method&&i<obs_property_list_item_count(method);++i)
        if(obs_property_list_item_int(method,i)==2&&!obs_property_list_item_disabled(method,i))supported=true;
    obs_properties_destroy(properties);return supported;
}
static void *testCreate(obs_data_t *, obs_source_t *) {return state;}
static obs_properties_t *testProperties(void *) {
    auto *properties=obs_properties_create();
    auto *reactivate=obs_properties_add_bool(properties,"reactivate_capture","Generated source status");
    obs_property_set_enabled(reactivate,state->testFailureMs>=0&&os_gettime_ns()-state->epoch>=static_cast<uint64_t>(state->testFailureMs)*1000000ULL);
    return properties;
}
static uint32_t testWidth(void *) {return state->r.monitorWidth;}
static uint32_t testHeight(void *) {return state->r.monitorHeight;}
static void testRender(void *, gs_effect_t *) {
    uint64_t now=obs_get_video_frame_time();
    double elapsed=now >= state->epoch ? (now-state->epoch)/1e9 : 0;
    vec4 color; bool flash=std::fmod(elapsed,1.0)<0.10;
    vec4_set(&color,flash?1.0f:0.08f,flash?1.0f:0.12f,flash?1.0f:0.20f,1.0f);
    auto *effect=obs_get_base_effect(OBS_EFFECT_SOLID);
    gs_effect_set_vec4(gs_effect_get_param_by_name(effect,"color"),&color);
    while(gs_effect_loop(effect,"Solid")) gs_draw_sprite(nullptr,0,state->r.width,state->r.height);
    vec4_set(&color,0.1f,0.9f,0.3f,1.0f);
    gs_effect_set_vec4(gs_effect_get_param_by_name(effect,"color"),&color);
    gs_matrix_push(); gs_matrix_translate3f(static_cast<float>(std::fmod(elapsed*90,state->r.width-32)),40,0);
    while(gs_effect_loop(effect,"Solid")) gs_draw_sprite(nullptr,0,32,32);
    gs_matrix_pop();
}
static void load(const fs::path &root, const char *module) {
#ifdef __APPLE__
    fs::path base=root/"Contents"/"PlugIns"/(std::string(module)+".plugin")/"Contents";
    auto binary=base/"MacOS"/module; auto resources=base/"Resources";
#else
    auto binary=root/"obs-plugins"/"64bit"/(std::string(module)+".dll"); auto resources=root/"data"/"obs-plugins"/module;
#endif
    obs_module_t *m=nullptr;
    if (obs_open_module(&m,binary.u8string().c_str(),resources.u8string().c_str()) != MODULE_SUCCESS || !obs_init_module(m)) throw std::runtime_error("runtime_module");
}
static obs_source_t *source(const char *id, const char *label, obs_data_t *settings) {
    auto *s=obs_source_create_private(id,label,settings);
    if (!s) throw std::runtime_error("capture_source");
    return s;
}
static std::string exactDevice(const char *sourceId, const std::string &requested) {
    if (requested.empty() || requested=="default_mic" || requested=="default_system" || requested=="default") return "default";
    auto *props=obs_get_source_properties(sourceId);
    auto *p=props?obs_properties_get(props,"device_id"):nullptr;
    std::vector<opencam::AudioDeviceIdentity> candidates;
    for(size_t i=0;p&&i<obs_property_list_item_count(p);++i) {
        const char *value=obs_property_list_item_string(p,i);
        const char *label=obs_property_list_item_name(p,i);
        if (value) candidates.push_back({value,label?label:""});
    }
    obs_properties_destroy(props);
#ifdef _WIN32
    return opencam::resolveAudioIdentity(candidates,requested,true);
#else
    return opencam::resolveAudioIdentity(candidates,requested,false);
#endif
}
struct CaptureDisplay {std::string id; uint32_t width,height;};
static CaptureDisplay monitorId(const Request &r) {
#ifdef __APPLE__
    uint32_t count=0; CGDirectDisplayID ids[64];
    if(CGGetActiveDisplayList(64,ids,&count)!=kCGErrorSuccess) throw std::runtime_error("display_identity");
    for(uint32_t i=0;i<count;++i) {
        auto uuid=CGDisplayCreateUUIDFromDisplayID(ids[i]); if(!uuid) continue;
        auto text=CFUUIDCreateString(nullptr,uuid); CFRelease(uuid);
        char buffer[128]={0}; bool valid=text&&CFStringGetCString(text,buffer,sizeof(buffer),kCFStringEncodingUTF8); if(text)CFRelease(text);
        if(valid&&r.monitor==buffer) {
            auto b=CGDisplayBounds(ids[i]); auto mode=CGDisplayCopyDisplayMode(ids[i]); if(!mode)break;
            // Managed enumeration uses desktop coordinate pixels. Scaled Retina
            // modes have a different backing texture size; validate the former,
            // then pin the latter for source readiness and crop conversion.
            size_t captureW=CGDisplayModeGetPixelWidth(mode),captureH=CGDisplayModeGetPixelHeight(mode); CGDisplayModeRelease(mode);
            size_t w=CGDisplayPixelsWide(ids[i]),h=CGDisplayPixelsHigh(ids[i]);
            double scale=b.size.width>0?w/b.size.width:1;
            if(captureW>0&&captureH>0&&r.monitorWidth==static_cast<int>(w)&&r.monitorHeight==static_cast<int>(h)&&r.x-r.cropX==static_cast<int>(std::round(b.origin.x*scale))&&r.y-r.cropY==static_cast<int>(std::round(b.origin.y*scale))) return {buffer,static_cast<uint32_t>(captureW),static_cast<uint32_t>(captureH)};
        }
    }
#else
    // The OBS monitor capture property identifies a display target, not a UI
    // enumeration index. Resolve its target path through the active topology.
    UINT32 pathsCount=0,modesCount=0;
    if(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pathsCount,&modesCount)!=ERROR_SUCCESS) throw std::runtime_error("display_identity");
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathsCount); std::vector<DISPLAYCONFIG_MODE_INFO> modes(modesCount);
    if(QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pathsCount,paths.data(),&modesCount,modes.data(),nullptr)!=ERROR_SUCCESS) throw std::runtime_error("display_identity");
    for(UINT32 i=0;i<pathsCount;++i) {
        DISPLAYCONFIG_SOURCE_DEVICE_NAME src{}; src.header={DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,sizeof(src),paths[i].sourceInfo.adapterId,paths[i].sourceInfo.id};
        if(DisplayConfigGetDeviceInfo(&src.header)!=ERROR_SUCCESS)continue;
        char device[256]={0}; WideCharToMultiByte(CP_UTF8,0,src.viewGdiDeviceName,-1,device,sizeof(device),nullptr,nullptr);
        auto index=paths[i].sourceInfo.modeInfoIdx; if(index>=modesCount)break; const auto &m=modes[index].sourceMode;
        DISPLAYCONFIG_TARGET_DEVICE_NAME target{}; target.header={DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,sizeof(target),paths[i].targetInfo.adapterId,paths[i].targetInfo.id};
        if(DisplayConfigGetDeviceInfo(&target.header)!=ERROR_SUCCESS)continue;
        char id[1024]={0}; WideCharToMultiByte(CP_UTF8,0,target.monitorDevicePath,-1,id,sizeof(id),nullptr,nullptr);
        if(r.monitor!=device&&opencam::asciiLower(r.monitor)!=opencam::asciiLower(id))continue;
        if(static_cast<int>(m.width)!=r.monitorWidth||static_cast<int>(m.height)!=r.monitorHeight||m.position.x!=r.x-r.cropX||m.position.y!=r.y-r.cropY)break;
        // The OBS module can resolve the exact current GDI name without any
        // case-sensitive mismatch between two equivalent interface-path APIs.
        return {device,m.width,m.height};
    }
#endif
    throw std::runtime_error("display_identity");
}
static obs_sceneitem_crop captureCrop(const Request &r,uint32_t width,uint32_t height) {
    // Round shared edges rather than widths independently, avoiding gaps at a
    // fractional scale and ensuring a full-display crop keeps every pixel.
    auto x=[&](int edge){return static_cast<int>(std::round(static_cast<double>(edge)*width/r.monitorWidth));};
    auto y=[&](int edge){return static_cast<int>(std::round(static_cast<double>(edge)*height/r.monitorHeight));};
    return {x(r.cropX),y(r.cropY),static_cast<int>(width)-x(r.cropX+r.width),static_cast<int>(height)-y(r.cropY+r.height)};
}

static int run(int argc,char **argv) {
#ifdef _WIN32
    WindowsAudioKeepalive systemClock;
#endif
    base_set_log_handler(logger,nullptr);
#ifdef _WIN32
    _setmode(_fileno(stdout),_O_BINARY);
#endif
#ifndef _WIN32
    std::signal(SIGPIPE,SIG_IGN);
#endif
    if(argc!=3||std::string(argv[1])!="--runtime") {error("invalid_arguments");return 2;}
    std::string input; if(!line(input)) {error("invalid_request");return 2;}
    auto *data=obs_data_create_from_json(input.c_str());
    Request r{}; try {r=request(data);}catch(const std::exception &e){obs_data_release(data);error(e.what());return 2;} obs_data_release(data);
#ifdef __APPLE__
    // Reject malformed protocol input before contacting WindowServer/AppKit.
    // Invalid requests must emit a bounded error, not initialize capture UI.
    [NSApplication sharedApplication];
#endif
    State s(r); state=&s;
    syntheticDiagnostics=r.synthetic && std::getenv("OPENCAM_OBS_SYNTHETIC_DIAGNOSTICS");
    fs::path root=fs::u8path(argv[2]);
    if(!root.is_absolute()) {error("runtime_path");return 2;}
    bool initialized=false; obs_source_t *screen=nullptr,*system=nullptr,*mic=nullptr; obs_scene_t *scene=nullptr;
    obs_encoder_t *videoEncoder=nullptr,*audioEncoder=nullptr; obs_output_t *output=nullptr;
    std::thread audioThread; int result=1;
    try {
        s.stage="core_startup";
        if(!obs_startup("en-US",nullptr,nullptr)) throw std::runtime_error("core_startup"); initialized=true;
        if(obs_get_version()!=LIBOBS_API_VER) throw std::runtime_error("core_version");
#ifdef __APPLE__
        // OBS33 labels its Metal renderer alpha-quality. Prefer its established
        // compositor backend; ScreenCaptureKit still supplies GPU surfaces.
        auto graphics=root/"Contents"/"Frameworks"/"libobs-opengl.dylib";
        auto dataPrefix=(root/"Contents"/"Frameworks"/"libobs.framework"/"Resources").generic_u8string()+"/";
#else
        auto graphics=root/"bin"/"64bit"/"libobs-d3d11.dll";
        auto dataPrefix=(root/"data"/"libobs").generic_u8string()+"/";
#endif
        // The pinned libobs data search concatenates its prefix and filename
        // verbatim; a missing separator becomes "libobsdefault.effect".
        obs_add_data_path(dataPrefix.c_str());
        std::string graphicsPath=graphics.u8string();
        obs_video_info vi{}; vi.graphics_module=graphicsPath.c_str(); vi.fps_num=r.fps; vi.fps_den=1;
        vi.base_width=vi.output_width=r.width; vi.base_height=vi.output_height=r.height;
        vi.output_format=VIDEO_FORMAT_NV12; vi.colorspace=VIDEO_CS_709; vi.range=VIDEO_RANGE_PARTIAL; vi.scale_type=OBS_SCALE_BILINEAR; vi.gpu_conversion=true;
        s.stage="graphics";
        if(obs_reset_video(&vi)!=OBS_VIDEO_SUCCESS) throw std::runtime_error("graphics_startup");
        obs_audio_info ai{48000,SPEAKERS_STEREO}; if(!obs_reset_audio(&ai))throw std::runtime_error("audio_startup");
        s.stage="modules";
        load(root,"obs-x264"); load(root,"obs-ffmpeg");
#ifdef __APPLE__
        if(!r.synthetic)load(root,"mac-capture"); if(r.encoder=="apple")load(root,"mac-videotoolbox");
#else
        if(!r.synthetic){load(root,"win-capture");load(root,"win-wasapi");}
        if(r.encoder=="nvenc")load(root,"obs-nvenc");
        if(r.encoder=="qsv")load(root,"obs-qsv11");
        // AMF is registered by obs-ffmpeg, not a separate enc-amf module.
#endif
        obs_post_load_modules();
        obs_output_info oi{}; oi.id="opencam_owned_mkv"; oi.flags=OBS_OUTPUT_ENCODED|OBS_OUTPUT_VIDEO|(r.hasAudio()?OBS_OUTPUT_AUDIO:0);
        oi.get_name=name;oi.create=create;oi.destroy=destroy;oi.start=start;oi.stop=stop;oi.encoded_packet=packet;oi.encoded_video_codecs="h264";oi.encoded_audio_codecs="aac";obs_register_output(&oi);
        s.epoch=os_gettime_ns();
        if(r.synthetic) {
            const char *failure=std::getenv("OPENCAM_OBS_TEST_SOURCE_FAILURE_MS");
            if(failure&&std::string(failure)=="0")s.testFailureMs=0;
            if(failure&&std::string(failure)=="1200")s.testFailureMs=1200;
        }
        s.stage="capture_source";
        auto *settings=obs_data_create();
        if(r.synthetic) {
            // Silence exercises the same device-free OBS mixer path as real
            // recovery mode; do not make it pass by adding a generated device.
            obs_source_info sourceInfo{};sourceInfo.id="opencam_clock_source";sourceInfo.type=OBS_SOURCE_TYPE_INPUT;sourceInfo.output_flags=OBS_SOURCE_VIDEO|OBS_SOURCE_CUSTOM_DRAW|(r.hasAudio()&&r.audio!="silence"?OBS_SOURCE_AUDIO:0);
            sourceInfo.get_name=name;sourceInfo.create=testCreate;sourceInfo.destroy=destroy;sourceInfo.get_width=testWidth;sourceInfo.get_height=testHeight;sourceInfo.video_render=testRender;sourceInfo.get_properties=testProperties;obs_register_source(&sourceInfo);
            screen=source("opencam_clock_source","clock",settings); system=obs_source_get_ref(screen);
            if(r.mic())mic=obs_source_get_ref(screen);
        }else {
            auto monitor=monitorId(r);
            s.captureWidth=monitor.width;s.captureHeight=monitor.height;
#ifdef __APPLE__
            obs_data_set_string(settings,"display_uuid",monitor.id.c_str());obs_data_set_int(settings,"type",0);obs_data_set_bool(settings,"show_cursor",r.cursor);obs_data_set_bool(settings,"hide_obs",false);
            screen=source("screen_capture","display",settings);
#else
            if(!wgcSupported())throw std::runtime_error("capture_source");
            obs_data_set_string(settings,"monitor_id",monitor.id.c_str());obs_data_set_int(settings,"method",2);obs_data_set_bool(settings,"capture_cursor",r.cursor);obs_data_set_bool(settings,"force_sdr",true);
            screen=source("monitor_capture","display",settings);
#endif
        }
        obs_data_release(settings);
        signal_handler_connect(obs_source_get_signal_handler(screen),"update_properties",sourcePropertiesChanged,&s);
#ifdef __APPLE__
        const bool sourceStatusRequired=true;
#else
        const bool sourceStatusRequired=r.synthetic;
#endif
        scene=obs_scene_create_private("recording"); auto *item=obs_scene_add(scene,screen);
        auto crop=captureCrop(r,s.captureWidth,s.captureHeight);obs_sceneitem_set_crop(item,&crop);
        vec2 outputBounds;vec2_set(&outputBounds,static_cast<float>(r.width),static_cast<float>(r.height));
        obs_sceneitem_set_bounds_type(item,OBS_BOUNDS_STRETCH);obs_sceneitem_set_bounds(item,&outputBounds);
        s.stage="audio_sources";
        if(!r.synthetic) {
            // Do not mix implicit screen-source audio in addition to the explicit source.
            obs_source_set_audio_mixers(screen,0);
            if(r.system()) {
                auto *a=obs_data_create();
#ifdef __APPLE__
                if(!r.systemDevice.empty()&&r.systemDevice!="default_system"&&r.systemDevice!="default")throw std::runtime_error("audio_device_identity");
                obs_data_set_int(a,"type",0);system=source("sck_audio_capture","system",a);
#else
                auto id=exactDevice("wasapi_output_capture",r.systemDevice);
                // Resolve once and bind OBS and the silent renderer to the same endpoint.
                // A successful renderer alone is not capture readiness: sample() is still required.
                try { id=systemClock.start(id); }
                catch(...) {
                    event("{\"event\":\"audio_transport\",\"state\":\"failed\",\"hresult\":"+std::to_string(static_cast<uint32_t>(systemClock.error()))+"}");
                    throw;
                }
                event("{\"event\":\"audio_transport\",\"state\":\"initialized\",\"hresult\":0}");
                obs_data_set_string(a,"device_id",id.c_str());obs_data_set_bool(a,"use_device_timing",true);system=source("wasapi_output_capture","system",a);
#endif
                obs_data_release(a);obs_set_output_source(1,system);
            }
            if(r.mic()) {
                auto *a=obs_data_create();
#ifdef __APPLE__
                auto id=exactDevice("coreaudio_input_capture",r.microphone);obs_data_set_string(a,"device_id",id.c_str());mic=source("coreaudio_input_capture","microphone",a);
#else
                auto id=exactDevice("wasapi_input_capture",r.microphone);obs_data_set_string(a,"device_id",id.c_str());obs_data_set_bool(a,"use_device_timing",true);mic=source("wasapi_input_capture","microphone",a);
#endif
                obs_data_release(a);obs_set_output_source(2,mic);
            }
        }
        obs_set_output_source(0,obs_scene_get_source(scene));
        if(system)obs_source_add_audio_capture_callback(system,sample,nullptr);
        if(mic)obs_source_add_audio_capture_callback(mic,sample,reinterpret_cast<void *>(1));
        if(r.synthetic&&r.hasAudio()&&r.audio!="silence") {
            audioThread=std::thread([&]{
                float samples[2][480]{}; uint64_t frame=0;
                while(!s.commandStop) {
                    uint64_t target=s.epoch+frame*1000000000ULL/48000;
                    if(os_gettime_ns()<target) {os_sleep_ms(1);continue;}
                    double seconds=frame/48000.0;
                    for(int i=0;i<480;++i){double t=seconds+i/48000.0;float value=r.audio=="silence"?0.0f:static_cast<float>(std::fmod(t,1.0)<0.10?0.2*std::sin(t*2*3.141592653589793*1000):0);samples[0][i]=samples[1][i]=value;}
                    obs_source_audio a{};a.data[0]=reinterpret_cast<uint8_t *>(samples[0]);a.data[1]=reinterpret_cast<uint8_t *>(samples[1]);a.frames=480;a.speakers=SPEAKERS_STEREO;a.samples_per_sec=48000;a.format=AUDIO_FORMAT_FLOAT_PLANAR;a.timestamp=target;obs_source_output_audio(screen,&a);frame+=480;
                }
            });
        }
        auto *enc=obs_data_create();obs_data_set_string(enc,"rate_control","CRF");obs_data_set_int(enc,"crf",r.quality);obs_data_set_string(enc,"preset","veryfast");obs_data_set_string(enc,"tune","zerolatency");obs_data_set_int(enc,"keyint_sec",2);
        const char *videoId="obs_x264";
        if(r.encoder=="nvenc"){videoId="obs_nvenc_h264_tex";obs_data_set_string(enc,"rate_control","CQP");obs_data_set_int(enc,"cqp",r.quality);obs_data_set_string(enc,"preset","p4");obs_data_set_int(enc,"bf",0);}
        if(r.encoder=="qsv"){videoId="obs_qsv11_v2";obs_data_set_string(enc,"rate_control","ICQ");obs_data_set_int(enc,"icq_quality",r.quality);obs_data_set_int(enc,"bf",0);}
        if(r.encoder=="amf"){videoId="h264_texture_amf";obs_data_set_string(enc,"rate_control","CQP");obs_data_set_int(enc,"cqp",r.quality);obs_data_set_int(enc,"bf",0);}
        if(r.encoder=="apple"){videoId="com.apple.videotoolbox.videoencoder.ave.avc";obs_data_set_string(enc,"rate_control","ABR");obs_data_set_int(enc,"bitrate",r.quality==18?10000:r.quality==28?3500:6000);obs_data_set_bool(enc,"bframes",false);}
        videoEncoder=obs_video_encoder_create(videoId,"video",enc,nullptr);obs_data_release(enc);
        if(!videoEncoder)throw std::runtime_error("encoder_unavailable");obs_encoder_set_video(videoEncoder,obs_get_video());
        if(r.hasAudio()) {auto *a=obs_data_create();obs_data_set_int(a,"bitrate",192);audioEncoder=obs_audio_encoder_create("ffmpeg_aac","audio",a,0,nullptr);obs_data_release(a);if(!audioEncoder)throw std::runtime_error("audio_encoder");obs_encoder_set_audio(audioEncoder,obs_get_audio());}
        output=obs_output_create("opencam_owned_mkv","owned",nullptr,nullptr);if(!output)throw std::runtime_error("mux_create");
        obs_output_set_video_encoder(output,videoEncoder);if(audioEncoder)obs_output_set_audio_encoder(output,audioEncoder,0);
        signal_handler_connect(obs_output_get_signal_handler(output),"deactivate",deactivate,&s);signal_handler_connect(obs_output_get_signal_handler(output),"stop",outputError,&s);
        // Do not write black placeholder frames or a falsely initialized audio
        // track while a selected source is still being opened. An unsupported
        // setup therefore fails before emitting media. The caller decides its
        // build policy; a native-only build must never switch recording cores.
        s.stage="source_wait";
        const uint64_t sourceDeadline=os_gettime_ns()+10000000000ULL;
        for(;;) {
#ifdef _WIN32
            if(FAILED(systemClock.error())) throw std::runtime_error("audio_device_lost");
#endif
            s.sourceWidth=obs_source_get_width(screen);s.sourceHeight=obs_source_get_height(screen);
            if(s.sourcePropertiesChanged.exchange(false)&&sourceFailed(screen,sourceStatusRequired))throw std::runtime_error("capture_source");
            bool geometry=s.sourceWidth==s.captureWidth&&s.sourceHeight==s.captureHeight;
            bool sound=(!r.system()||s.systemSamples)&&(!r.mic()||s.micSamples);
            if(geometry&&sound)break;
            if(stopRequested())throw std::runtime_error("capture_cancelled");
            if(os_gettime_ns()>sourceDeadline)throw std::runtime_error("capture_start_timeout");
            pump();
        }
        s.stage="encoder_start";
        if(!obs_output_start(output))throw std::runtime_error("encoder_start");
        s.stage="first_frame";
        // stdin is the ownership lease. EOF after the request is also a stop.
        bool ready=false;uint64_t last=0,geometryLostAt=0;bool systemWarned=false,micWarned=false;const uint64_t deadline=os_gettime_ns()+10000000000ULL;
        while(!s.stopped) {
            if(stopRequested())s.commandStop=true;
            pump();
            uint64_t now=os_gettime_ns();
#ifdef _WIN32
            if(FAILED(systemClock.error())) {
                event("{\"event\":\"audio_transport\",\"state\":\"lost\",\"hresult\":"+std::to_string(static_cast<uint32_t>(systemClock.error()))+"}");
                // Seal packets before publishing the fatal source error: the
                // managed owner may terminate us immediately on that event.
                obs_output_stop(output);
                const uint64_t sealDeadline=os_gettime_ns()+3000000000ULL;
                while(!s.stopped&&os_gettime_ns()<sealDeadline) pump();
                if(s.stopped) { s.mux.finish(); if(ready) progress(s,os_gettime_ns()); }
                throw std::runtime_error("audio_device_lost");
            }
#endif
            s.sourceWidth=obs_source_get_width(screen);s.sourceHeight=obs_source_get_height(screen);
            if(r.synthetic&&s.testFailureMs>=0&&now-s.epoch>=static_cast<uint64_t>(s.testFailureMs)*1000000ULL)s.sourcePropertiesChanged=true;
            if(s.sourcePropertiesChanged.exchange(false)&&sourceFailed(screen,sourceStatusRequired))throw std::runtime_error("capture_source_lost");
            if(s.failed||s.mux.failed)throw std::runtime_error("recording_output");
            bool geometry=s.sourceWidth==s.captureWidth&&s.sourceHeight==s.captureHeight;
            bool sound=(!r.system()||s.systemSamples)&&(!r.mic()||s.micSamples);
            if(ready&&!geometry) {if(!geometryLostAt)geometryLostAt=now;else if(now-geometryLostAt>2000000000ULL)throw std::runtime_error("capture_geometry_lost");}else geometryLostAt=0;
            if(!ready&&s.mux.frames&&geometry&&sound) {
                ready=true;
#ifdef __APPLE__
                const char *backend=r.synthetic?"synthetic":"sck";
#else
                const char *backend=r.synthetic?"synthetic":"wgc";
#endif
                event(std::string("{\"event\":\"ready\",\"protocol\":1,\"backend\":\"")+backend+"\",\"encoder\":\""+r.encoder+"\""+startupDetails()+"}");
                s.stage="recording";
            }
            if(!ready&&now>deadline)throw std::runtime_error("capture_start_timeout");
            auto age=[&](uint64_t captured)->int64_t{return captured?static_cast<int64_t>((now>=captured?now-captured:0)/1000):-1;};
            const auto systemAge=age(s.systemAt.load()),micAge=age(s.micAt.load());
            auto warn=[&](bool selected,int64_t since,bool &warned,const char *name) {bool late=selected&&(since<0||since>2000000);if(ready&&late&&!warned)event(std::string("{\"event\":\"warning\",\"code\":\"audio_source_stalled\",\"source\":\"")+name+"\"}");warned=late;};
            warn(r.system(),systemAge,systemWarned,"system");warn(r.mic(),micAge,micWarned,"microphone");
            if(ready&&now-last>500000000ULL) {last=now;progress(s,now);}
            if(s.commandStop&&!s.stopping){s.stage="stopping";obs_output_stop(output);}
        }
        s.mux.finish(); if(s.mux.failed)throw std::runtime_error("mux_finalize");
        // Pause/stop seals the segment using these counters, including the last
        // encoder packets rather than the previous half-second observation.
        if(ready)progress(s,os_gettime_ns());
        result=0;
    }catch(const std::exception &e){std::string code=e.what();if(code.empty()||code.size()>64||code.find_first_not_of("abcdefghijklmnopqrstuvwxyz_")!=std::string::npos)code="native_failure";error(code.c_str());}
    s.commandStop=true;if(audioThread.joinable())audioThread.join();
    if(output){if(obs_output_active(output))obs_output_force_stop(output);obs_output_release(output);}
    if(system)obs_source_remove_audio_capture_callback(system,sample,nullptr);
    if(mic)obs_source_remove_audio_capture_callback(mic,sample,reinterpret_cast<void *>(1));
    if(screen)signal_handler_disconnect(obs_source_get_signal_handler(screen),"update_properties",sourcePropertiesChanged,&s);
    for(int channel=0;channel<3;++channel)if(initialized)obs_set_output_source(channel,nullptr);
    if(scene)obs_scene_release(scene);if(screen)obs_source_release(screen);if(system)obs_source_release(system);if(mic)obs_source_release(mic);
#ifdef _WIN32
    systemClock.stop();
#endif
    if(videoEncoder)obs_encoder_release(videoEncoder);if(audioEncoder)obs_encoder_release(audioEncoder);
    if(initialized)obs_shutdown();
    if(result==0)event("{\"event\":\"stopped\"}");
    return result;
}
#ifdef _WIN32
int wmain(int argc,wchar_t **arguments) {
    std::vector<std::string> values;std::vector<char *> argv;
    for(int i=0;i<argc;++i) {int size=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,arguments[i],-1,nullptr,0,nullptr,nullptr);if(!size)return 2;std::string value(size,'\0');WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,arguments[i],-1,value.data(),size,nullptr,nullptr);value.pop_back();values.push_back(std::move(value));}
    for(auto &value:values)argv.push_back(value.data());
    if(argc==3) {auto path=fs::u8path(values[2])/"bin"/"64bit";SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);AddDllDirectory(path.c_str());}
    return run(argc,argv.data());
}
#else
int main(int argc,char **argv) {return run(argc,argv);}
#endif
