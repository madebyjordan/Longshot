/* Hookshot-Installer.exe: the Hookshot launcher (launcher/Hookshot) installed for this Windows user, no administrator needed.
   The launcher's portable zip rides at the end of this program (scripts/launcher/publish.ps1 appends it, then 16 bytes: its size,
   little-endian, and "HSZIP001"). Installing:
     - the zip unpacked with Windows' own tar.exe beside the install folder, then swapped in whole: an install stopped half way, or
       one over a launcher that is running, leaves the one before as it was;
     - %LOCALAPPDATA%\HookshotLauncher\app: HookshotLauncher.exe and its files, and Uninstall.exe (this program without the zip).
       That is where a 2.x Builder's hand-over installs the launcher too (docs/launcher-upgrade.md section 2), so there is one
       launcher on the PC whichever way it came;
     - Hookshot in the Start menu and on the desktop;
     - Hookshot in Settings > Apps (HKCU\...\Uninstall\Hookshot), which runs Uninstall.exe;
     - then Hookshot started.
   Uninstalling takes the launcher, its shortcuts and that entry away. The games, their saves and the builders it downloaded are
   the player's and stay, unless the player asks for the builders to go too.

   Hookshot-Installer.exe [/S]          install (/S: no questions, nothing started after)
   Uninstall.exe /uninstall [/S]        uninstall
   For checks without touching this PC's own (tests/test_launcher_installer.py): HOOKSHOT_LAUNCHER_HOME (the launcher's home, as
   the hand-over has it: the install folder is its app), HOOKSHOT_INSTALL_DIR (the install folder itself),
   HOOKSHOT_INSTALL_LINKS (a folder for the two shortcuts), HOOKSHOT_INSTALL_KEY (the Apps entry's key name),
   HOOKSHOT_SETUP_BASE (the builders' folder, as the setup has it), HOOKSHOT_INSTALL_LOG (what it would say, to that file).

   Build: windres installer.rc -O coff -o installer_rc.o
          clang -O2 -mwindows -municode installer.c installer_rc.o -lole32 -luuid -lshell32 -ladvapi32 -Wl,--dependent-load-flag=0x800
                -o Hookshot-Installer.exe   (-DHOOKSHOT_VERSION=\"v3.0.0\" names the version in Settings > Apps) */
#define WIN32_LEAN_AND_MEAN
#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#define COBJMACROS
#include <windows.h>
#include <objbase.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

#ifndef HOOKSHOT_VERSION
#define HOOKSHOT_VERSION "dev"
#endif
#define BIG 2048

static const char MAGIC[8] = {'H', 'S', 'Z', 'I', 'P', '0', '0', '1'};
static int g_quiet;

/* (what it says: a message box, or with /S nothing; HOOKSHOT_INSTALL_LOG gets it either way) */
static int say(UINT kind, const wchar_t *fmt, ...) {
    wchar_t text[4096];
    va_list ap;
    va_start(ap, fmt);
    _vsnwprintf(text, 4096, fmt, ap);
    va_end(ap);
    text[4095] = 0;
    const wchar_t *log = _wgetenv(L"HOOKSHOT_INSTALL_LOG");
    if (log) {
        FILE *f = _wfopen(log, L"ab");
        if (f) {
            char utf8[16384];
            WideCharToMultiByte(CP_UTF8, 0, text, -1, utf8, sizeof utf8, NULL, NULL);
            fprintf(f, "%s\n", utf8), fclose(f);
        }
    }
    if (g_quiet) return (kind & MB_YESNO) ? IDYES : IDOK;
    return MessageBoxW(NULL, text, L"Hookshot", kind | MB_SETFOREGROUND);
}

static int exists(const wchar_t *p) { return GetFileAttributesW(p) != INVALID_FILE_ATTRIBUTES; }

