"""The game folder's launcher (builder/setup/setup.c, launch_installed) opens the Hookshot launcher (launcher/Hookshot) where it last
ran from, as it wrote it in launcher.txt in the Builder home (BuilderHome.cs: %LOCALAPPDATA%\\HookshotBuilder, or a portable
one), before any installed builder; one moved or removed since, or a line that is not a whole path to an .exe, is passed over for
the installed builder as before. The actual function and its helpers, cut from setup.c, compiled with stand-ins (the Builder home,
and a CreateProcessW that only says what it would start) over real files in a temporary folder.

python -I -B tests/test_setup_launcher_first.py [--cc <clang or gcc>]
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

RUN_ALL_ARGS = ["--cc", "clang"]
RUN_ALL_SKIP = {"linux": "the setup is Windows' (Win32 file and process calls); exercised by the Windows source checks"}

ROOT = Path(__file__).resolve().parents[1]

FIXTURE = r"""
#define WIN32_LEAN_AND_MEAN
#ifndef UNICODE
#define UNICODE
#endif
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>
#include <stdarg.h>
#include <stdlib.h>
typedef struct { wchar_t root[MAX_PATH]; int portable; } HbHome;
static HbHome g_home;
static int g_home_ok = 1;
static wchar_t g_started[4096], g_dir[MAX_PATH];
static void fail(const wchar_t *fmt, ...) { (void)fmt; exit(90); }
static BOOL fake_start(const wchar_t *cmd, const wchar_t *dir, PROCESS_INFORMATION *pi) {
    wcscpy(g_started, cmd), wcscpy(g_dir, dir);
    pi->hThread = pi->hProcess = NULL;
    return TRUE;
}
#define CreateProcessW(app, cmd, a, b, c, d, e, dir, si, pi) fake_start(cmd, dir, pi)
/* ACTUAL */
int main(void) {
    /* (the folder by the environment, read wide: a C program's argv is in the ANSI code page, and the folder has a ü in it) */
    const wchar_t *base = _wgetenv(L"FIXTURE_BASE");
    if (!base) return 2;
    swprintf(g_home.root, MAX_PATH, L"%ls\\HookshotBuilder", base);
    const int r = launch_installed(L"--launch \"game\"");
    char started[8192], dir[MAX_PATH * 3];
    WideCharToMultiByte(CP_UTF8, 0, g_started, -1, started, sizeof started, NULL, NULL);
    WideCharToMultiByte(CP_UTF8, 0, g_dir, -1, dir, sizeof dir, NULL, NULL);
    printf("%d\n%s\n%s\n", r, started, dir);
    return 0;
}
"""


def cut(source, start, end):
    i = source.index(start)
    return source[i:source.index(end, i)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cc", default=shutil.which("clang") or shutil.which("x86_64-w64-mingw32-clang") or "gcc")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("the setup is Windows'")
    setup = (ROOT / "builder/setup/setup.c").read_text(encoding="utf-8")
    actual = "\n".join([cut(setup, "static int list_name_ok(", "\n}\n") + "\n}\n",
                        cut(setup, "static int home_path(", "\n/* ---- HTTPS GET"),
                        cut(setup, "static void read_small(", "\nstatic void write_small("),
                        cut(setup, "static int builder_ready(", "\n}\n") + "\n}\n",
                        cut(setup, "static int launch_installed(", "\n/* (RecompLauncher's Play)")])
    failures = 0
    with tempfile.TemporaryDirectory(prefix="hookshot launcher first ü ") as t:
        t = Path(t)
        src, exe = t / "fixture.c", t / "fixture.exe"
        src.write_text(FIXTURE.replace("/* ACTUAL */", actual), encoding="utf-8")
        subprocess.run([args.cc, "-std=c11", "-O0", "-w", str(src), "-o", str(exe)], check=True)
        base = t / "appdata"
        home = base / "HookshotBuilder"
        record = home / "launcher.txt"

        def check(name, want_started, want_dir=None):
            nonlocal failures
            out = subprocess.run([str(exe)], capture_output=True, text=True, encoding="utf-8", timeout=15,
                                 env=dict(os.environ, FIXTURE_BASE=str(base))).stdout.split("\n")
            ok = out[0] == ("1" if want_started else "0")
            if want_started:
                ok &= out[1] == '"%s" --launch "game"' % want_started and out[2] == str(want_dir)
            print(("PASS " if ok else "FAIL ") + name + ("" if ok else ": " + repr(out[:3])))
            failures += not ok

        check("nothing installed, no launcher: the setup runs as before", None)
        builder = home / "versions" / "v9.9.9" / "HookshotBuilder" / "HookshotBuilder.exe"
        builder.parent.mkdir(parents=True)
        # (a whole one: builder_ready, #2449, wants its runtime beside it)
        for name in ("HookshotBuilder.exe", "HookshotBuilder.dll", "HookshotBuilder.deps.json", "HookshotBuilder.runtimeconfig.json",
                     "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll", "WindowsBase.dll",
                     "PresentationCore.dll", "PresentationFramework.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll"):
            (builder.parent / name).write_bytes(b"MZ")
        (home / "versions" / "current.txt").write_text("v9.9.9")
        check("a builder installed, no launcher: the builder", builder, builder.parent)
        launcher = t / "Programs" / "Hookshot ü" / "Hookshot.exe"
        launcher.parent.mkdir(parents=True)
        launcher.write_bytes(b"MZ")
        record.write_bytes(str(launcher).encode("utf-8") + b"\r\n")
        check("the launcher where it last ran: it first, started in its own folder", launcher, launcher.parent)
        launcher.unlink()
        check("the launcher moved or removed since: the builder", builder, builder.parent)
        launcher.write_bytes(b"MZ")
        for what, bad in (("a bare name", "Hookshot.exe"), ("a folder", str(launcher.parent)),
                          ("a quote in it", str(launcher) + '" --evil "'), ("not an .exe", str(launcher.with_suffix(".bat")))):
            record.write_text(bad, encoding="utf-8")
            check("not a whole path to an .exe (%s): the builder" % what, builder, builder.parent)
        record.write_bytes(b"\xff\xfe" + str(launcher).encode("utf-16-le"))
        check("not UTF-8 text: the builder", builder, builder.parent)
    print("%d failed" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
