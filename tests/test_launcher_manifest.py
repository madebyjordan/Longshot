"""scripts/launcher/manifest.py writes a release's hookshot-launcher.txt that the 2.x Builders' own parser takes
(builder/linux/linux_upgrade.parse_manifest, the same rules as Upgrader.ParseManifest on Windows): the tag asked for, format 1,
each file's SHA-256, size and link on the release it is an asset of; the nightly's files on the rolling nightly release.

python -I -B tests/test_launcher_manifest.py
"""
import hashlib
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "builder" / "linux"))
sys.path.insert(0, str(ROOT / "scripts" / "launcher"))
import linux_upgrade  # noqa: E402
import manifest  # noqa: E402


class Manifest(unittest.TestCase):
    def files(self, t, version):
        z, a = t / ("Hookshot-%s-Windows-x64-portable.zip" % version), t / ("Hookshot-Launcher-%s-x86_64.AppImage" % version)
        z.write_bytes(b"PK zip of the launcher")
        a.write_bytes(b"\x7fELF the launcher's AppImage")
        return z, a

    def run_script(self, *args):
        return subprocess.run([sys.executable, "-I", str(ROOT / "scripts/launcher/manifest.py"), *map(str, args)],
                              capture_output=True, text=True)

    def test_a_release_reads_as_the_builders_read_it(self):
        with tempfile.TemporaryDirectory() as t:
            z, a = self.files(Path(t), "v3.0.0")
            r = self.run_script("--release", "v3.0.0", z, a)
            self.assertEqual(r.returncode, 0, r.stderr)
            m = linux_upgrade.parse_manifest(r.stdout, "v3.0.0")
            self.assertEqual(m.tag, "v3.0.0")
            for platform, path in (("windows-x64", z), ("linux-x64", a)):
                line = m.line(platform)
                self.assertEqual((line.sha, line.size), (hashlib.sha256(path.read_bytes()).hexdigest(), path.stat().st_size))
                self.assertEqual(line.url, "https://github.com/NotRexed/Hookshot/releases/download/v3.0.0/" + path.name)

    def test_the_nightly_is_on_the_rolling_release_under_its_own_tag(self):
        with tempfile.TemporaryDirectory() as t:
            z, a = self.files(Path(t), "v3.0.0-4-g1a2b3c4")
            r = self.run_script("--release", "nightly", "--repo", "someone/Hookshot", z, a)
            self.assertEqual(r.returncode, 0, r.stderr)
            m = linux_upgrade.parse_manifest(r.stdout, "v3.0.0-4-g1a2b3c4")
            self.assertTrue(m.line("linux-x64").url.startswith("https://github.com/someone/Hookshot/releases/download/nightly/"))
            # (a tag named outright wins over the zip's name: the nightly setup lists' own)
            r = self.run_script("--release", "nightly", "--tag", "nightly-12-1a2b3c4d", z, a)
            self.assertEqual(linux_upgrade.parse_manifest(r.stdout, "nightly-12-1a2b3c4d").tag, "nightly-12-1a2b3c4d")

    def test_what_it_refuses(self):
        with tempfile.TemporaryDirectory() as t:
            z, a = self.files(Path(t), "v3.0.0")
            other = Path(t) / "launcher.zip"
            other.write_bytes(b"x")
            self.assertNotEqual(self.run_script("--release", "v3.0.0", other, a).returncode, 0)   # (no tag in its name, none given)
            self.assertNotEqual(self.run_script("--release", "..", z, a).returncode, 0)
            self.assertNotEqual(self.run_script("--release", "v3.0.0", "--repo", "x y", z, a).returncode, 0)
            self.assertNotEqual(self.run_script("--release", "v3.0.0", z, Path(t) / "missing").returncode, 0)
            empty = Path(t) / "Hookshot-v3.0.0-Windows-x64-portable.zip"
            empty.write_bytes(b"")
            self.assertNotEqual(self.run_script("--release", "v3.0.0", empty, a).returncode, 0)

    def test_the_sample_shape(self):
        """the contract's sample (tests/fixtures/launcher/hookshot-launcher.txt) and a written one have the same lines"""
        sample = (ROOT / "tests/fixtures/launcher/hookshot-launcher.txt").read_text(encoding="utf-8")
        kinds = lambda text: [l.split()[0] for l in text.splitlines() if l and not l.startswith("#")]
        with tempfile.TemporaryDirectory() as t:
            z, a = self.files(Path(t), "v3.0.0")
            written = manifest.manifest("v3.0.0", "v3.0.0", "NotRexed/Hookshot", [("windows-x64", z), ("linux-x64", a)])
        self.assertEqual(kinds(written), kinds(sample)[:4])


if __name__ == "__main__":
    unittest.main()