static void rmtree(const wchar_t *dir) {
    wchar_t pat[BIG], p[BIG];
    WIN32_FIND_DATAW fd;
    swprintf(pat, BIG, L"%ls\\*", dir);
    HANDLE h = FindFirstFileW(pat, &fd);
    if (h != INVALID_HANDLE_VALUE) {
        do {
            if (!wcscmp(fd.cFileName, L".") || !wcscmp(fd.cFileName, L"..")) continue;
            swprintf(p, BIG, L"%ls\\%ls", dir, fd.cFileName);
            /* (a link to a folder is removed, never followed) */
            if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && !(fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)) rmtree(p);
            else {
                SetFileAttributesW(p, FILE_ATTRIBUTE_NORMAL);
                if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) RemoveDirectoryW(p);
                else DeleteFileW(p);
            }
        } while (FindNextFileW(h, &fd));
        FindClose(h);
    }
    RemoveDirectoryW(dir);
}

static void env_or(wchar_t *out, const wchar_t *name, int csidl, const wchar_t *under) {
    const wchar_t *v = _wgetenv(name);
    if (v && *v) {
        wcsncpy(out, v, BIG - 1), out[BIG - 1] = 0;
        return;
    }
    wchar_t base[MAX_PATH] = L"";
    SHGetFolderPathW(NULL, csidl, NULL, 0, base);
    swprintf(out, BIG, under ? L"%ls\\%ls" : L"%ls", base, under);
}

/* the install folder, the two shortcuts, the Apps entry's key, the builders' folder */
static wchar_t g_dir[BIG], g_start_link[BIG], g_desk_link[BIG], g_key[BIG], g_base[BIG];

