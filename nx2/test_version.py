"""nx2/version.py, game-free: where the release name comes from, and a fingerprint that ignores line endings.

    python -I -B nx2/test_version.py
"""
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import version  # noqa: E402


def tree(root, eol):
    for rel in ("host/a.c", "gpu/vk/b.cpp", "nx2/c.py"):
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "wb") as f:
            f.write(("line one%sline two%s" % (eol, eol)).encode())


def main():
    os.environ.pop("HOOKSHOT_VERSION", None)
    with tempfile.TemporaryDirectory() as lf, tempfile.TemporaryDirectory() as crlf:
        tree(lf, "\n")
        tree(crlf, "\r\n")
        assert version.release(lf) == "unknown", version.release(lf)
        assert version.fingerprint(lf) == version.fingerprint(crlf), "line endings changed the fingerprint"
        with open(os.path.join(lf, "nx2", "c.py"), "a") as f:
            f.write("changed\n")
        assert version.fingerprint(lf) != version.fingerprint(crlf), "a changed source kept the fingerprint"

        with open(os.path.join(lf, "VERSION"), "w") as f:
            f.write("movie-sync\nignored second line\n")
        assert version.release(lf) == "movie-sync"
        os.environ["HOOKSHOT_VERSION"] = "from-env"
        assert version.release(lf) == "from-env", "HOOKSHOT_VERSION comes first"

        os.environ["HOOKSHOT_VERSION"] = 'v1 "quoted" \\ & | %PATH%'
        d = version.describe(lf)
        assert not set(d) & set('"\\&|%'), d
        assert version.define(lf).startswith('-DHOOKSHOT_VERSION="') and version.define(lf).endswith(')"')
        os.environ.pop("HOOKSHOT_VERSION")
    checkout()
    print("version: ok (%s)" % version.describe())


def checkout():
    """(longshot) a checkout's name: the tag when exactly on it, else the branch and commit, -dirty with changes"""
    import shutil
    import subprocess
    if not shutil.which("git"):
        print("version: no git, checkout names not checked")
        return
    with tempfile.TemporaryDirectory() as repo:
        def git(*args):
            return subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false",
                                   "-c", "tag.gpgsign=false"] + list(args), capture_output=True, text=True, check=True).stdout.strip()
        git("init", "-q", "-b", "dev")
        tree(repo, "\n")
        git("add", "-A")
        git("commit", "-q", "-m", "one")
        git("tag", "v2.0.6")   # (a Hookshot tag on an earlier commit: not this build's name)
        with open(os.path.join(repo, "host", "a.c"), "a") as f:
            f.write("two\n")
        git("commit", "-q", "-am", "two")
        sha = git("rev-parse", "--short=7", "HEAD")
        assert version.release(repo) == "dev " + sha, version.release(repo)
        with open(os.path.join(repo, "host", "a.c"), "a") as f:
            f.write("not committed\n")
        assert version.release(repo) == "dev %s-dirty" % sha, version.release(repo)
        git("checkout", "-q", "--", ".")
        with open(os.path.join(repo, "CHANGELOG.md"), "w", encoding="utf-8") as f:
            f.write("# Changelog\n\n## 1.1.0 (unreleased)\n\n## 1.0.0 (2026-11-10)\n")
        git("add", "CHANGELOG.md")
        git("commit", "-q", "-m", "changelog")
        sha = git("rev-parse", "--short=7", "HEAD")
        assert version.release(repo) == "pre-1.1.0 " + sha, "dev is the next release before it is out: " + version.release(repo)
        git("checkout", "-q", "-b", "feature")
        assert version.release(repo) == "feature " + sha, version.release(repo)
        git("checkout", "-q", "dev")
        git("tag", "v1.0.0")
        assert version.release(repo) == "v1.0.0", version.release(repo)
        with open(os.path.join(repo, "untracked.txt"), "w") as f:
            f.write("a file git does not know\n")
        assert version.release(repo) == "v1.0.0", "an untracked file is not a change to the build"
        git("checkout", "-q", "--detach", "HEAD~2")
        assert version.release(repo).startswith("v2.0.6"), version.release(repo)


if __name__ == "__main__":
    main()
