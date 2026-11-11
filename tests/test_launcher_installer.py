"""Hookshot-Installer.exe (launcher/installer/installer.c) installs the launcher it carries, and its Uninstall.exe takes it away.

The installer is compiled, a stand-in launcher zip appended to it as scripts/launcher/publish.ps1 appends the real one, and it is run
with no questions (/S) against a temporary install folder, shortcut folder, builders' folder and Settings > Apps key
(HOOKSHOT_INSTALL_DIR, HOOKSHOT_INSTALL_LINKS, HOOKSHOT_SETUP_BASE, HOOKSHOT_INSTALL_KEY): nothing of this PC's own is touched.

- installed: the launcher's files, Uninstall.exe (the installer without the zip), both shortcuts, the Apps entry;
- installed again over it, with the files of the new zip only (one the old zip had and the new one has not is gone);
- not over a launcher that is running: the one installed stays whole;
- an installer with no zip at its end refuses;
- uninstalled by Uninstall.exe from inside the install folder, as Settings > Apps runs it: the folder, the shortcuts and the
  entry go; launcher.txt goes when it names that folder (the game folders' starters then open the builder again), the
  builders' folder stays.

python -I -B tests/test_launcher_installer.py [--cc <clang>]
"""
import argparse
import io
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import zipfile

RUN_ALL_ARGS = ["--cc", "clang"]
RUN_ALL_SKIP = {"linux": "the installer is Windows' (Win32 files, shortcuts and registry)"}

ROOT = Path(__file__).resolve().parents[1]


def launcher_zip(files):
    data = io.BytesIO()
    with zipfile.ZipFile(data, "w") as z:
        for name, body in files.items():
            z.writestr("Hookshot/" + name, body)
    return data.getvalue()


def installer(stub, zipped, out):
    out.write_bytes(stub.read_bytes() + zipped + struct.pack("<Q", len(zipped)) + b"HSZIP001")
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cc", default=shutil.which("clang") or shutil.which("x86_64-w64-mingw32-clang") or "clang")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("the installer is Windows'")
    import winreg

    failures = 0

    def check(name, ok, why=""):
        nonlocal failures
        print(("PASS " if ok else "FAIL ") + name + ("" if ok else ": " + why))
        failures += not ok

    key = "Hookshot-installer-test-%d" % os.getpid()
    with tempfile.TemporaryDirectory(prefix="hookshot installer ü ") as t:
        t = Path(t)
        stub = t / "stub.exe"
        subprocess.run([args.cc, "-O1", "-municode", "-mwindows", "-DHOOKSHOT_VERSION=\"v9.9.9\"", str(ROOT / "launcher/installer/installer.c"),
                        "-lole32", "-luuid", "-lshell32", "-ladvapi32", "-o", str(stub)], check=True)
        install, links, base, log = t / "Programs" / "Hookshot", t / "links", t / "HookshotBuilder", t / "log.txt"
        base.mkdir()
        env = dict(os.environ, HOOKSHOT_INSTALL_DIR=str(install), HOOKSHOT_INSTALL_LINKS=str(links), HOOKSHOT_INSTALL_KEY=key,
                   HOOKSHOT_SETUP_BASE=str(base), HOOKSHOT_INSTALL_LOG=str(log))
        run = lambda exe, *a: subprocess.run([str(exe), *a], env=env, timeout=60).returncode

        def entry():
            try:
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall\\" + key) as k:
                    return {n: winreg.QueryValueEx(k, n)[0] for n in ("DisplayName", "DisplayVersion", "UninstallString", "InstallLocation")}
            except OSError:
                return None

        try:
            first = installer(stub, launcher_zip({"Hookshot.exe": b"MZ first", "libSkiaSharp.dll": b"skia", "old.dll": b"old"}), t / "first.exe")
            code = run(first, "/S")
            check("installed", code == 0 and (install / "Hookshot.exe").read_bytes() == b"MZ first" and (install / "old.dll").exists(),
                  "exit %d" % code)
            check("Uninstall.exe is the installer without its zip", (install / "Uninstall.exe").read_bytes() == stub.read_bytes())
            check("both shortcuts", (links / "Start menu" / "Hookshot.lnk").exists() and (links / "Desktop" / "Hookshot.lnk").exists())
            e = entry()
            check("Settings > Apps entry", e is not None and e["DisplayName"] == "Hookshot" and e["DisplayVersion"] == "9.9.9" and
                  e["InstallLocation"] == str(install) and e["UninstallString"] == '"%s" /uninstall' % (install / "Uninstall.exe"), repr(e))
            check("nothing left beside it", sorted(p.name for p in install.parent.iterdir()) == ["Hookshot"],
                  repr(list(install.parent.iterdir())))

            second = installer(stub, launcher_zip({"Hookshot.exe": b"MZ second", "libSkiaSharp.dll": b"skia 2"}), t / "second.exe")
            code = run(second, "/S")
            check("installed again over it, the new zip's files only", code == 0 and (install / "Hookshot.exe").read_bytes() == b"MZ second"
                  and not (install / "old.dll").exists(), "exit %d" % code)

            third = installer(stub, launcher_zip({"Hookshot.exe": b"MZ third"}), t / "third.exe")
            with open(install / "Hookshot.exe", "rb"):   # (as a running program holds its file: no rename of its folder)
                code = run(third, "/S")
            check("not over a launcher that is running: refused, the one installed whole", code == 3 and
                  (install / "Hookshot.exe").read_bytes() == b"MZ second" and (install / "libSkiaSharp.dll").exists(), "exit %d" % code)

            code = run(stub, "/S")
            check("an installer with no zip at its end refuses", code == 1 and (install / "Hookshot.exe").read_bytes() == b"MZ second",
                  "exit %d" % code)

            (base / "launcher.txt").write_text(str(install / "Hookshot.exe") + "\n", encoding="utf-8")
            (base / "versions").mkdir()
            code = run(install / "Uninstall.exe", "/uninstall", "/S")
            until = time.time() + 20
            while install.exists() and time.time() < until:
                time.sleep(0.2)
            time.sleep(0.5)
            check("uninstalled from inside its folder (a copy in %TEMP% does it)", code == 0 and not install.exists(), "exit %d" % code)
            check("its shortcuts and Apps entry gone", not (links / "Start menu" / "Hookshot.lnk").exists() and
                  not (links / "Desktop" / "Hookshot.lnk").exists() and entry() is None)
            check("launcher.txt naming it gone, the builders kept", not (base / "launcher.txt").exists() and (base / "versions").exists())

            run(second, "/S")
            elsewhere = str(t / "portable" / "Hookshot.exe")
            (base / "launcher.txt").write_text(elsewhere + "\n", encoding="utf-8")
            os.replace(install / "Uninstall.exe", t / "Uninstall.exe")   # (run from outside the folder: it does it itself)
            code = run(t / "Uninstall.exe", "/uninstall", "/S")
            check("uninstalled from elsewhere; a launcher.txt naming another copy stays", code == 0 and not install.exists() and
                  (base / "launcher.txt").read_text(encoding="utf-8").strip() == elsewhere, "exit %d" % code)
        finally:
            try:
                winreg.DeleteKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Uninstall\\" + key)
            except OSError:
                pass
    print("%d failed" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
