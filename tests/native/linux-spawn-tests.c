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
        if (!strcmp(argv[1], "multi")) {
            char a, b, c;
            if (read(0, &a, 1) != 1 || read(3, &b, 1) != 1 || read(4, &c, 1) != 1 || a != 'A' || b != 'B' || c != 'C') return 22;
            return 0;
        }
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
    assert(strstr(result, "mapped-source-ok")); close(output[0]); close(error[0]);
    // Force input mapping cycle 4 -> 3 and 3 -> 4; naive sequential dup2 corrupts C.
    int saved[3];
    for (int i = 0; i < 3; ++i) { int p[2]; assert(!pipe(p)); char value = 'A' + i;
        assert(write(p[1], &value, 1) == 1); close(p[1]); saved[i] = fcntl(p[0], F_DUPFD_CLOEXEC, 100); close(p[0]); assert(saved[i] >= 100); }
    assert(dup2(saved[0], 5) == 5 && dup2(saved[1], 4) == 4 && dup2(saved[2], 3) == 3);
    int multiple[] = {5, 4, 3}; char *multi_args[] = {argv[0], "multi", NULL};
    assert(!opencam_spawn(argv[0], multi_args, multiple, 3, 1, 2, &pid));
    for (int i = 0; i < 3; ++i) { close(saved[i]); close(3+i); }
    assert(waitpid(pid, &status, 0) == pid && status == 0); return 0;
}
