"""Export or restore an Ocarina of Time remake 1.1.1 save ZIP.

SPDX-License-Identifier: GPL-3.0-or-later
Based on the portable save exchange in BaileyPillon/Longshot.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import tempfile
import zipfile
import zlib


TITLE = "0400486024dec000"
DEFAULT_ACCOUNT = "5a3c9e1f0b7d24680000000000000001"
ACCOUNT_FILES = {"account.sav": 128 * 1024, "option.sav": 256 * 1024}
SLOT_FILES = {"caption.sav": 512 * 1024, "progress.sav": 1024 * 1024}
ACCOUNT = re.compile(r"[0-9a-f]{32}")
SLOT = re.compile(r"slot_(?:0[0-9]|1[01])")
MAX_MANIFEST = 16 * 1024
MAX_MEMBERS = 128
# Leave space for optional NX2navi bug-report attachments. They are never read.
MAX_ZIP = 256 * 1024 * 1024


def save_limit(name):
    if name in ACCOUNT_FILES:
        return ACCOUNT_FILES[name]
    folder, _, filename = name.partition("/")
    if SLOT.fullmatch(folder):
        return SLOT_FILES.get(filename, 0)
    return 0


def hashes(data):
    return {name: hashlib.sha256(content).hexdigest() for name, content in data.items()}


def read_package(path):
    path = plain_path(path)
    with path.open("rb") as stream:
        if os.fstat(stream.fileno()).st_size > MAX_ZIP:
            raise ValueError("Save package is too large")
        try:
            with zipfile.ZipFile(stream) as archive:
                return read_members(archive)
        except (zipfile.BadZipFile, RuntimeError, NotImplementedError, UnicodeError, EOFError, zlib.error) as error:
            raise ValueError("Could not read this save ZIP") from error


def read_members(archive):
    entries = archive.infolist()
    if len(entries) > MAX_MEMBERS:
        raise ValueError("Too many entries in save ZIP")
    seen, data, manifest = set(), {}, None
    for entry in entries:
        name = entry.filename
        parts = name.rstrip("/").split("/")
        if (name != entry.orig_filename or name.casefold() in seen or "\\" in name or ":" in name
                or any(part in ("", ".", "..") for part in parts)
                or stat.S_ISLNK(entry.external_attr >> 16) or entry.flag_bits & 1):
            raise ValueError("Invalid or duplicate entry in save ZIP")
        seen.add(name.casefold())
        if name == "manifest.json" and 0 < entry.file_size <= MAX_MANIFEST:
            manifest = json.loads(archive.read(entry))
        elif name.startswith("bug-report/"):
            continue
        elif entry.is_dir() and (name == "save/" or len(parts) == 2 and parts[0] == "save" and SLOT.fullmatch(parts[1])):
            continue
        elif name.startswith("save/") and 0 < entry.file_size <= save_limit(name[5:]):
            content = archive.read(entry)
            if len(content) != entry.file_size:
                raise ValueError(f"Incorrect save ZIP entry size: {name}")
            data[name[5:]] = content
        else:
            raise ValueError(f"Unexpected or oversized save ZIP entry: {name}")

    if (not isinstance(manifest, dict) or manifest.get("format") != "nx2navi-save"
            or type(manifest.get("format_version")) is not int or manifest["format_version"] != 1
            or str(manifest.get("title_id", "")).lower() != TITLE or manifest.get("game_version") != "1.1.1"
            or type(manifest.get("files")) is not int or manifest["files"] != len(data)):
        raise ValueError("Choose an Ocarina of Time remake 1.1.1 save ZIP")
    slots = sorted({name.split("/")[0] for name in data if "/" in name})
    if "account.sav" not in data or not slots or any(
        f"{slot}/{filename}" not in data for slot in slots for filename in SLOT_FILES
    ):
        raise ValueError("Save ZIP must contain an account and complete save slots")
    if "sha256" in manifest and manifest["sha256"] != hashes(data):
        raise ValueError("Save ZIP checksum mismatch")
    account = manifest.get("longshot_account", DEFAULT_ACCOUNT)
    if not isinstance(account, str) or not ACCOUNT.fullmatch(account):
        raise ValueError("Invalid save account identifier")
    return data, slots, account



def plain_path(path, *, directory=False):
    """Reject links and Windows junctions in the path or its ancestors."""
    path = Path(os.path.abspath(path))
    for part in reversed((path, *path.parents)):
        try:
            info = part.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError(f"Choose a path without links: {part}")
        kind = stat.S_ISREG if part == path and not directory else stat.S_ISDIR
        if not kind(info.st_mode):
            raise ValueError(f"Unexpected file type: {part}")
    return path


def stamp(info):
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns


def read_account(source):
    source = plain_path(source, directory=True)
    slots = sorted(item.name for item in source.iterdir() if SLOT.fullmatch(item.name))
    if not slots:
        raise ValueError("Choose the save account folder with account.sav and at least one complete slot")
    names = ["account.sav"]
    if (source / "option.sav").exists() or (source / "option.sav").is_symlink():
        names.append("option.sav")
    names.extend(f"{slot}/{name}" for slot in slots for name in SLOT_FILES)
    data, stamps = {}, {}
    for name in names:
        path = plain_path(source / name)
        original = path.stat()
        limit = save_limit(name)
        if not 0 < original.st_size <= limit:
            raise ValueError(f"Empty or oversized save file: {name}")
        stamps[path] = stamp(original)
        with path.open("rb") as stream:
            if stamp(os.fstat(stream.fileno())) != stamps[path]:
                raise ValueError("A save changed while reading. Close the game and retry.")
            data[name] = stream.read(limit + 1)
        if len(data[name]) != original.st_size:
            raise ValueError("A save changed while reading. Close the game and retry.")
    if any(stamp(path.stat()) != previous for path, previous in stamps.items()):
        raise ValueError("A save changed while reading. Close the game and retry.")
    return data, slots


def export_package(source, output):
    source = plain_path(source, directory=True)
    output = plain_path(output)
    if output.suffix.lower() != ".zip":
        raise ValueError("Choose a ZIP filename")
    if output.exists():
        raise FileExistsError(f"Choose a new ZIP filename: {output}")
    data, slots = read_account(source)
    account = source.name.lower()
    if not ACCOUNT.fullmatch(account):
        account = DEFAULT_ACCOUNT
    manifest = dict(format="nx2navi-save", format_version=1, title_id=TITLE, game_version="1.1.1",
                    game="The Legend of Zelda: Ocarina of Time", files=len(data), nx2navi="",
                    exported=datetime.now(timezone.utc).isoformat(timespec="seconds"), exported_by="Longshot",
                    bug_report=False, longshot_account=account, sha256=hashes(data))
    with tempfile.TemporaryDirectory(prefix=".longshot-export-", dir=output.parent) as temporary:
        staged = Path(temporary) / "save.zip"
        with staged.open("xb") as stream:
            with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("manifest.json", json.dumps(manifest, indent=2))
                for name, content in data.items():
                    archive.writestr("save/" + name, content)
            stream.flush()
            os.fsync(stream.fileno())
        if read_package(staged) != (data, slots, account):
            raise ValueError("The exported archive did not verify")
        # The temporary file lives on the same volume. Neither operation replaces a competing export.
        if os.name == "nt":
            staged.rename(output)
        else:
            os.link(staged, output)
    return slots


def restore_package(archive, user):
    data, slots, _ = read_package(archive)
    user = plain_path(user, directory=True)
    user.mkdir()  # Reserve an entirely new user folder before writing anything.
    try:
        with tempfile.TemporaryDirectory(prefix=".longshot-restore-", dir=user) as temporary:
            save = Path(temporary) / "save"
            account = save / DEFAULT_ACCOUNT
            for name, content in data.items():
                target = account / name
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open("xb") as stream:
                    stream.write(content)
                    stream.flush()
                    os.fsync(stream.fileno())
            if read_account(account) != (data, slots):
                raise ValueError("The staged save did not verify")
            save.rename(user / "save")
    except BaseException:
        if not any(user.iterdir()):
            user.rmdir()
        raise
    return slots


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    export = commands.add_parser("export", help="package a native save account from a closed game")
    export.add_argument("account", type=Path)
    export.add_argument("archive", type=Path)
    restore = commands.add_parser("restore", help="unpack saves into a new --user directory")
    restore.add_argument("archive", type=Path)
    restore.add_argument("user", type=Path)
    args = parser.parse_args()
    try:
        slots = (export_package(args.account, args.archive) if args.command == "export"
                 else restore_package(args.archive, args.user))
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print("Saved " + ", ".join(slots))


if __name__ == "__main__":
    main()
