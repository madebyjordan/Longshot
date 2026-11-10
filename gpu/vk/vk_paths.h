// The game folder's path for the files the game keeps there (caches, the shader list, settings): host/hos_paths.h's rule, for
// this DLL. The game is bin\hookshot.exe beside its launcher since the launcher came; its files stay in the folder above bin.
// game_module_path answers as GetModuleFileNameA(nullptr, ...) does, with the folder's path when the exe is in a "bin" folder
// beside image.bin.
#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#undef min
#undef max
#include <cstdlib>
#include <cstring>
#include <string>

namespace mkwvk {
inline DWORD game_module_path(char* buf, DWORD size) {
    DWORD n = GetModuleFileNameA(nullptr, buf, size);
    if (!n || n >= size) return n;
    char* name = buf + n;
    while (name > buf && name[-1] != '\\' && name[-1] != '/') name--;
    if (name - buf < 5) return n;
    char* dir = name - 1;
    char* bin = dir;
    while (bin > buf && bin[-1] != '\\' && bin[-1] != '/') bin--;
    if (dir - bin != 3 || _strnicmp(bin, "bin", 3) != 0) return n;
    char probe[MAX_PATH + 16];
    const size_t root = (size_t)(bin - buf);
    if (root + 10 >= sizeof probe) return n;
    std::memcpy(probe, buf, root);
    std::strcpy(probe + root, "image.bin");
    if (GetFileAttributesA(probe) == INVALID_FILE_ATTRIBUTES) return n;
    std::memmove(buf + root, name, std::strlen(name) + 1);
    return (DWORD)std::strlen(buf);
}

// (test runs) The caches the renderer writes (hookshot_programs.bin, hookshot_pipelines.bin, hookshot_prepared.bin,
// mkw_vk_pipelines.bin and their .running/.warming markers) are kept in the game folder. RX_VK_CACHE_DIR=<folder> keeps them
// in that folder instead, so a run can start cold, or from a cache of its own, without copying the game folder or moving its
// caches aside. The folder must exist. The shader list (hookshot_shaders.bin) and the settings stay in the game folder
inline std::string cache_path(const char* name) {
    if (const char* d = getenv("RX_VK_CACHE_DIR"); d && *d) {
        std::string dir(d);
        if (dir.back() != '\\' && dir.back() != '/') dir += '/';   // (Windows takes / too; Linux only it)
        return dir + name;
    }
    char exe[MAX_PATH];
    const DWORD n = game_module_path(exe, MAX_PATH);
    std::string dir(exe, n);
    return dir.substr(0, dir.find_last_of("\\/") + 1) + name;
}
// RX_VK_CACHE_READONLY=1: the caches are read and never written, cut short or deleted. A test build then cannot spoil the
// game's 6.7 GB program cache (a build with another translator stamp starts it again, "wb"): what it makes is kept in
// memory for the run and dropped. A cache of another stamp is not used at all
inline bool cache_readonly() {
    static const bool ro = [] {
        const char* e = getenv("RX_VK_CACHE_READONLY");
        return e && *e && *e != '0';
    }();
    return ro;
}
}  // namespace mkwvk
