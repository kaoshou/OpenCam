// SPDX-License-Identifier: AGPL-3.0-or-later
#define _GNU_SOURCE
#include <assert.h>
#include <fcntl.h>
#include <stdio.h>
#include <unistd.h>
#include <sys/wait.h>
#include <string.h>
int opencam_spawn(const char *, char *const[], const int *, int, int, int, int *);
int main(int argc, char **argv) {
    if (argc > 1) {
        // Only explicitly mapped FDs may survive, even when the parent forgot CLOEXEC.
        if (fcntl(200, F_GETFD) != -1) return 20;
        char b[4] = {0}; if (read(0, b, 3) != 3 || strcmp(b, "abc")) return 21;
        puts("mapped-source-ok"); return 0;
    }
    int input[2], output[2], error[2]; assert(!pipe(input)); assert(!pipe(output)); assert(!pipe(error));
    assert(dup2(input[0], 200) == 200); assert(write(input[1], "abc", 3) == 3); close(input[1]);
    int sources[] = {input[0]}; int pid = 0;
    char *args[] = {argv[0], "child", NULL};
    assert(!opencam_spawn(argv[0], args, sources, 1, output[1], error[1], &pid));
    close(output[1]); close(error[1]); close(input[0]); close(200);
    char result[64] = {0}; assert(read(output[0], result, 63) > 0);
    int status; assert(waitpid(pid, &status, 0) == pid); assert(status == 0);
    assert(strstr(result, "mapped-source-ok")); close(output[0]); close(error[0]); return 0;
}
