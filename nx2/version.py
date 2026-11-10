"""Which Hookshot a build is: the release it came from and a fingerprint of its sources.

The game prints it as the first line of hookshot_log.txt and in the Insert menu, so a report says what was run.

    python -I nx2/version.py            prints it for this folder

The release name comes from, in order: HOOKSHOT_VERSION; a VERSION file at the root (written when a release is packaged,
since the builder's tools folder has no git history); in a checkout, the release tag when the commit is exactly one,
on dev the release it is heading for and the commit (`pre-1.1.0 6353e9b`, from CHANGELOG.md's unreleased heading),
otherwise the branch and the commit (`main 03e97b8`; `-dirty` with changes not committed). The
fingerprint is a hash of the sources in this folder (host/, gpu/, nx2/), line endings aside: every build from the
same release zip gives the same one, so even a build that says "unknown" can be traced to the zip it came from. (A
checkout and a release's tools folder hold different sets of files, so their fingerprints differ.)
"""
import hashlib
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCES = ("host", "gpu", "nx2")
SOURCE_EXT = (".c", ".cc", ".cpp", ".h", ".hpp", ".inc", ".py", ".glsl", ".comp", ".vert", ".frag", ".spv")


def release(root=ROOT):
    name = os.environ.get("HOOKSHOT_VERSION", "").strip()
    if not name:
        for file in ("VERSION", "version.txt"):   # (version.txt: the release's tag, written into the builder's tools when it is packed)
            try:
                with open(os.path.join(root, file), encoding="utf-8") as f:
                    name = f.readline().strip()
            except OSError:
                pass
            if name:
                break
    if not name and os.path.exists(os.path.join(root, ".git")):
        name = checkout_name(root)
    return name or "unknown"


def checkout_name(root=ROOT):
    """(longshot) A build from a checkout: the release tag when it is exactly one and has no changes ("v1.0.0"); on dev the
    release it is heading for and the commit ("pre-1.1.0 6353e9b", CHANGELOG.md's unreleased heading); else the branch and the
    commit ("main 03e97b8-dirty"). It was git describe, the nearest tag: Hookshot's v2.x tags
    sit beside Longshot's own, and a dev build said "v2.0.6-480-g86148ce", neither Longshot's release nor which branch"""
    def git(*args):
        try:
            r = subprocess.run(["git", "-C", root] + list(args), capture_output=True, text=True, timeout=20)
        except (OSError, subprocess.SubprocessError):
            return None
        return r.stdout.strip() if r.returncode == 0 else None

    sha = git("rev-parse", "--short=7", "HEAD")
    if not sha:
        return ""
    dirty = bool(git("status", "--porcelain", "--untracked-files=no"))
    tag = git("describe", "--tags", "--exact-match", "--match", "v[0-9]*", "HEAD")
    if tag and not dirty:
        return tag
    branch = git("rev-parse", "--abbrev-ref", "HEAD")
    if not branch or branch == "HEAD":
        branch = "detached"
    if branch == "dev":   # (dev is always the next release before it is out: "pre-1.1.0 6353e9b")
        upcoming = unreleased(root)
        if upcoming:
            branch = "pre-" + upcoming
    return "%s %s%s" % (branch, sha, "-dirty" if dirty else "")


def unreleased(root=ROOT):
    """(longshot) The release dev is heading for: CHANGELOG.md's "## 1.1.0 (unreleased)" heading, or None"""
    try:
        with open(os.path.join(root, "CHANGELOG.md"), encoding="utf-8") as f:
            for line in f:
                m = re.match(r"##\s+v?(\d+\.\d+\.\d+)\s+\(unreleased\)", line)
                if m:
                    return m.group(1)
    except OSError:
        pass
    return None


def fingerprint(root=ROOT):
    h = hashlib.sha1()
    for top in SOURCES:
        base = os.path.join(root, top)
        for d, dirs, files in os.walk(base):
            dirs[:] = sorted(x for x in dirs if x != "__pycache__")
            for f in sorted(files):
                if not f.endswith(SOURCE_EXT):
                    continue
                p = os.path.join(d, f)
                with open(p, "rb") as src:
                    data = src.read().replace(b"\r\n", b"\n")
                h.update(os.path.relpath(p, root).replace("\\", "/").encode() + b"\0" + data + b"\0")
    return h.hexdigest()[:8]


def describe(root=ROOT):
    """'movie-sync (source 1a2b3c4d)': safe to pass as a C string literal on any command line."""
    text = "%s (source %s)" % (release(root), fingerprint(root))
    return re.sub(r"[^A-Za-z0-9 ._+()/-]", "_", text)[:80]


def define(root=ROOT):
    return '-DHOOKSHOT_VERSION="%s"' % describe(root)


if __name__ == "__main__":
    print(describe())