static void places(void) {
    const wchar_t *dir = _wgetenv(L"HOOKSHOT_INSTALL_DIR");
    if (dir && *dir) wcsncpy(g_dir, dir, BIG - 1), g_dir[BIG - 1] = 0;
    else {
        wchar_t home[BIG];
        env_or(home, L"HOOKSHOT_LAUNCHER_HOME", CSIDL_LOCAL_APPDATA, L"HookshotLauncher");
        swprintf(g_dir, BIG, L"%ls\\app", home);
    }
    const wchar_t *links = _wgetenv(L"HOOKSHOT_INSTALL_LINKS");
    if (links && *links) {
        swprintf(g_start_link, BIG, L"%ls\\Start menu\\Hookshot.lnk", links);
        swprintf(g_desk_link, BIG, L"%ls\\Desktop\\Hookshot.lnk", links);
    } else {
        wchar_t p[MAX_PATH] = L"";
        SHGetFolderPathW(NULL, CSIDL_PROGRAMS, NULL, 0, p);
        swprintf(g_start_link, BIG, L"%ls\\Hookshot.lnk", p);
        SHGetFolderPathW(NULL, CSIDL_DESKTOPDIRECTORY, NULL, 0, p);
        swprintf(g_desk_link, BIG, L"%ls\\Hookshot.lnk", p);
    }
    const wchar_t *key = _wgetenv(L"HOOKSHOT_INSTALL_KEY");
    swprintf(g_key, BIG, L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\%ls", key && *key ? key : L"Hookshot");
    env_or(g_base, L"HOOKSHOT_SETUP_BASE", CSIDL_LOCAL_APPDATA, L"HookshotBuilder");
}

static int run_hidden(wchar_t *cmd, DWORD *code) {
    STARTUPINFOW si = {sizeof si};
    PROCESS_INFORMATION pi;
    if (!CreateProcessW(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) return 0;
    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, code);
    CloseHandle(pi.hThread), CloseHandle(pi.hProcess);
    return 1;
}

/* this program's own file: its length, and where the zip at its end starts and how long it is (0: none) */
static int payload(const wchar_t *self, ULONGLONG *stub, ULONGLONG *zip) {
    HANDLE f = CreateFileW(self, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return 0;
    LARGE_INTEGER size, at;
    unsigned char tail[16];
    DWORD got = 0;
    int ok = GetFileSizeEx(f, &size) && size.QuadPart > 16;
    if (ok) {
        at.QuadPart = size.QuadPart - 16;
        ok = SetFilePointerEx(f, at, NULL, FILE_BEGIN) && ReadFile(f, tail, 16, &got, NULL) && got == 16;
    }
    CloseHandle(f);
    if (!ok) return 0;
    ULONGLONG n = 0;
    for (int i = 7; i >= 0; i--) n = n << 8 | tail[i];
    if (memcmp(tail + 8, MAGIC, 8) || n == 0 || n > (ULONGLONG)size.QuadPart - 16) {
        *stub = (ULONGLONG)size.QuadPart, *zip = 0;
        return 1;
    }
    *zip = n, *stub = (ULONGLONG)size.QuadPart - 16 - n;
    return 1;
}

/* bytes [from, from + n) of one file copied to another */
static int copy_part(const wchar_t *src, ULONGLONG from, ULONGLONG n, const wchar_t *dst) {
    HANDLE in = CreateFileW(src, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
    if (in == INVALID_HANDLE_VALUE) return 0;
    HANDLE out = CreateFileW(dst, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL);
    if (out == INVALID_HANDLE_VALUE) return CloseHandle(in), 0;
    LARGE_INTEGER at;
    at.QuadPart = (LONGLONG)from;
    int ok = SetFilePointerEx(in, at, NULL, FILE_BEGIN);
    static char buf[1 << 20];
    while (ok && n) {
        DWORD want = n < sizeof buf ? (DWORD)n : sizeof buf, got = 0, put = 0;
        ok = ReadFile(in, buf, want, &got, NULL) && got == want && WriteFile(out, buf, got, &put, NULL) && put == got;
        n -= got;
    }
    CloseHandle(in), CloseHandle(out);
    if (!ok) DeleteFileW(dst);
    return ok;
}

static int make_link(const wchar_t *link, const wchar_t *target, const wchar_t *dir) {
    wchar_t parent[BIG];
    wcscpy(parent, link);
    *wcsrchr(parent, L'\\') = 0;
    SHCreateDirectoryExW(NULL, parent, NULL);
    IShellLinkW *sl = NULL;
    if (FAILED(CoCreateInstance(&CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, &IID_IShellLinkW, (void **)&sl))) return 0;
    IShellLinkW_SetPath(sl, target);
    IShellLinkW_SetWorkingDirectory(sl, dir);
    IShellLinkW_SetDescription(sl, L"Hookshot: build, update and play");
    IShellLinkW_SetIconLocation(sl, target, 0);
    IPersistFile *pf = NULL;
    int ok = SUCCEEDED(IShellLinkW_QueryInterface(sl, &IID_IPersistFile, (void **)&pf)) && SUCCEEDED(IPersistFile_Save(pf, link, TRUE));
    if (pf) IPersistFile_Release(pf);
    IShellLinkW_Release(sl);
    return ok;
}

static void set_text(HKEY k, const wchar_t *name, const wchar_t *value) {
    RegSetValueExW(k, name, 0, REG_SZ, (const BYTE *)value, (DWORD)((wcslen(value) + 1) * sizeof(wchar_t)));
}

static void set_number(HKEY k, const wchar_t *name, DWORD value) { RegSetValueExW(k, name, 0, REG_DWORD, (const BYTE *)&value, sizeof value); }

static ULONGLONG folder_bytes(const wchar_t *dir) {
    wchar_t pat[BIG], p[BIG];
    WIN32_FIND_DATAW fd;
    ULONGLONG n = 0;
    swprintf(pat, BIG, L"%ls\\*", dir);
    HANDLE h = FindFirstFileW(pat, &fd);
    if (h == INVALID_HANDLE_VALUE) return 0;
    do {
        if (!wcscmp(fd.cFileName, L".") || !wcscmp(fd.cFileName, L"..")) continue;
        swprintf(p, BIG, L"%ls\\%ls", dir, fd.cFileName);
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)) n += folder_bytes(p);
        } else n += (ULONGLONG)fd.nFileSizeHigh << 32 | fd.nFileSizeLow;
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return n;
}

/* a folder renamed, tried for a few seconds: a program running from it (Hookshot, or the Uninstall.exe that started this) holds
   it until it exits */
static int rename_waiting(const wchar_t *from, const wchar_t *to, int tries) {
    for (int i = 0; i < tries; i++) {
        if (MoveFileExW(from, to, 0)) return 1;
        Sleep(250);
    }
    return 0;
}

static int install(const wchar_t *self) {
    ULONGLONG stub = 0, zip = 0;
    if (!payload(self, &stub, &zip) || !zip) {
        say(MB_ICONERROR, L"This installer is incomplete: the Hookshot launcher it carries is missing. Download it again.");
        return 1;
    }
    wchar_t exe[BIG];
    swprintf(exe, BIG, L"%ls\\HookshotLauncher.exe", g_dir);
    const int over = exists(exe);
    if (say(MB_YESNO | MB_ICONQUESTION,
            over ? L"Install Hookshot %hs over the one already in\n%ls?\n\nYour games, saves and builders stay as they are."
                 : L"Install Hookshot %hs?\n\nIt is installed for you only, in\n%ls\nwith Hookshot in the Start menu and on the desktop.\n\n"
                   L"Your games and saves are kept apart from it.",
            HOOKSHOT_VERSION, g_dir) != IDYES)
        return 2;

    /* the zip out of this program, unpacked beside the install folder (the same drive, so the swap is a rename) */
    wchar_t staging[BIG], old[BIG], zipfile[BIG], unpacked[BIG], launcher[BIG], cmd[BIG * 3], tar[MAX_PATH];
    swprintf(staging, BIG, L"%ls.new", g_dir);
    swprintf(old, BIG, L"%ls.old", g_dir);
    swprintf(zipfile, BIG, L"%ls\\launcher.zip", staging);
    /* (the zip's root holds HookshotLauncher.exe and the rest of the app, as a release's hookshot-launcher.txt names it) */
    swprintf(unpacked, BIG, L"%ls\\app", staging);
    swprintf(launcher, BIG, L"%ls\\HookshotLauncher.exe", unpacked);
    rmtree(staging), rmtree(old);
    if (SHCreateDirectoryExW(NULL, unpacked, NULL) != ERROR_SUCCESS) {
        say(MB_ICONERROR, L"Hookshot could not be installed: the folder %ls could not be made.", staging);
        return 1;
    }
    GetSystemDirectoryW(tar, MAX_PATH);
    wcscat(tar, L"\\tar.exe");
    DWORD code = 1;
    swprintf(cmd, BIG * 3, L"\"%ls\" -xf \"%ls\" -C \"%ls\"", tar, zipfile, unpacked);
    if (!copy_part(self, stub, zip, zipfile) || !run_hidden(cmd, &code) || code != 0 || !exists(launcher)) {
        rmtree(staging);
        say(MB_ICONERROR, L"Hookshot could not be installed: its files could not be unpacked (tar.exe: %lu). Is the drive full?", code);
        return 1;
    }
    DeleteFileW(zipfile);

    /* Uninstall.exe: this program without the zip */
    wchar_t uninstall[BIG];
    swprintf(uninstall, BIG, L"%ls\\Uninstall.exe", unpacked);
    if (!copy_part(self, 0, stub, uninstall)) {
        rmtree(staging);
        say(MB_ICONERROR, L"Hookshot could not be installed: its uninstaller could not be written.");
        return 1;
    }

    /* the swap: the folder before it stays whole until the new one is in its place */
    for (;;) {
        if (!exists(g_dir) || rename_waiting(g_dir, old, 8)) break;
        if (say(MB_RETRYCANCEL | MB_ICONWARNING, L"Hookshot is running from %ls. Close it, then choose Retry.", g_dir) != IDRETRY || g_quiet) {
            rmtree(staging);
            return 3;
        }
    }
    wchar_t parent[BIG];
    wcscpy(parent, g_dir);
    *wcsrchr(parent, L'\\') = 0;
    SHCreateDirectoryExW(NULL, parent, NULL);
    if (!MoveFileExW(unpacked, g_dir, 0)) {
        if (exists(old)) MoveFileExW(old, g_dir, 0);
        rmtree(staging);
        say(MB_ICONERROR, L"Hookshot could not be installed in %ls (error %lu). The one before is as it was.", g_dir, GetLastError());
        return 1;
    }
    rmtree(old), rmtree(staging);

    /* the shortcuts and Settings > Apps */
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    const int links = make_link(g_start_link, exe, g_dir) & make_link(g_desk_link, exe, g_dir);
    CoUninitialize();
    HKEY k;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, g_key, 0, NULL, 0, KEY_WRITE, NULL, &k, NULL) == ERROR_SUCCESS) {
        wchar_t text[BIG + 32], version[64];
        MultiByteToWideChar(CP_UTF8, 0, HOOKSHOT_VERSION, -1, version, 64);
        set_text(k, L"DisplayName", L"Hookshot");
        set_text(k, L"DisplayVersion", version[0] == L'v' ? version + 1 : version);
        set_text(k, L"Publisher", L"Hookshot");
        set_text(k, L"DisplayIcon", exe);
        set_text(k, L"InstallLocation", g_dir);
        set_text(k, L"URLInfoAbout", L"https://github.com/NotRexed/Hookshot");
        swprintf(text, BIG + 32, L"\"%ls\\Uninstall.exe\" /uninstall", g_dir);
        set_text(k, L"UninstallString", text);
        swprintf(text, BIG + 32, L"\"%ls\\Uninstall.exe\" /uninstall /S", g_dir);
        set_text(k, L"QuietUninstallString", text);
        set_number(k, L"NoModify", 1);
        set_number(k, L"NoRepair", 1);
        set_number(k, L"EstimatedSize", (DWORD)(folder_bytes(g_dir) / 1024));
        RegCloseKey(k);
    }
    if (!links) say(MB_ICONWARNING, L"Hookshot is installed in %ls, but its shortcuts could not be made.", g_dir);
    say(0, L"installed %ls", g_dir);

    if (!g_quiet) {
        STARTUPINFOW si = {sizeof si};
        PROCESS_INFORMATION pi;
        swprintf(cmd, BIG * 3, L"\"%ls\"", exe);
        if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, g_dir, &si, &pi)) CloseHandle(pi.hThread), CloseHandle(pi.hProcess);
    }
    return 0;
}

