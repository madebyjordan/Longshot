"""scripts/test-run.ps1 makes its run folder from either game folder layout, and keeps -Env's switches apart.

A builder's folder has bin\\hookshot.exe; a checkout's build (scripts/build-game.ps1 -Channel dev) has hookshot.exe beside
image.bin, and the script refused it. Started with powershell -File, as its own header says to, -Env "A=1","B=2" reaches
it as the one string "A=1,B=2": the game then read one switch, A, with the rest as its value. A dry run on stand-in
folders (no game: empty files) shows both, and -User and -Options. Windows PowerShell only.

python -I -B tests/test_test_run_folder.py
"""
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "test-run.ps1"


def game_folder(root, flat):
    """A stand-in game folder: the files the script looks for, a few bytes each"""
    root.mkdir()
    programs = root if flat else root / "bin"
    programs.mkdir(exist_ok=True)
    for name in ("hookshot.exe", "hookshot_vk.dll", "SDL3.dll"):
        (programs / name).write_bytes(b"MZ")
    for name in ("image.bin", "hookshot_shaders.bin", "hookshot_programs.bin", "hookshot_log.txt"):
        (root / name).write_bytes(b"x")
    (root / "hookshot_options.ini").write_text("output_res=0\n", encoding="ascii")
    (root / "romfs").mkdir()
    (root / "user" / "save").mkdir(parents=True)
    (root / "user" / "save" / "slot.sav").write_bytes(b"game")
    return root


def dry_run(shell, game, *more):
    r = subprocess.run([shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), "-Game", str(game), "-Name", "t",
                        "-DryRun", "-Keep", *more], capture_output=True, text=True)
    if r.returncode:
        raise SystemExit("test-run.ps1 failed on %s:\n%s%s" % (game.name, r.stdout, r.stderr))
    runs = sorted((game.parent / "runs").iterdir())
    return runs[-1] / "game"


def main():
    shell = shutil.which("powershell") or shutil.which("pwsh")
    if os.name != "nt" or not shell:
        print("SKIP: the script is Windows PowerShell's")
        return
    with tempfile.TemporaryDirectory(prefix="test-run-") as tmp:
        tmp = Path(tmp)
        for flat in (False, True):
            base = tmp / ("flat" if flat else "bin")
            base.mkdir()
            run = dry_run(shell, game_folder(base / "game", flat))
            # either way the run folder has a bin\ with the programs, the image one folder up, and none of the caches or logs
            got = sorted(p.name for p in (run / "bin").iterdir())
            assert got == ["SDL3.dll", "hookshot.exe", "hookshot_vk.dll"], (flat, got)
            assert (run / "image.bin").exists() and (run / "hookshot_shaders.bin").exists()
            assert not (run / "hookshot_programs.bin").exists() and not (run / "bin" / "hookshot_log.txt").exists()
            assert (run / "user" / "save" / "slot.sav").read_bytes() == b"game"
            assert (run / "hookshot_options.ini").read_text(encoding="ascii").strip() == "output_res=0"

        # -Env as powershell -File hands it over: one string; a value's own comma stays in the value
        base = tmp / "env"
        base.mkdir()
        saves = tmp / "pinned-user"
        (saves / "save").mkdir(parents=True)
        (saves / "save" / "slot.sav").write_bytes(b"pinned")
        options = tmp / "pinned.ini"
        options.write_text("output_res=1\n", encoding="ascii")
        run = dry_run(shell, game_folder(base / "game", True), "-Env", "A=1,RX_VK_CACHE_DIR=C:\\a,b\\c,HOS_PROFILE=5",
                      "-User", str(saves), "-Options", str(options))
        env = (run / "hookshot_env.txt").read_text(encoding="ascii").split()
        assert env == ["HOOKSHOT_NO_UPDATE_CHECK=1", "A=1", "RX_VK_CACHE_DIR=C:\\a,b\\c", "HOS_PROFILE=5"], env
        assert (run / "user" / "save" / "slot.sav").read_bytes() == b"pinned"
        assert (run / "hookshot_options.ini").read_text(encoding="ascii").strip() == "output_res=1"

        # neither layout: said, not started
        (tmp / "none").mkdir()
        bad = game_folder(tmp / "none" / "game", True)
        (bad / "hookshot.exe").unlink()
        r = subprocess.run([shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), "-Game", str(bad), "-DryRun"],
                           capture_output=True, text=True)
        assert r.returncode != 0 and "not a game folder" in r.stdout + r.stderr, (r.returncode, r.stdout, r.stderr)
    print("PASS: both game folder layouts make a run folder, and -Env's switches stay apart")


if __name__ == "__main__":
    main()
