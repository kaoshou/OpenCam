// SPDX-License-Identifier: AGPL-3.0-or-later
#include <pulse/simple.h>
#include <pulse/error.h>
#include <stdio.h>
#include <stdint.h>
#include <unistd.h>
#include <errno.h>
int main(void) {
    pa_sample_spec format = {PA_SAMPLE_FLOAT32LE, 48000, 2}; int error = 0;
    pa_simple *stream = pa_simple_new(NULL, "OpenCam", PA_STREAM_PLAYBACK, NULL, "Project preview", &format, NULL, NULL, &error);
    if (!stream) { fputs("Audio output initialization failed.\n", stderr); return 1; }
    uint8_t buffer[8192]; uint64_t written = 0, last = 0; ssize_t n;
    // Assemble complete stereo frames even if pipe reads split an 8-byte sample.
    size_t pending = 0;
    while ((n = read(0, buffer + pending, sizeof(buffer) - pending)) != 0) {
        if (n < 0) { if (errno == EINTR) continue; goto fail; }
        size_t total = pending + n, bytes = total - total % 8;
        if (bytes && pa_simple_write(stream, buffer, bytes, &error) < 0) goto fail;
        written += bytes / 8;
        pending = total - bytes;
        for (size_t i = 0; i < pending; i++) buffer[i] = buffer[bytes + i];
        pa_usec_t latency = pa_simple_get_latency(stream, &error); if (latency == (pa_usec_t)-1) goto fail;
        uint64_t queued = latency * 48000 / 1000000;
        uint64_t played = written > queued ? written - queued : 0;
        if (played < last) played = last;
        last = played; printf("{\"sampleClock\":%llu}\n", (unsigned long long)played); fflush(stdout);
    }
    if (pending || pa_simple_drain(stream, &error) < 0) goto fail;
    printf("{\"sampleClock\":%llu}\n", (unsigned long long)written); fflush(stdout);
    pa_simple_free(stream); return 0;
fail:
    fputs("Audio output failed.\n", stderr); pa_simple_free(stream); return 1;
}
