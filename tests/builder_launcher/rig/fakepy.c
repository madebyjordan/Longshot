/* A stand-in for a version's tools\python\python.exe in the test rig: started as the builder starts make_game.py
   (python.exe -I <tools>\nx2\make_game.py --update <game folder> ...), it says what make_game.py says while it builds
   (@@stage, @@progress, @@plan, @@file, @@eta, @@done) in a few seconds, and writes the folder's hookshot_version.txt as the
   version in <tools>\version.txt. FAKE_STEP_MS: the wait between its lines (150). FAKE_FAIL=1: it stops half way with an error. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <windows.h>

static void say(const char *line) { puts(line); fflush(stdout); }

int main(int argc, char **argv) {
    const char *script = NULL, *folder = NULL;
    for (int i = 1; i < argc; i++) {
        if (strstr(argv[i], "make_game.py")) script = argv[i];
        if (!strcmp(argv[i], "--update") && i + 1 < argc) folder = argv[i + 1];
    }
    if (!script || !folder) { say("@@error the stand-in was not started as make_game.py --update <folder>"); return 2; }
    const char *e = getenv("FAKE_STEP_MS");
    const int step = e && *e ? atoi(e) : 150;
    const int fail = getenv("FAKE_FAIL") && *getenv("FAKE_FAIL") == '1';

    char version[128] = "v0.0.0", path[1024];
    snprintf(path, sizeof path, "%s", script);
    char *cut = strstr(path, "\\nx2\\");
    if (cut) {
        strcpy(cut, "\\version.txt");
        FILE *f = fopen(path, "r");
        if (f) { if (fgets(version, sizeof version, f)) version[strcspn(version, "\r\n")] = 0; fclose(f); }
    }

    say("@@stage read"); Sleep(step);
    say("@@stage lift");
    for (int i = 1; i <= 4; i++) { char l[64]; snprintf(l, sizeof l, "@@progress %.2f", i / 4.0); say(l); Sleep(step); }
    say("@@stage compile");
    say("@@plan 8 516");
    for (int i = 1; i <= 8; i++) {
        char l[96];
        snprintf(l, sizeof l, "@@file src_%d.c 3 new", i); say(l);
        snprintf(l, sizeof l, "@@progress %.3f", i / 8.0); say(l);
        snprintf(l, sizeof l, "@@eta %d", (8 - i) * 45); say(l);
        Sleep(step);
        if (fail && i == 4) { say("@@error The compiler stopped on src_4.c (a made-up failure of the test rig)."); return 1; }
    }
    say("@@stage finish"); Sleep(step);
    snprintf(path, sizeof path, "%s\\hookshot_version.txt", folder);
    FILE *f = fopen(path, "w");
    if (!f) { say("@@error the game folder could not be written"); return 1; }
    fprintf(f, "version=%s\n", version);
    fclose(f);
    char done[1100];
    snprintf(done, sizeof done, "@@done %s", folder);
    say(done);
    return 0;
}
