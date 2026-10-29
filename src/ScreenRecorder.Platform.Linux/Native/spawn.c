// SPDX-License-Identifier: AGPL-3.0-or-later
#define _GNU_SOURCE
#include <spawn.h>
#include <fcntl.h>
#include <unistd.h>
#include <errno.h>
#include <stdlib.h>
extern char **environ;

// Keep glibc opaque structures in C. Never share Darwin layouts/constants with .NET.
int opencam_spawn(const char *path, char *const argv[], const int *inputs, int count, int output, int error, int *pid) {
    if (!path || path[0] != '/' || !argv || !inputs || count < 1 || count > 129 || !pid) return EINVAL;
    posix_spawn_file_actions_t actions; posix_spawnattr_t attributes;
    int result = posix_spawn_file_actions_init(&actions); if (result) return result;
    result = posix_spawnattr_init(&attributes); if (result) { posix_spawn_file_actions_destroy(&actions); return result; }
    int *copies = malloc((count + 2) * sizeof(int));
    if (!copies) { result = ENOMEM; goto cleanup; }
    for (int i = 0; i < count + 2; i++) copies[i] = -1;
    // All mappings finish below count+2. Relocate every source first to avoid
    // clobbering an input whose original FD equals another mapping's target.
    for (int i = 0; i < count + 2; i++) {
        int source = i < count ? inputs[i] : i == count ? output : error;
        copies[i] = fcntl(source, F_DUPFD_CLOEXEC, count + 3);
        if (copies[i] < 0) { result = errno; goto close_copies; }
    }
    for (int i = 0; i < count + 2; i++) {
        int target = i == 0 ? 0 : i < count ? i + 2 : i == count ? 1 : 2;
        result = posix_spawn_file_actions_adddup2(&actions, copies[i], target); if (result) goto close_copies;
    }
    result = posix_spawn_file_actions_addclosefrom_np(&actions, count + 2); if (result) goto close_copies;
    result = posix_spawnattr_setpgroup(&attributes, 0); if (result) goto close_copies;
    result = posix_spawnattr_setflags(&attributes, POSIX_SPAWN_SETPGROUP); if (result) goto close_copies;
    result = posix_spawn(pid, path, &actions, &attributes, argv, environ);
close_copies:
    for (int i = 0; i < count + 2; i++) if (copies[i] >= 0) close(copies[i]);
    free(copies);
cleanup:
    posix_spawnattr_destroy(&attributes); posix_spawn_file_actions_destroy(&actions); return result;
}
