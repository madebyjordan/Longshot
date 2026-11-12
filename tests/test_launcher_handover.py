"""The launcher's side of the hand-over from a 2.x Builder (launcher/Hookshot/Handover.cs, docs/launcher-upgrade.md), with the
contract's sample files (tests/fixtures/launcher/), as the Builders' own checks use them (test_builder_upgrade.py,
test_linux_upgrade.py):

- the sample import files are read, and one of another format, with no absolute folder or not JSON is refused;
- the Windows sample taken into a launcher with no settings gives the Builder's own (the sample builder-settings.json) and
  its work folder; a launcher with settings keeps them, and its default folder gives way only when it holds no game;
- the Linux Builder's builder.ini, read on a first start with nothing dropped, is the sample import-linux.json;
- import/ is read and emptied at a start (a bad file kept as .bad, a .part left alone), in HOOKSHOT_LAUNCHER_HOME;
- the 2.x command line (--launch, --update [--tag] [--fetched], --play);
- an import the Linux upgrader writes itself (builder/linux/linux_upgrade.py) is taken whole.

Handover.cs, GameFolder.cs and BuilderSettings.cs are compiled with a stand-in for Builders (tests/launcher_handover_test.cs):
no Avalonia, no network, no game.

python -I -B tests/test_launcher_handover.py [--dotnet dotnet]
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

RUN_ALL_ARGS = ["--dotnet", "{dotnet}"]

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ("launcher/Hookshot/Handover.cs", "launcher/Hookshot/GameFolder.cs", "builder/HookshotBuilder/BuilderSettings.cs")
PROJECT = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>%s</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NuGetAudit>false</NuGetAudit>
<UseSharedCompilation>false</UseSharedCompilation><InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup></Project>"""


def upgrader_import(into):
    """The Linux upgrader's own import file for the sample builder.ini, a folder stamped as the sample's, into <into>/import"""
    sys.path.insert(0, str(ROOT / "builder" / "linux"))
    import linux_upgrade
    folder = into / "game"
    folder.mkdir()
    (folder / "hookshot_version.txt").write_text("version=v2.3.0-4-g1a2b3c4\nbuilder=/old\n", encoding="utf-8")
    cfg = linux_upgrade.read_ini((ROOT / "tests/fixtures/launcher/builder.ini").read_text(encoding="utf-8"))
    linux_upgrade.write_import(linux_upgrade.import_for(cfg, str(folder)), home=str(into))
    return into / "import"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        parser.error("a .NET SDK is required (the launcher's is .NET 8)")
    with tempfile.TemporaryDirectory(prefix="launcher-handover-build-") as folder:
        root = Path(folder)
        for name in SOURCES:
            shutil.copyfile(ROOT / name, root / Path(name).name)
        shutil.copyfile(ROOT / "tests/launcher_handover_test.cs", root / "Program.cs")
        (root / "NuGet.Config").write_text("<configuration><packageSources><clear/></packageSources></configuration>", encoding="utf-8")
        project = root / "Fixture.csproj"
        project.write_text(PROJECT % "net8.0", encoding="utf-8")
        restore = subprocess.run([dotnet, "restore", str(project), "--disable-build-servers"], capture_output=True, text=True)
        if restore.returncode and "NU1100" in restore.stdout + restore.stderr:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                raise SystemExit("on a GitHub runner the launcher's code must build for net8.0, and its reference pack is "
                                 "missing\n" + restore.stdout + restore.stderr)
            sdk = subprocess.run([dotnet, "--version"], capture_output=True, text=True, check=True).stdout.strip()
            project.write_text(PROJECT % ("net%s.0" % sdk.split(".")[0]), encoding="utf-8")
        elif restore.returncode:
            raise SystemExit(restore.stdout + restore.stderr)
        upgrader = root / "upgrader"
        upgrader.mkdir()
        dropped = upgrader_import(upgrader)
        env = dict(os.environ)
        env.pop("HOOKSHOT_LAUNCHER_HOME", None)
        return subprocess.run([dotnet, "run", "--project", str(project), "--configuration", "Release", "--disable-build-servers",
                               "--", str(ROOT / "tests/fixtures/launcher"), str(dropped)], env=env).returncode


if __name__ == "__main__":
    raise SystemExit(main())
