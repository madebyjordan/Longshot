"""hookshot-launcher.txt for a release: what tells a 2.x Builder that this release ships the Hookshot Launcher, and where to get
it (docs/launcher-upgrade.md, section 1). A Builder from 2.2 on that finds it offers Upgrade, and installs the file named for
its system after checking its size and SHA-256.

    python3 -I scripts/launcher/manifest.py --release <release> [--tag <tag>] [--repo <owner/name>] <zip> <appimage> > hookshot-launcher.txt

--release: the release the files are assets of (a tag, or `nightly` for the rolling nightly). --tag: the tag a Builder asks for
(it checks the file's `tag` line against it); by default the version in the zip's name (Hookshot-<version>-Windows-x64-portable.zip),
which is the release's tag for a release. For the nightly it must be the tag its setup lists carry. <zip>: the portable zip, whose
root holds HookshotLauncher.exe (scripts/launcher/publish.ps1). <appimage>: the launcher's AppImage (scripts/launcher/publish.sh).
"""
import argparse
import hashlib
import os
import re
import sys

ZIP_NAME = re.compile(r"^Hookshot-(.+)-Windows-x64-portable\.zip$")
TAG = re.compile(r"^[0-9A-Za-z._+-]{1,128}$")


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest(), os.path.getsize(path)


def manifest(tag, release, repo, files):
    """The file's text. files: (platform, path) pairs, each an asset of <release> under its own name"""
    if not TAG.match(tag) or tag in (".", ".."):
        raise ValueError("%r is not a version name" % tag)
    if not TAG.match(release) or release in (".", ".."):
        raise ValueError("%r is not a release" % release)
    lines = ["# The Hookshot Launcher of %s, for the 2.x Builders' hand-over (docs/launcher-upgrade.md)" % tag,
             "tag %s" % tag, "format 1"]
    for platform, path in files:
        sha, size = digest(path)
        if size == 0:
            raise ValueError("%s is empty" % path)
        lines.append("launcher %s %s %d https://github.com/%s/releases/download/%s/%s" %
                     (platform, sha, size, repo, release, os.path.basename(path)))
    return "\n".join(lines) + "\n"


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--release", required=True)
    p.add_argument("--tag")
    p.add_argument("--repo", default="NotRexed/Hookshot")
    p.add_argument("zip")
    p.add_argument("appimage")
    a = p.parse_args(argv)
    tag = a.tag
    if not tag:
        named = ZIP_NAME.match(os.path.basename(a.zip))
        if not named:
            p.error("no --tag, and %s is not named Hookshot-<version>-Windows-x64-portable.zip" % a.zip)
        tag = named.group(1)
    if not re.match(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", a.repo):
        p.error("--repo is owner/name")
    try:
        sys.stdout.write(manifest(tag, a.release, a.repo, [("windows-x64", a.zip), ("linux-x64", a.appimage)]))
    except (OSError, ValueError) as e:
        p.error(str(e))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
