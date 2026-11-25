// LD_PRELOAD shim: strips O_DIRECT from open calls, because tmpfs rejects O_DIRECT with EINVAL.
// Test use only: SQL Server data on tmpfs is lost when the container stops.
#define _GNU_SOURCE
#include <dlfcn.h>
#include <fcntl.h>
#include <stdarg.h>

#define NEEDS_MODE(flags) (((flags) & O_CREAT) || (((flags) & O_TMPFILE) == O_TMPFILE))

#define WRAP_OPEN(name)                                                     \
    int name(const char *path, int flags, ...) {                           \
        static int (*real)(const char *, int, ...);                        \
        if (!real) real = dlsym(RTLD_NEXT, #name);                         \
        mode_t mode = 0;                                                   \
        if (NEEDS_MODE(flags)) { va_list ap; va_start(ap, flags);           \
            mode = va_arg(ap, mode_t); va_end(ap); }                       \
        return real(path, flags & ~O_DIRECT, mode);                        \
    }

#define WRAP_OPENAT(name)                                                   \
    int name(int dirfd, const char *path, int flags, ...) {                \
        static int (*real)(int, const char *, int, ...);                   \
        if (!real) real = dlsym(RTLD_NEXT, #name);                         \
        mode_t mode = 0;                                                   \
        if (NEEDS_MODE(flags)) { va_list ap; va_start(ap, flags);           \
            mode = va_arg(ap, mode_t); va_end(ap); }                       \
        return real(dirfd, path, flags & ~O_DIRECT, mode);                 \
    }

WRAP_OPEN(open)
WRAP_OPEN(open64)
WRAP_OPENAT(openat)
WRAP_OPENAT(openat64)

int __open_2(const char *path, int flags) {
    static int (*real)(const char *, int);
    if (!real) real = dlsym(RTLD_NEXT, "__open_2");
    return real(path, flags & ~O_DIRECT);
}
int __open64_2(const char *path, int flags) {
    static int (*real)(const char *, int);
    if (!real) real = dlsym(RTLD_NEXT, "__open64_2");
    return real(path, flags & ~O_DIRECT);
}

// fcntl(F_SETFL, O_DIRECT) is another way to turn it on.
int fcntl(int fd, int cmd, ...) {
    static int (*real)(int, int, ...);
    if (!real) real = dlsym(RTLD_NEXT, "fcntl");
    va_list ap; va_start(ap, cmd); void *arg = va_arg(ap, void *); va_end(ap);
    if (cmd == F_SETFL) return real(fd, cmd, (int)(long)arg & ~O_DIRECT);
    return real(fd, cmd, arg);
}
int fcntl64(int fd, int cmd, ...) {
    static int (*real)(int, int, ...);
    if (!real) real = dlsym(RTLD_NEXT, "fcntl64");
    va_list ap; va_start(ap, cmd); void *arg = va_arg(ap, void *); va_end(ap);
    if (cmd == F_SETFL) return real(fd, cmd, (int)(long)arg & ~O_DIRECT);
    return real(fd, cmd, arg);
}
