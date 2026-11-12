/* The stand-in game of scripts/launcher/dummy.ps1: what Play starts in a game folder of made-up releases. It says so, with the
   folder and the version it was "built" as, and exits. No game is run or read.
   Build: clang -O1 -mwindows -municode dummygame.c -o hookshot.exe */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

int WINAPI wWinMain(HINSTANCE inst, HINSTANCE prev, LPWSTR cmdline, int show) {
    (void)inst, (void)prev, (void)cmdline, (void)show;
    wchar_t dir[MAX_PATH] = L"", stamp[MAX_PATH], version[128] = L"(no version)", text[1024];
    GetCurrentDirectoryW(MAX_PATH, dir);
    swprintf(stamp, MAX_PATH, L"%ls\\hookshot_version.txt", dir);
    FILE *f = _wfopen(stamp, L"r, ccs=UTF-8");
    if (f) {
        wchar_t line[256];
        while (fgetws(line, 256, f))
            if (!wcsncmp(line, L"version=", 8)) {
                wcsncpy(version, line + 8, 127);
                version[wcscspn(version, L"\r\n")] = 0;
                break;
            }
        fclose(f);
    }
    swprintf(text, 1024, L"Play works.\n\nThis is the stand-in game of the made-up releases (scripts/launcher/dummy.ps1), "
             L"\"built\" as %ls in\n%ls\n\nNo game was started.", version, dir);
    MessageBoxW(NULL, text, L"Hookshot (stand-in game)", MB_OK | MB_ICONINFORMATION);
    return 0;
}
