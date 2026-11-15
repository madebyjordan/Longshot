#!/usr/bin/env python3
"""Save profile selection and custom nicknames (host/nn_applet.c, host/nn_fs.c, gpu/vk/vk_menu.cpp)."""

import argparse
import os
import subprocess
import sys
import tempfile

RUN_ALL_ARGS = ["--cc", "gcc"]

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def slice_between(src: str, start: str, end: str) -> str:
    i = src.index(start)
    j = src.index(end, i)
    return src[i:j]


def check_menu_sources() -> None:
    with open(os.path.join(ROOT, "gpu", "vk", "vk_menu.h"), encoding="utf-8") as f:
        menu_h = f.read()
    with open(os.path.join(ROOT, "gpu", "vk", "vk_menu.cpp"), encoding="utf-8") as f:
        menu_cpp = f.read()
    with open(os.path.join(ROOT, "docs", "hookshot-readme.md"), encoding="utf-8") as f:
        readme = f.read()

    if "int profile = 0;" not in menu_h:
        raise AssertionError("vk_menu.h: Options::profile missing")
    for needle in (
        "tx_save_profile(int v)",
        '{TAB_GAMEPLAY, "Save profile", &opts.profile, 0, 7, 1, tx_save_profile,',
        'if (r.value == &opts.profile) return "profile";',
        '"profile_name_"',
        'getenv("HOS_PROFILE")',
    ):
        if needle not in menu_cpp:
            raise AssertionError(f"vk_menu.cpp: missing {needle!r}")
    for needle in ("| Gameplay | Save profile |", "profile_name_1", "HOS_PROFILE"):
        if needle not in readme:
            raise AssertionError(f"docs/hookshot-readme.md: missing {needle!r}")