/* (launcher.txt names where the launcher last ran: the game folders' starters open it, builder/setup/setup.c. One that names the
   folder uninstalled goes, so they open the installed builder again) */
static void forget_launcher(void) {
    wchar_t record[BIG], wpath[BIG], prefix[BIG + 2];
    char path[BIG * 3] = "";
    swprintf(record, BIG, L"%ls\\launcher.txt", g_base);
    FILE *f = _wfopen(record, L"rb");
    if (!f) return;
    if (!fgets(path, sizeof path, f)) path[0] = 0;
    fclose(f);
    path[strcspn(path, "\r\n")] = 0;
    MultiByteToWideChar(CP_UTF8, 0, path, -1, wpath, BIG);
    swprintf(prefix, BIG + 2, L"%ls\\", g_dir);
    if (!_wcsnicmp(wpath, prefix, wcslen(prefix))) DeleteFileW(record);
}

static int uninstall(const wchar_t *self) {
    /* (run from the install folder, as Settings > Apps runs it: a copy in %TEMP% does it, so that the folder can go whole) */
    wchar_t prefix[BIG + 2], cmd[BIG * 3];
    swprintf(prefix, BIG + 2, L"%ls\\", g_dir);
    if (!_wcsnicmp(self, prefix, wcslen(prefix))) {
        wchar_t tmp[MAX_PATH], copy[BIG];
        GetTempPathW(MAX_PATH, tmp);
        swprintf(copy, BIG, L"%lsHookshot-Uninstall-%lu.exe", tmp, GetCurrentProcessId());
        if (CopyFileW(self, copy, FALSE)) {
            STARTUPINFOW si = {sizeof si};
            PROCESS_INFORMATION pi;
            swprintf(cmd, BIG * 3, L"\"%ls\" /uninstall /from-temp%ls", copy, g_quiet ? L" /S" : L"");
            if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, tmp, &si, &pi)) {
                CloseHandle(pi.hThread), CloseHandle(pi.hProcess);
                return 0;
            }
        }
    }
    if (say(MB_YESNO | MB_ICONQUESTION, L"Uninstall Hookshot?\n\nYour games and their saves stay where they are.") != IDYES) return 2;
    wchar_t gone[BIG];
    swprintf(gone, BIG, L"%ls.gone", g_dir);
    rmtree(gone);
    for (;;) {
        if (!exists(g_dir) || rename_waiting(g_dir, gone, 20)) break;
        if (say(MB_RETRYCANCEL | MB_ICONWARNING, L"Hookshot is running. Close it, then choose Retry.") != IDRETRY || g_quiet) return 3;
    }
    rmtree(gone);
    DeleteFileW(g_start_link), DeleteFileW(g_desk_link);
    RegDeleteKeyW(HKEY_CURRENT_USER, g_key);
    forget_launcher();
    /* (the builders, the build files and the settings: asked, kept by default. Another builder may still use them) */
    if (exists(g_base)) {
        const ULONGLONG bytes = folder_bytes(g_base);
        if (!g_quiet && say(MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2,
                            L"Hookshot is uninstalled.\n\nAlso remove the builders and build files it downloaded (%.1f GB, in %ls)? "
                            L"Your games and saves stay either way. A game folder keeps working; updating it would download its builder again.",
                            bytes / 1073741824.0, g_base) == IDYES)
            rmtree(g_base);
    } else if (!g_quiet) say(MB_ICONINFORMATION, L"Hookshot is uninstalled. Your games and their saves stay where they are.");
    say(0, L"uninstalled %ls", g_dir);
    return 0;
}

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, LPWSTR cmdline, int show) {
    (void)inst, (void)prev, (void)cmdline, (void)show;
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    int argc = 0;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    int remove = 0, from_temp = 0;
    for (int i = 1; argv && i < argc; i++) {
        if (!_wcsicmp(argv[i], L"/S") || !_wcsicmp(argv[i], L"--quiet")) g_quiet = 1;
        else if (!_wcsicmp(argv[i], L"/uninstall") || !_wcsicmp(argv[i], L"--uninstall")) remove = 1;
        else if (!_wcsicmp(argv[i], L"/from-temp")) from_temp = 1;
    }
    places();
    wchar_t self[BIG];
    GetModuleFileNameW(NULL, self, BIG);
    const int code = remove ? uninstall(self) : install(self);
    if (from_temp) {   /* (the copy in %TEMP% removes itself once it has exited) */
        wchar_t cmd[BIG * 2], comspec[MAX_PATH];
        GetSystemDirectoryW(comspec, MAX_PATH);
        swprintf(cmd, BIG * 2, L"\"%ls\\cmd.exe\" /c ping -n 3 127.0.0.1 >nul & del /f /q \"%ls\"", comspec, self);
        STARTUPINFOW si = {sizeof si};
        PROCESS_INFORMATION pi;
        if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) CloseHandle(pi.hThread), CloseHandle(pi.hProcess);
    }
    return code;
}
