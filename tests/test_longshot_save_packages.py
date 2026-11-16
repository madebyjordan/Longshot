"""Exercise save ZIP exchange with authored bytes and disposable user folders."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import longshot_save_packages as packages


class SavePackageTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / ("b" * 32)
        self.archive = self.root / "save.zip"
        self.user = self.root / "user"
        self.data = {"account.sav": b"account", "option.sav": b"option",
                     "slot_03/caption.sav": b"caption", "slot_03/progress.sav": b"progress"}
        for name, content in self.data.items():
            file = self.source / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(content)

    def nx2navi(self, *, extra=None, **overrides):
        manifest = dict(format="nx2navi-save", format_version=1, title_id=packages.TITLE,
                        game_version="1.1.1", files=len(self.data))
        manifest.update(overrides)
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr("manifest.json", json.dumps(manifest))
            for name, content in self.data.items():
                archive.writestr("save/" + name, content)
            if extra:
                archive.writestr(extra, b"metadata")

    def test_round_trip_preserves_source_and_refuses_replacement(self):
        self.assertEqual(packages.export_package(self.source, self.archive), ["slot_03"])
        self.assertEqual(packages.read_package(self.archive), (self.data, ["slot_03"], self.source.name))
        self.assertEqual(packages.restore_package(self.archive, self.user), ["slot_03"])
        target = self.user / "save" / packages.DEFAULT_ACCOUNT
        for name, content in self.data.items():
            self.assertEqual((target / name).read_bytes(), content)
            self.assertEqual((self.source / name).read_bytes(), content)
        with self.assertRaises(FileExistsError):
            packages.export_package(self.source, self.archive)
        with self.assertRaises(FileExistsError):
            packages.restore_package(self.archive, self.user)
        (target / "account.sav").write_bytes(b"local changes")
        with self.assertRaises(FileExistsError):
            packages.restore_package(self.archive, self.user)
        self.assertEqual((target / "account.sav").read_bytes(), b"local changes")

    def test_nx2navi_restore_ignores_diagnostics(self):
        self.nx2navi(extra="bug-report/nx2navi.ini")
        self.assertEqual(packages.restore_package(self.archive, self.user), ["slot_03"])
        self.assertEqual(list(self.user.rglob("*.ini")), [])
        self.assertEqual((self.user / "save" / packages.DEFAULT_ACCOUNT / "account.sav").read_bytes(), b"account")

    def test_rejected_archives_do_not_create_user_folder(self):
        for case in ({"extra": "save/../account.sav"}, {"extra": "save/ACCOUNT.SAV"},
                     {"sha256": {}}, {"game_version": "1.0.0"}, {"title_id": "b" * 16}):
            with self.subTest(case=case):
                self.nx2navi(**case)
                with self.assertRaises(ValueError):
                    packages.restore_package(self.archive, self.user)
                self.assertFalse(self.user.exists())
        del self.data["slot_03/caption.sav"]
        self.nx2navi()
        with self.assertRaises(ValueError):
            packages.restore_package(self.archive, self.user)
        self.assertFalse(self.user.exists())

    def test_publication_failure_removes_empty_reservation(self):
        self.nx2navi()
        with patch.object(Path, "rename", side_effect=OSError("simulated publication error")):
            with self.assertRaises(OSError):
                packages.restore_package(self.archive, self.user)
        self.assertFalse(self.user.exists())


if __name__ == "__main__":
    unittest.main()
