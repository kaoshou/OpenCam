// SPDX-License-Identifier: AGPL-3.0-or-later
// Linux-only helper. Explicit diagnostic CLI retains OCLINUX; product CLI uses OCOBS.
#include "mux.hpp"
#include "linux-sources.hpp"
#include <obs-nix-platform.h>
#include <util/platform.h>
#include <X11/Xlib.h>
#include <wayland-client.h>
#include <glib.h>
#include <gio/gio.h>
#include <poll.h>
#include <unistd.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <filesystem>
#include <iostream>
#include <mutex>
#include <string>
#include <thread>
#include <csignal>
#include "wayland-displays.hpp"
#include "audio-meter.hpp"
#include "portal-responses.hpp"

namespace fs = std::filesystem;
static std::mutex logMutex;
static std::string backend;
static bool product = false;
static opencam::LinuxSourceHealth sourceHealth;
static void emit(const char *name, const std::string &fields = "")
{
    if (product && std::strcmp(name, "ready") && std::strcmp(name, "progress") &&
        std::strcmp(name, "stopped") && std::strcmp(name, "error")) return;
    std::lock_guard<std::mutex> guard(logMutex);
    std::cerr << (product ? "OCOBS " : "OCLINUX ") << "{\"event\":\"" << name << "\",\"backend\":\"" << backend << "\"" << fields << "}\n";
}
static std::string jsonQuote(const std::string &text)
{
    std::string out = "\"";
    const char hex[] = "0123456789abcdef";
    for (unsigned char c : text) {
        if (c == '"' || c == '\\') { out += '\\'; out += c; }
        else if (c < 32) { out += "\\u00"; out += hex[c >> 4]; out += hex[c & 15]; }
        else out += c;
    }
    return out + '"';
}
static void logger(int level, const char *format, va_list args, void *)
{
    // Pinned OBS 33.0.0 only logs this transport transition; observe its exact
    // fixed format without retaining addresses, errors or permission tokens.
    if (backend == "pipewire" && std::strcmp(format, "[pipewire] Stream %p state: \"%s\" (error: %s)") == 0) {
        va_list copy; va_copy(copy, args);
        (void)va_arg(copy, void *);
        const char *status = va_arg(copy, const char *);
        sourceHealth.streamState(status);
        va_end(copy);
    }
    // Opt-in only for generated desktop/audio fixtures; may include device IDs.
    if (std::getenv("OPENCAM_LINUX_PROBE_DIAGNOSTICS") && level <= LOG_INFO) {
        std::lock_guard<std::mutex> guard(logMutex);
        std::vfprintf(stderr, format, args); std::fputc('\n', stderr);
    }
}
static void pump()
{
    for (int i = 0; i < 32 && g_main_context_iteration(nullptr, false); ++i) {}
    os_sleep_ms(5);
}
static bool command()
{
    pollfd p{STDIN_FILENO, POLLIN, 0};
    if (poll(&p, 1, 0) <= 0) return false;
    char c; return ::read(STDIN_FILENO, &c, 1) != 1 || c == '\n';
}
static std::string requestLine()
{
    std::string result;
    char c;
    while (::read(STDIN_FILENO, &c, 1) == 1) {
        if (c == '\n') return result;
        if (result.size() >= 65535) throw std::runtime_error("invalid_request");
        result += c;
    }
    throw std::runtime_error("invalid_request");
}
struct Request {
    int width = 640, height = 360, fps = 20, quality = 23;
    int protocol = 1, monitorWidth = 640, monitorHeight = 360, cropX = 0, cropY = 0, systemVolume = 100, micVolume = 100;
    bool cursor = true;
    std::string monitor, audio = "none", system, microphone;
};
static Request parse(const std::string &text)
{
    auto *data = obs_data_create_from_json(text.c_str());
    if (!data) throw std::runtime_error("invalid_request");
    Request r;
    auto integer = [&](const char *key) {
        double v = obs_data_get_double(data, key);
        if (!std::isfinite(v) || v != std::floor(v) || v < 0 || v > 16384) throw std::runtime_error("invalid_request");
        return static_cast<int>(v);
    };
    try {
        r.width = integer("width"); r.height = integer("height"); r.fps = integer("fps"); r.quality = integer("quality");
        r.monitor = obs_data_get_string(data, "monitor"); r.audio = obs_data_get_string(data, "audio");
        r.system = obs_data_get_string(data, "systemDevice"); r.microphone = obs_data_get_string(data, "microphone");
        r.protocol = integer("protocol"); r.monitorWidth = integer("monitorWidth"); r.monitorHeight = integer("monitorHeight");
        r.cropX = integer("cropX"); r.cropY = integer("cropY"); r.cursor = obs_data_get_bool(data, "cursor");
        if (r.protocol == 2) { r.systemVolume = integer("systemAudioVolumePercent"); r.micVolume = integer("microphoneVolumePercent"); }
        if ((r.protocol != 1 && (!product || r.protocol != 2)) || obs_data_get_bool(data, "synthetic") ||
            std::string(obs_data_get_string(data, "encoder")) != "cpu" || r.width < 2 || r.height < 2 ||
            r.width % 2 || r.height % 2 || r.fps < 1 || r.fps > 30 ||
            (r.quality != 18 && r.quality != 23 && r.quality != 28) ||
            r.systemVolume > 200 || r.micVolume > 200 ||
            r.monitorWidth < 2 || r.monitorHeight < 2 || r.cropX + r.width > r.monitorWidth || r.cropY + r.height > r.monitorHeight ||
            (!product && (r.cropX || r.cropY || r.monitorWidth != r.width || r.monitorHeight != r.height)) ||
            (r.audio != "none" && r.audio != "system" && r.audio != "microphone" && r.audio != "both" && (!product || r.audio != "silence")))
            throw std::runtime_error("invalid_request");
    } catch (...) { obs_data_release(data); throw; }
    obs_data_release(data); return r;
}
static void module(const fs::path &root, const char *name)
{
    auto binary = root / "lib" / "obs-plugins" / (std::string(name) + ".so");
    auto data = root / "share" / "obs" / "obs-plugins" / name;
    obs_module_t *value = nullptr;
    if (obs_open_module(&value, binary.c_str(), data.c_str()) != MODULE_SUCCESS || !obs_init_module(value))
        throw std::runtime_error("runtime_module");
}
class SourceProperties {
    obs_source_t *instance = nullptr;
public:
    obs_properties_t *properties = nullptr;
    SourceProperties(const char *type, bool requiresInstance) {
        if (requiresInstance) {
            // Pinned XSHM's properties callback dereferences its source data.
            // The class-level API passes nullptr and crashes. Create a source
            // with INVALID_DISPLAY so enumeration cannot capture a default
            // screen before explicit source identity validation.
            auto *settings = obs_data_create();
            obs_data_set_int(settings, "screen", -1);
            instance = obs_source_create_private(type, "enumeration_only", settings);
            obs_data_release(settings);
            if (!instance) throw std::runtime_error("display_enumeration");
            properties = obs_source_properties(instance);
        } else properties = obs_get_source_properties(type);
    }
    SourceProperties(const SourceProperties &) = delete;
    ~SourceProperties() {
        if (properties) obs_properties_destroy(properties);
        if (instance) obs_source_release(instance);
    }
};
static bool exact(const char *type, const char *property, const std::string &id, bool numeric = false)
{
    SourceProperties owned(type, numeric);
    auto *list = owned.properties ? obs_properties_get(owned.properties, property) : nullptr;
    bool found = false;
    for (size_t i = 0; list && i < obs_property_list_item_count(list); ++i) {
        auto value = numeric ? "xshm:" + std::to_string(obs_property_list_item_int(list, i))
                             : std::string(obs_property_list_item_string(list, i));
        if (!obs_property_list_item_disabled(list, i) && value == id && value != "default") found = true;
    }
    return found;
}
struct State {
    Request request; Mux mux; obs_output_t *output = nullptr;
    std::atomic<bool> stopped{false}, failed{false}, systemSamples{false}, micSamples{false};
    opencam::LinuxAudioMeter systemMeter, microphoneMeter;
};
static State *state;
static const char *outputName(void *) { return "OpenCam Linux diagnostic output"; }
static void *outputCreate(obs_data_t *, obs_output_t *output) { state->output = output; return state; }
static void outputDestroy(void *) {}
static bool outputStart(void *opaque)
{
    auto &s = *static_cast<State *>(opaque);
    if (!obs_output_can_begin_data_capture(s.output, 0) || !obs_output_initialize_encoders(s.output, 0)) return false;
    try { s.mux.open(s.output, s.request.width, s.request.height, s.request.fps, s.request.audio != "none"); }
    catch (...) { return false; }
    return obs_output_begin_data_capture(s.output, 0);
}
static void outputStop(void *opaque, uint64_t) { obs_output_end_data_capture(static_cast<State *>(opaque)->output); }
static void outputPacket(void *opaque, encoder_packet *packet) { static_cast<State *>(opaque)->mux.packet(packet); }
static void deactivated(void *opaque, calldata_t *) { static_cast<State *>(opaque)->stopped = true; }
static void outputError(void *opaque, calldata_t *data)
{
    if (calldata_int(data, "code") != OBS_OUTPUT_SUCCESS) static_cast<State *>(opaque)->failed = true;
}
static void audioSample(void *role, obs_source_t *, const audio_data *audio, bool muted)
{
    if (!audio->frames) return;
    sourceHealth.noteSamples(role != nullptr, audio->frames, os_gettime_ns());
    const float *channels[MAX_AV_PLANES]{};
    for (size_t i = 0; i < MAX_AV_PLANES; ++i) channels[i] = reinterpret_cast<const float *>(audio->data[i]);
    (role ? state->microphoneMeter : state->systemMeter).note(channels, MAX_AV_PLANES, audio->frames, muted, os_gettime_ns());
    if (role) state->micSamples = true; else state->systemSamples = true;
}
int main(int argc, char **argv)
{
    std::signal(SIGPIPE, SIG_IGN);
    product = argc == 3;
    if (product && std::string(argv[1]) != "--runtime") return 2;
    if (!product && (argc != 7 || std::string(argv[1]) != "--runtime" || std::string(argv[3]) != "--mode" ||
        std::string(argv[5]) != "--backend")) return 2;
    bool waylandSession = (std::getenv("XDG_SESSION_TYPE") && std::string(std::getenv("XDG_SESSION_TYPE")) == "wayland") || std::getenv("WAYLAND_DISPLAY");
    backend = product ? (waylandSession ? "pipewire" : "x11") : argv[6];
    if (backend != "x11" && backend != "pipewire") return 2;
    const std::string mode = product ? "capture" : argv[4];
    if (mode != "capture" && mode != "enumerate") return 2;
    State s; state = &s;
    try { if (mode == "capture") s.request = parse(requestLine()); }
    catch (...) { emit("error", ",\"code\":\"invalid_request\""); return 2; }
    // OBS's portal source does not expose a verifiable selected output identity.
    // Dimensions alone cannot distinguish two monitors. Keep diagnostic portal
    // research separate; never echo an unverified requested identity as ready.
    if (product && backend == "pipewire") { emit("error", ",\"code\":\"portal_source_unverified\""); return 2; }
    if (backend == "x11" && ((std::getenv("XDG_SESSION_TYPE") && std::string(std::getenv("XDG_SESSION_TYPE")) == "wayland") || std::getenv("WAYLAND_DISPLAY"))) {
        emit("error", ",\"code\":\"wayland_requires_portal\""); return 2;
    }
    auto root = fs::path(argv[2]);
    if (!root.is_absolute()) { emit("error", ",\"code\":\"runtime_path\""); return 2; }
    Display *x11 = nullptr; wl_display *wayland = nullptr;
    GDBusConnection *bus = nullptr; guint sessionSubscription = 0, responseSubscription = 0;
    std::unique_ptr<opencam::PortalResponses> portal;
    bool started = false, ready = false;
    obs_source_t *screen = nullptr, *system = nullptr, *mic = nullptr;
    obs_scene_t *scene = nullptr;
    obs_encoder_t *video = nullptr, *audio = nullptr;
    obs_output_t *output = nullptr;
    int result = 1;
    const char *stage = "core_startup";
    base_set_log_handler(logger, nullptr);
    try {
        if (backend == "x11") x11 = XOpenDisplay(nullptr); else wayland = wl_display_connect(nullptr);
        if ((!x11 && backend == "x11") || (!wayland && backend == "pipewire")) {
            emit("environment_unavailable", ",\"code\":\"desktop_missing\""); return 2;
        }
        obs_set_nix_platform(backend == "x11" ? OBS_NIX_PLATFORM_X11_EGL : OBS_NIX_PLATFORM_WAYLAND);
        obs_set_nix_platform_display(backend == "x11" ? static_cast<void *>(x11) : static_cast<void *>(wayland));
        if (!obs_startup("en-US", nullptr, nullptr)) throw std::runtime_error("core_startup");
        started = true;
        if (obs_get_version() != LIBOBS_API_VER) throw std::runtime_error("core_version");
        sourceHealth.pipewire = backend == "pipewire";
        sourceHealth.systemSelected = s.request.audio == "system" || s.request.audio == "both";
        sourceHealth.microphoneSelected = s.request.audio == "microphone" || s.request.audio == "both";
        if (backend == "pipewire") {
            GError *error = nullptr;
            bus = g_bus_get_sync(G_BUS_TYPE_SESSION, nullptr, &error);
            if (error) { g_error_free(error); error = nullptr; }
            if (!bus) throw std::runtime_error("portal_bus_unavailable");
            auto *properties = g_dbus_connection_call_sync(bus, "org.freedesktop.portal.Desktop",
                "/org/freedesktop/portal/desktop", "org.freedesktop.DBus.Properties", "Get",
                g_variant_new("(ss)", "org.freedesktop.portal.ScreenCast", "version"),
                G_VARIANT_TYPE("(v)"), G_DBUS_CALL_FLAGS_NONE, 5000, nullptr, &error);
            if (error) { g_error_free(error); error = nullptr; }
            if (!properties) throw std::runtime_error("portal_unavailable");
            GVariant *boxed = g_variant_get_child_value(properties, 0);
            GVariant *version = g_variant_get_variant(boxed);
            unsigned portalVersion = g_variant_is_of_type(version, G_VARIANT_TYPE_UINT32) ? g_variant_get_uint32(version) : 0;
            g_variant_unref(version); g_variant_unref(boxed); g_variant_unref(properties);
            if (portalVersion < 4) throw std::runtime_error("portal_identity_unavailable");
            portal = std::make_unique<opencam::PortalResponses>(portalVersion, g_dbus_connection_get_unique_name(bus));
            responseSubscription = g_dbus_connection_signal_subscribe(bus, "org.freedesktop.portal.Desktop",
                "org.freedesktop.portal.Request", "Response", nullptr, nullptr, G_DBUS_SIGNAL_FLAGS_NONE,
                [](GDBusConnection *, const gchar *, const gchar *path, const gchar *, const gchar *, GVariant *parameters, gpointer data) {
                    static_cast<opencam::PortalResponses *>(data)->receive(path, parameters);
                }, portal.get(), nullptr);
            // GLib shares the session-bus connection within this helper. Portal
            // Session::Closed is unicast to its owner; no other application's
            // session or restore token is inspected or closed here.
            sessionSubscription = g_dbus_connection_signal_subscribe(bus, "org.freedesktop.portal.Desktop",
                "org.freedesktop.portal.Session", "Closed", nullptr, nullptr, G_DBUS_SIGNAL_FLAGS_NONE,
                [](GDBusConnection *, const gchar *, const gchar *path, const gchar *, const gchar *, GVariant *, gpointer data) {
                    auto &responses = *static_cast<opencam::PortalResponses *>(data);
                    responses.closed(path);
                    if (responses.failure() == "portal_session_closed") sourceHealth.sessionClosed();
                }, portal.get(), nullptr);
        }
        auto data = (root / "share" / "obs" / "libobs").string() + "/";
        obs_add_data_path(data.c_str());
        auto graphics = (root / "lib" / "libobs-opengl.so").string();
        obs_video_info vi{};
        vi.graphics_module = graphics.c_str(); vi.fps_num = s.request.fps; vi.fps_den = 1;
        vi.base_width = vi.output_width = s.request.width; vi.base_height = vi.output_height = s.request.height;
        vi.output_format = VIDEO_FORMAT_NV12; vi.colorspace = VIDEO_CS_709; vi.range = VIDEO_RANGE_PARTIAL;
        vi.scale_type = OBS_SCALE_BILINEAR; vi.gpu_conversion = true;
        stage = "graphics";
        if (obs_reset_video(&vi) != OBS_VIDEO_SUCCESS) throw std::runtime_error("graphics_startup");
        obs_audio_info ai{48000, SPEAKERS_STEREO};
        if (!obs_reset_audio(&ai)) throw std::runtime_error("audio_startup");
        stage = "modules";
        module(root, backend == "x11" ? "linux-capture" : "linux-pipewire");
        module(root, "linux-pulseaudio"); module(root, "obs-x264"); module(root, "linux-aac");
        obs_post_load_modules();
        if (mode == "enumerate") {
            if (backend == "x11") {
                SourceProperties owned("xshm_input_v2", true);
                auto *list = owned.properties ? obs_properties_get(owned.properties, "screen") : nullptr;
                for (size_t i = 0; list && i < obs_property_list_item_count(list); ++i)
                    if (!obs_property_list_item_disabled(list, i))
                        emit("display", ",\"id\":" + jsonQuote("xshm:" + std::to_string(obs_property_list_item_int(list, i))) +
                            ",\"label\":" + jsonQuote(obs_property_list_item_name(list, i)));
            } else for (const auto &display : opencam::waylandDisplays(wayland))
                emit("display", ",\"id\":" + jsonQuote("portal:" + display.name) + ",\"width\":" + std::to_string(display.width) +
                    ",\"height\":" + std::to_string(display.height) + ",\"x\":" + std::to_string(display.x) + ",\"y\":" + std::to_string(display.y));
            for (const auto *type : {"pulse_output_capture", "pulse_input_capture"}) {
                auto *props = obs_get_source_properties(type);
                auto *list = props ? obs_properties_get(props, "device_id") : nullptr;
                for (size_t i = 0; list && i < obs_property_list_item_count(list); ++i)
                    emit("audio_device", ",\"source\":" + jsonQuote(type) + ",\"id\":" + jsonQuote(obs_property_list_item_string(list, i)));
                if (props) obs_properties_destroy(props);
            }
            result = 0;
        } else {
            stage = "capture_source";
            auto *settings = obs_data_create();
            if (backend == "x11") {
                if (!exact("xshm_input_v2", "screen", s.request.monitor, true)) { obs_data_release(settings); throw std::runtime_error("display_identity"); }
                obs_data_set_int(settings, "screen", std::stoll(s.request.monitor.substr(5)));
                obs_data_set_bool(settings, "show_cursor", s.request.cursor);
                screen = obs_source_create_private("xshm_input_v2", "screen", settings);
            } else {
                obs_data_set_bool(settings, "ShowCursor", s.request.cursor);
                emit("permission_wait");
                screen = obs_source_create_private("pipewire-desktop-capture-source", "screen", settings);
            }
            obs_data_release(settings);
            if (!screen) throw std::runtime_error("capture_source");
            obs_source_set_audio_mixers(screen, 0);
            scene = obs_scene_create_private("capture_scene");
            if (!scene) throw std::runtime_error("capture_source");
            auto *item = obs_scene_add(scene, screen);
            if (!item) throw std::runtime_error("capture_source");
            obs_sceneitem_crop crop{s.request.cropX, s.request.cropY, s.request.monitorWidth - s.request.cropX - s.request.width,
                s.request.monitorHeight - s.request.cropY - s.request.height};
            obs_sceneitem_set_crop(item, &crop);
            obs_set_output_source(0, obs_scene_get_source(scene));
            auto attach = [&](bool selected, bool isMic) -> obs_source_t * {
                if (!selected) return nullptr;
                const auto *type = isMic ? "pulse_input_capture" : "pulse_output_capture";
                const auto &id = isMic ? s.request.microphone : s.request.system;
                if (!exact(type, "device_id", id)) throw std::runtime_error("audio_device_identity");
                auto *a = obs_data_create(); obs_data_set_string(a, "device_id", id.c_str());
                auto *source = obs_source_create_private(type, isMic ? "microphone" : "system", a); obs_data_release(a);
                if (!source) throw std::runtime_error("audio_source");
                obs_source_set_volume(source, (isMic ? s.request.micVolume : s.request.systemVolume) / 100.0f);
                obs_source_add_audio_capture_callback(source, audioSample, isMic ? reinterpret_cast<void *>(1) : nullptr);
                obs_set_output_source(isMic ? 2 : 1, source); return source;
            };
            system = attach(s.request.audio == "system" || s.request.audio == "both", false);
            mic = attach(s.request.audio == "microphone" || s.request.audio == "both", true);
            stage = "source_wait";
            auto deadline = os_gettime_ns() + (backend == "pipewire" ? 60000000000ULL : 10000000000ULL);
            while (obs_source_get_width(screen) != static_cast<uint32_t>(s.request.monitorWidth) ||
                   obs_source_get_height(screen) != static_cast<uint32_t>(s.request.monitorHeight) ||
                   (backend == "pipewire" && !sourceHealth.streaming()) ||
                   (portal && !portal->grant().selected()) ||
                   (system && !s.systemSamples) || (mic && !s.micSamples)) {
                if (portal && !portal->failure().empty()) throw std::runtime_error(portal->failure());
                if (auto *reason = sourceHealth.terminalFailure()) throw std::runtime_error(reason);
                if (command()) throw std::runtime_error("capture_cancelled");
                if (os_gettime_ns() > deadline) throw std::runtime_error("source_timeout");
                pump();
            }
            emit("source_ready", ",\"width\":" + std::to_string(s.request.width) + ",\"height\":" + std::to_string(s.request.height));
            if (system) emit("audio_health", ",\"source\":\"system\",\"state\":\"healthy\"");
            if (mic) emit("audio_health", ",\"source\":\"microphone\",\"state\":\"healthy\"");
            auto *enc = obs_data_create(); obs_data_set_string(enc, "rate_control", "CRF");
            obs_data_set_int(enc, "crf", s.request.quality); obs_data_set_string(enc, "preset", "veryfast");
            obs_data_set_string(enc, "tune", "zerolatency"); obs_data_set_int(enc, "keyint_sec", 2);
            video = obs_video_encoder_create("obs_x264", "video", enc, nullptr); obs_data_release(enc);
            if (!video) throw std::runtime_error("video_encoder");
            obs_encoder_set_video(video, obs_get_video());
            if (s.request.audio != "none") {
                enc = obs_data_create(); obs_data_set_int(enc, "bitrate", 192);
                audio = obs_audio_encoder_create("ffmpeg_aac", "audio", enc, 0, nullptr); obs_data_release(enc);
                if (!audio) throw std::runtime_error("audio_encoder");
                obs_encoder_set_audio(audio, obs_get_audio());
            }
            obs_output_info oi{}; oi.id = "opencam_linux_probe_mkv";
            oi.flags = OBS_OUTPUT_ENCODED | OBS_OUTPUT_VIDEO | (audio ? OBS_OUTPUT_AUDIO : 0);
            oi.get_name = outputName; oi.create = outputCreate; oi.destroy = outputDestroy;
            oi.start = outputStart; oi.stop = outputStop; oi.encoded_packet = outputPacket;
            oi.encoded_video_codecs = "h264"; oi.encoded_audio_codecs = "aac"; obs_register_output(&oi);
            output = obs_output_create(oi.id, "probe", nullptr, nullptr);
            if (!output) throw std::runtime_error("output_create");
            obs_output_set_video_encoder(output, video); if (audio) obs_output_set_audio_encoder(output, audio, 0);
            signal_handler_connect(obs_output_get_signal_handler(output), "deactivate", deactivated, &s);
            signal_handler_connect(obs_output_get_signal_handler(output), "stop", outputError, &s);
            stage = "output_start";
            sourceHealth.arm();
            if (auto *reason = sourceHealth.failure(os_gettime_ns())) throw std::runtime_error(reason);
            if (!obs_output_start(output)) throw std::runtime_error("output_start");
            deadline = os_gettime_ns() + 10000000000ULL;
            while (!s.mux.frames || !s.mux.bytes) {
                if (auto *reason = sourceHealth.failure(os_gettime_ns())) throw std::runtime_error(reason);
                if (s.mux.failed || s.failed || command() || os_gettime_ns() > deadline) throw std::runtime_error("first_frame");
                pump();
            }
            ready = true;
            emit("ready", product ? ",\"protocol\":" + std::to_string(s.request.protocol) + ",\"encoder\":\"cpu\",\"monitor\":" + jsonQuote(s.request.monitor) : "");
            stage = "recording";
            deadline = os_gettime_ns() + 600000000000ULL; // Diagnostic bounded lease, not a production duration limit.
            uint64_t progressAt = 0;
            while (!command()) {
                if (auto *reason = sourceHealth.failure(os_gettime_ns())) throw std::runtime_error(reason);
                if (obs_source_get_width(screen) != static_cast<uint32_t>(s.request.monitorWidth) ||
                    obs_source_get_height(screen) != static_cast<uint32_t>(s.request.monitorHeight))
                    throw std::runtime_error("source_geometry_changed");
                if (s.mux.failed || s.failed || !obs_output_active(output)) throw std::runtime_error("output_failed");
                if (!product && os_gettime_ns() > deadline) throw std::runtime_error("lease_timeout");
                if (product && os_gettime_ns() - progressAt >= 250000000ULL) {
                    progressAt = os_gettime_ns();
                    emit("progress", ",\"frames\":" + std::to_string(s.mux.frames.load()) + ",\"mediaUs\":" + std::to_string(s.mux.mediaUs.load()) +
                        ",\"bytes\":" + std::to_string(s.mux.bytes.load()) + ",\"systemPeak\":" + std::to_string(s.systemMeter.peak()) +
                        ",\"microphonePeak\":" + std::to_string(s.microphoneMeter.peak()) + ",\"systemAgeUs\":" + std::to_string(s.systemMeter.ageUs(progressAt)) +
                        ",\"microphoneAgeUs\":" + std::to_string(s.microphoneMeter.ageUs(progressAt)));
                }
                pump();
            }
            sourceHealth.disarm();
            obs_output_stop(output); deadline = os_gettime_ns() + 5000000000ULL;
            while (!s.stopped && os_gettime_ns() < deadline) pump();
            if (!s.stopped || s.failed || s.mux.failed) throw std::runtime_error("stop_failed");
            s.mux.finish();
            if (s.mux.failed) throw std::runtime_error("mux_finalize");
            result = 0;
        }
    } catch (const std::exception &e) {
        auto code = std::string(e.what());
        if (code.empty() || code.size() > 64 || code.find_first_not_of("abcdefghijklmnopqrstuvwxyz_") != std::string::npos) code = "native_failure";
        emit("error", ",\"code\":" + jsonQuote(code) + ",\"stage\":" + jsonQuote(stage));
    }
    sourceHealth.disarm();
    if (responseSubscription) g_dbus_connection_signal_unsubscribe(bus, responseSubscription);
    if (sessionSubscription) g_dbus_connection_signal_unsubscribe(bus, sessionSubscription);
    if (bus) g_object_unref(bus);
    if (output) { if (obs_output_active(output)) obs_output_force_stop(output); obs_output_release(output); }
    if (system) obs_source_remove_audio_capture_callback(system, audioSample, nullptr);
    if (mic) obs_source_remove_audio_capture_callback(mic, audioSample, reinterpret_cast<void *>(1));
    if (started) for (int i = 0; i < 3; ++i) obs_set_output_source(i, nullptr);
    if (screen) obs_source_release(screen); if (system) obs_source_release(system); if (mic) obs_source_release(mic);
    if (scene) obs_scene_release(scene);
    if (video) obs_encoder_release(video); if (audio) obs_encoder_release(audio);
    if (started) obs_shutdown();
    if (x11) XCloseDisplay(x11); if (wayland) wl_display_disconnect(wayland);
    if (!result && ready) emit("stopped");
    return result;
}
