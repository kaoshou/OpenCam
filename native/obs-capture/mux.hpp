// SPDX-License-Identifier: AGPL-3.0-or-later
#pragma once
#include <obs.h>
extern "C" {
#include <libavformat/avformat.h>
#include <libavutil/channel_layout.h>
}
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <stdexcept>

// The parent owns the file. Only libavformat serializes media; no hand-written
// EBML, independent timestamp resets, unbounded packet queue or file reopening.
class Mux {
    AVFormatContext *context = nullptr;
    AVIOContext *io = nullptr;
    AVStream *video = nullptr, *audio = nullptr;
    obs_encoder_t *videoEncoder = nullptr, *audioEncoder = nullptr;
    std::mutex mutex;
    bool finished = false;
    bool headerWritten = false;
    static int write(void *opaque, const uint8_t *bytes, int count) {
        auto *self = static_cast<Mux *>(opaque);
        if (std::fwrite(bytes, 1, count, stdout) != static_cast<size_t>(count) || std::fflush(stdout))
            return AVERROR(EIO);
        self->bytes += count;
        return count;
    }
    static void extra(AVCodecParameters *parameters, obs_encoder_t *encoder) {
        uint8_t *data = nullptr; size_t size = 0;
        if (!obs_encoder_get_extra_data(encoder, &data, &size) || !size || size > 1024 * 1024)
            throw std::runtime_error("encoder_header");
        parameters->extradata = static_cast<uint8_t *>(av_mallocz(size + AV_INPUT_BUFFER_PADDING_SIZE));
        if (!parameters->extradata) throw std::bad_alloc();
        std::memcpy(parameters->extradata, data, size);
        parameters->extradata_size = static_cast<int>(size);
    }
public:
    std::atomic<uint64_t> bytes{0}, frames{0};
    std::atomic<int64_t> mediaUs{0};
    std::atomic<bool> failed{false};
    void open(obs_output_t *output, uint32_t width, uint32_t height, uint32_t fps, bool includeAudio) {
        if (avformat_alloc_output_context2(&context, nullptr, "matroska", nullptr) < 0 || !context)
            throw std::runtime_error("mux_context");
        auto *buffer = static_cast<uint8_t *>(av_malloc(32768));
        if (!buffer) throw std::bad_alloc();
        io = avio_alloc_context(buffer, 32768, 1, this, nullptr, write, nullptr);
        if (!io) {av_free(buffer); throw std::bad_alloc();}
        io->seekable = 0; context->pb = io; context->flags |= AVFMT_FLAG_CUSTOM_IO;
        context->max_interleave_delta = 1000000;
        video = avformat_new_stream(context, nullptr);
        if (!video) throw std::bad_alloc();
        video->time_base = {1, static_cast<int>(fps)};
        video->avg_frame_rate = {static_cast<int>(fps), 1};
        auto *v = video->codecpar;
        v->codec_type = AVMEDIA_TYPE_VIDEO; v->codec_id = AV_CODEC_ID_H264;
        v->width = width; v->height = height; v->format = AV_PIX_FMT_YUV420P;
        v->color_range = AVCOL_RANGE_MPEG; v->color_space = AVCOL_SPC_BT709;
        v->color_primaries = AVCOL_PRI_BT709; v->color_trc = AVCOL_TRC_BT709;
        videoEncoder = obs_output_get_video_encoder(output);
        if (includeAudio) {
            audio = avformat_new_stream(context, nullptr);
            if (!audio) throw std::bad_alloc();
            audio->time_base = {1, 48000};
            auto *a = audio->codecpar;
            a->codec_type = AVMEDIA_TYPE_AUDIO; a->codec_id = AV_CODEC_ID_AAC;
            a->sample_rate = 48000; av_channel_layout_default(&a->ch_layout, 2);
            audioEncoder = obs_output_get_audio_encoder(output, 0);
        }
    }
    void header() {
        // VideoToolbox (and some hardware drivers) publishes codec extradata
        // only with its first encoded frame, not during encoder initialization.
        extra(video->codecpar, videoEncoder);
        if (audio) extra(audio->codecpar, audioEncoder);
        AVDictionary *options = nullptr;
        av_dict_set(&options, "live", "1", 0);
        av_dict_set(&options, "cluster_time_limit", "1000", 0);
        av_dict_set(&options, "flush_packets", "1", 0);
        int result = avformat_write_header(context, &options); av_dict_free(&options);
        if (result < 0) throw std::runtime_error("mux_header");
        headerWritten = true;
    }
    void packet(const encoder_packet *packet) {
        std::lock_guard<std::mutex> hold(mutex);
        if (finished || failed) return;
        if (!packet || packet->size > 64 * 1024 * 1024 || packet->timebase_den <= 0 || packet->timebase_num <= 0) {
            failed = true; return;
        }
        if (!headerWritten) {
            try { header(); }
            catch (...) { failed = true; return; }
        }
        AVStream *stream = packet->type == OBS_ENCODER_VIDEO ? video : audio;
        if (!stream) {failed = true; return;}
        AVPacket *p = av_packet_alloc();
        if (!p || av_new_packet(p, static_cast<int>(packet->size)) < 0) {av_packet_free(&p); failed = true; return;}
        std::memcpy(p->data, packet->data, packet->size);
        p->pts = packet->pts; p->dts = packet->dts;
        p->duration = packet->type == OBS_ENCODER_VIDEO ? 1 : 1024;
        p->stream_index = stream->index;
        if (packet->keyframe) p->flags |= AV_PKT_FLAG_KEY;
        av_packet_rescale_ts(p, {packet->timebase_num, packet->timebase_den}, stream->time_base);
        int64_t end = av_rescale_q(p->pts + p->duration, stream->time_base, {1, 1000000});
        int result = av_interleaved_write_frame(context, p); av_packet_free(&p);
        if (result < 0 || io->error) {failed = true; return;}
        if (packet->type == OBS_ENCODER_VIDEO) {++frames; mediaUs = end;}
    }
    void finish() {
        std::lock_guard<std::mutex> hold(mutex);
        if (finished) return;
        finished = true;
        if (headerWritten) {
            if (av_write_trailer(context) < 0) failed = true;
            avio_flush(io);
            if (io->error || std::fflush(stdout)) failed = true;
        }
    }
    ~Mux() {
        if (context) {context->pb = nullptr; avformat_free_context(context);}
        if (io) {av_freep(&io->buffer); avio_context_free(&io);}
    }
};