HARNESS = r"""
#define _CRT_SECURE_NO_WARNINGS
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef unsigned long DWORD;
#define MAX_PATH 260

static char fake_exe_path[MAX_PATH];
static DWORD hos_game_path_a(char *buf, DWORD cap) {
    return (DWORD)snprintf(buf, cap, "%s", fake_exe_path);
}
static void hos_log(const char *fmt, ...) { (void)fmt; }

typedef struct { uint64_t r[8]; } Ctx;
static uint8_t g_mem[0x1000];
#define GP(a) (g_mem + (a))
#define G64(a) (*(uint64_t *)GP(a))
#define G32(a) (*(uint32_t *)GP(a))
#define ARG(i) (c->r[i])
#define RET(v) (c->r[0] = (uint64_t)(v))
#define HLE(name) void imp_##name(Ctx *c)

static char last_mount_name[32];
static char last_mount_sub[64];
#define R_NOT_MOUNTED 0x2F5C02u
static const char *gstr(uint64_t a) { return (const char *)GP(a); }
static int user_mount(const char *name, const char *sub) {
    snprintf(last_mount_name, sizeof last_mount_name, "%s", name);
    snprintf(last_mount_sub, sizeof last_mount_sub, "%s", sub);
    return 1;
}

/* ==== SLICED FROM host/nn_applet.c and host/nn_fs.c ==== */
__SLICED_CODE__
/* ==== END SLICE ==== */

static int fail(const char *msg) {
    fprintf(stderr, "FAIL: %s\n", msg);
    return 1;
}

static void reset_account(void) {
    user_uid[0] = 0;
    user_nickname[0] = 0;
    memset(g_mem, 0, sizeof g_mem);
    last_mount_name[0] = 0;
    last_mount_sub[0] = 0;
}

static void write_ini(const char *dir, const char *body) {
    char p[MAX_PATH];
    snprintf(p, sizeof p, "%s/hookshot_options.ini", dir);
    FILE *f = fopen(p, "wb");
    if (body && f) fputs(body, f);
    if (f) fclose(f);
}

#ifdef _WIN32
#define SEP "\\"
static void set_env(const char *k, const char *v) { _putenv_s(k, v ? v : ""); }
#else
#define SEP "/"
static void set_env(const char *k, const char *v) {
    if (v && *v) setenv(k, v, 1);
    else unsetenv(k);
}
#endif

int main(int argc, char **argv) {
    if (argc < 2) return fail("missing temp dir");
    const char *dir = argv[1];
    snprintf(fake_exe_path, sizeof fake_exe_path, "%s" SEP "game.exe", dir);
    set_env("HOS_PROFILE", "");

    /* 1. Default profile (no ini, no env): Profile 1 ("Player"), original save directory */
    reset_account();
    Ctx c = {{0}};
    imp__ZN2nn7account10InitializeEv(&c);
    c.r[0] = 0x100;
    imp__ZN2nn7account22TryOpenPreselectedUserEPNS0_10UserHandleE(&c);
    if (c.r[0] != 1) return fail("TryOpenPreselectedUser result");
    if (G64(0x100) != 1 || G64(0x108) != 0x5a3c9e1f0b7d2468ULL) return fail("default uid mismatch");

    c.r[0] = 0x150;
    c.r[1] = 0x100;
    imp__ZN2nn7account9GetUserIdEPNS0_3UidERKNS0_10UserHandleE(&c);
    if (G64(0x150) != 1 || G64(0x158) != 0x5a3c9e1f0b7d2468ULL) return fail("GetUserId mismatch");

    c.r[0] = 0x200;
    c.r[1] = 0x100;
    imp__ZN2nn7account11GetNicknameEPNS0_8NicknameERKNS0_3UidE(&c);
    if (strcmp((const char *)GP(0x200), "Player") != 0) return fail("default nickname should be Player");

    strcpy((char *)GP(0x300), "save");
    c.r[0] = 0x300;
    c.r[1] = 0x100;
    imp__ZN2nn2fs13MountSaveDataEPKcRKNS_7account3UidE(&c);
    if (strcmp(last_mount_sub, "save\\5a3c9e1f0b7d24680000000000000001") != 0)
        return fail("default save mount path mismatch");
    imp__ZN2nn7account9CloseUserERKNS0_10UserHandleE(&c);

    /* 2. Ini selects profile=1 (Profile 2) with custom nickname */
    write_ini(dir, "profile=1\nprofile_name_2=  Link  \n");
    reset_account();
    c.r[0] = 0x100;
    imp__ZN2nn7account22TryOpenPreselectedUserEPNS0_10UserHandleE(&c);
    if (G64(0x100) != 2) return fail("profile=1 should select uid[0]==2");

    c.r[0] = 0x200;
    c.r[1] = 0x100;
    imp__ZN2nn7account11GetNicknameEPNS0_8NicknameERKNS0_3UidE(&c);
    if (strcmp((const char *)GP(0x200), "Link") != 0) return fail("custom profile_name_2 should be trimmed to Link");

    strcpy((char *)GP(0x300), "save");
    c.r[0] = 0x300;
    c.r[1] = 0x100;
    imp__ZN2nn2fs13MountSaveDataEPKcRKNS_7account3UidE(&c);
    if (strcmp(last_mount_sub, "save\\5a3c9e1f0b7d24680000000000000002") != 0)
        return fail("profile 2 save mount path mismatch");

    /* 3. Default numbered nickname when profile_name_N is not set, plus ListAllUsers and GetUserExistence */
    write_ini(dir, "profile=3\n");
    reset_account();
    c.r[0] = 0x100;
    c.r[1] = 0x110;
    c.r[2] = 8;
    imp__ZN2nn7account12ListAllUsersEPiPNS0_3UidEi(&c);
    if (G32(0x100) != 1 || G64(0x110) != 4) return fail("ListAllUsers should return profile 4");

    c.r[0] = 0x200;
    c.r[1] = 0x110;
    imp__ZN2nn7account11GetNicknameEPNS0_8NicknameERKNS0_3UidE(&c);
    if (strcmp((const char *)GP(0x200), "Player 4") != 0) return fail("default profile 4 nickname should be Player 4");

    c.r[0] = 0x250;
    c.r[1] = 0x110;
    imp__ZN2nn7account16GetUserExistenceEPbRKNS0_3UidE(&c);
    if (*GP(0x250) != 1) return fail("GetUserExistence should be true for active profile");
    G64(0x110) = 1;
    c.r[0] = 0x250;
    c.r[1] = 0x110;
    imp__ZN2nn7account16GetUserExistenceEPbRKNS0_3UidE(&c);
    if (*GP(0x250) != 0) return fail("GetUserExistence should be false for inactive profile");

    /* 4. HOS_PROFILE env overrides ini, clamps to 1..8, and 32-byte nickname truncation */
    write_ini(dir, "profile=0\nprofile_name_8=12345678901234567890123456789012EXTRA\n");
    set_env("HOS_PROFILE", "99");
    reset_account();
    c.r[0] = 0x100;
    imp__ZN2nn7account22TryOpenPreselectedUserEPNS0_10UserHandleE(&c);
    if (G64(0x100) != 8) return fail("HOS_PROFILE=99 should clamp to profile 8");

    c.r[0] = 0x200;
    c.r[1] = 0x100;
    imp__ZN2nn7account11GetNicknameEPNS0_8NicknameERKNS0_3UidE(&c);
    if (strcmp((const char *)GP(0x200), "12345678901234567890123456789012") != 0)
        return fail("nickname should truncate cleanly at 32 bytes");

    printf("ok\n");
    return 0;
}
"""


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--cc", default="gcc")
    args = ap.parse_args()

    check_menu_sources()

    with open(os.path.join(ROOT, "host", "nn_applet.c"), encoding="utf-8") as f:
        applet = f.read()
    with open(os.path.join(ROOT, "host", "nn_fs.c"), encoding="utf-8") as f:
        fs = f.read()

    account_block = slice_between(
        applet,
        "/* ---- nn::account:",
        "/* ---- nn::time:",
    )
    list_users_block = slice_between(
        applet,
        "/* ListAllUsers(int* count, Uid* out, int max) -> Result */",
        "/* OpenUser(UserHandle*, Uid const&) -> Result */",
    )
    mount_save_block = slice_between(
        fs,
        "/* MountSaveData(char const*, account::Uid const&) */",
        "/* MountSaveDataReadOnly",
    )

    sliced = account_block + "\n" + list_users_block + "\n" + mount_save_block
    c_src = HARNESS.replace("__SLICED_CODE__", sliced)

    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        c_path = os.path.join(tmp, "harness.c")
        exe_path = os.path.join(tmp, "harness.exe" if os.name == "nt" else "harness")
        with open(c_path, "w", encoding="utf-8") as f:
            f.write(c_src)

        is_msvc = os.path.basename(args.cc).lower() in ("cl", "cl.exe")
        if is_msvc:
            cmd = [args.cc, "/nologo", "/std:c11", "/W4", "/WX", "/wd4100",
                   f"/Fo{os.path.join(tmp, 'harness.obj')}", f"/Fe:{exe_path}", c_path]
        else:
            cmd = [args.cc, "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror", "-Wno-unused-parameter",
                   c_path, "-o", exe_path]
        comp = subprocess.run(cmd, capture_output=True, text=True)
        if comp.returncode != 0:
            sys.stderr.write(comp.stdout + comp.stderr)
            return 1

        run = subprocess.run([exe_path, tmp], capture_output=True, text=True)
        if run.returncode != 0 or run.stdout.strip() != "ok":
            sys.stderr.write(run.stdout + run.stderr)
            return 1

    print("PASS: test_save_profiles")
    return 0


if __name__ == "__main__":
    sys.exit(main())
