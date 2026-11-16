"""The first Windows release can borrow a pinned setup list without changing release precedence."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("release_parts", ROOT / "scripts/release_parts.py")
release_parts = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release_parts)

API = "https://api.github.com/repos/fixture/Longshot/releases?per_page=30"
BOOTSTRAP = "https://example.invalid/bootstrap.txt"
STABLE = "https://example.invalid/stable.txt"
MANIFEST = b"tag v2.1.0\n"


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.output = Path(temporary.name) / "setup.txt"
        self.args = argparse.Namespace(
            repo=["fixture/Longshot"], skip_tag="v1.1.0", out=self.output,
            bootstrap_url=BOOTSTRAP, bootstrap_sha256=hashlib.sha256(MANIFEST).hexdigest(),
        )

    def test_release_without_a_setup_list_uses_bootstrap(self):
        releases = [{"tag_name": "v1.0.0", "draft": False, "prerelease": False, "assets": []}]
        with patch.object(release_parts, "get", side_effect=[json.dumps(releases).encode(), MANIFEST]) as download:
            self.assertEqual(release_parts.previous(self.args), 0)
        self.assertEqual(download.call_args_list[0].args, (API,))
        download.assert_called_with(BOOTSTRAP)
        self.assertEqual(self.output.read_bytes(), MANIFEST)

    def test_hash_mismatch_keeps_existing_output(self):
        self.output.write_bytes(b"existing manifest")
        with patch.object(release_parts, "get", side_effect=[b"[]", b"mismatched manifest"]):
            self.assertEqual(release_parts.previous(self.args), 1)
        self.assertEqual(self.output.read_bytes(), b"existing manifest")

    def test_uses_longshot_stable_release_first(self):
        releases = [
            {"tag_name": "v1.1.0", "draft": False, "prerelease": False, "assets": [
                {"name": "hookshot-setup.txt", "browser_download_url": "https://example.invalid/same-tag.txt"}]},
            {"tag_name": "v1.0.0", "draft": False, "prerelease": False, "assets": [
                {"name": "hookshot-setup.txt", "browser_download_url": STABLE}]},
        ]
        with patch.object(release_parts, "get", side_effect=[json.dumps(releases).encode(), b"tag v1.0.0\n"]) as download:
            self.assertEqual(release_parts.previous(self.args), 0)
        download.assert_called_with(STABLE)
        self.assertEqual(self.output.read_bytes(), b"tag v1.0.0\n")


if __name__ == "__main__":
    unittest.main()
