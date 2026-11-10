"""RX_VK_CACHE_DIR and RX_VK_CACHE_READONLY (gpu/vk/vk_paths.h): where the renderer keeps the caches it writes, and a run
that only reads them.

A test run could start cold, or from a cache of its own, only by copying the game folder or moving its 6.7 GB program cache
aside, and a test build with another translator stamp started that cache again. cache_path and cache_readonly are compiled
here on their own and run with the switches set and unset; the cache files are checked to go through cache_path, and each
writer to stop when read-only.
python -I -B tests/test_cache_dir.py
"""
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VK = ROOT / "gpu" / "vk"
CACHES = ("hookshot_programs.bin", "mkw_vk_pipelines.bin", "hookshot_pipelines.bin", "hookshot_prepared.bin",
          "hookshot_pipeline_states.bin")


def block(text, marker):
    start = text.index(marker)
    end = text.index("{", start) + 1
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


FIXTURE = r"""
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
typedef unsigned long DWORD;
#define MAX_PATH 260
static DWORD game_module_path(char* buf, DWORD size) {   // (the game folder's exe, as vk_paths.h answers it)
    const char* p = "C:\\Games\\Longshot\\hookshot.exe";
    std::strncpy(buf, p, size);
    return (DWORD)std::strlen(p);
}
CACHE_PATH
CACHE_READONLY
int main() {
    std::printf("%s|%d\n", cache_path("hookshot_programs.bin").c_str(), (int)cache_readonly());
}
"""


class CacheDirTests(unittest.TestCase):
    def test_switches(self):
        compiler = shutil.which("g++") or shutil.which("clang++")
        if not compiler:
            self.skipTest("no C++ compiler")
        paths = (VK / "vk_paths.h").read_text(encoding="utf-8")
        src = FIXTURE.replace("CACHE_PATH", block(paths, "inline std::string cache_path(")) \
                     .replace("CACHE_READONLY", block(paths, "inline bool cache_readonly(") + ";")
        with tempfile.TemporaryDirectory(prefix="hookshot-cache-dir-") as tmp:
            cpp, exe = Path(tmp) / "t.cpp", Path(tmp) / "t.exe"
            cpp.write_text(src, encoding="utf-8")
            build = subprocess.run([compiler, "-std=c++20", "-Wall", str(cpp), "-o", str(exe)], text=True, capture_output=True)
            self.assertEqual(build.returncode, 0, build.stderr)

            def run(**env):
                e = {k: v for k, v in os.environ.items() if k not in ("RX_VK_CACHE_DIR", "RX_VK_CACHE_READONLY")}
                e.update(env)
                out = subprocess.run([str(exe)], text=True, capture_output=True, env=e)
                self.assertEqual(out.returncode, 0, out.stderr)
                path, ro = out.stdout.strip().split("|")
                return path, ro == "1"

            # (off: the game folder, written as before)
            self.assertEqual(run(), ("C:\\Games\\Longshot\\hookshot_programs.bin", False))
            self.assertEqual(run(RX_VK_CACHE_DIR=""), ("C:\\Games\\Longshot\\hookshot_programs.bin", False))
            # (a folder, with or without its last separator)
            self.assertEqual(run(RX_VK_CACHE_DIR="D:\\runs\\a")[0], "D:\\runs\\a/hookshot_programs.bin")
            self.assertEqual(run(RX_VK_CACHE_DIR="D:\\runs\\a\\")[0], "D:\\runs\\a\\hookshot_programs.bin")
            self.assertEqual(run(RX_VK_CACHE_DIR="/tmp/run/")[0], "/tmp/run/hookshot_programs.bin")
            # (read-only: set and not 0)
            self.assertTrue(run(RX_VK_CACHE_READONLY="1")[1])
            self.assertFalse(run(RX_VK_CACHE_READONLY="0")[1])
            self.assertFalse(run(RX_VK_CACHE_READONLY="")[1])

    def test_caches_go_through_cache_path(self):
        for name in ("vk_draw.cpp", "vk_shader.cpp"):
            text = (VK / name).read_text(encoding="utf-8")
            for cache in CACHES:
                for m in re.finditer(r'"%s"' % re.escape(cache), text):
                    line = text[text.rfind("\n", 0, m.start()) + 1:text.find("\n", m.end())]
                    self.assertIn('cache_path("%s")' % cache, line, "%s: %s outside cache_path" % (name, cache))

    def test_writers_stop_when_read_only(self):
        draw = (VK / "vk_draw.cpp").read_text(encoding="utf-8")
        shader = (VK / "vk_shader.cpp").read_text(encoding="utf-8")
        # (the program cache: opened for reading only, nothing added, cut or started again)
        init = block(shader, "void disk_init(")
        self.assertIn("if (ro) {", init)
        self.assertLess(init.index("if (ro) {"), init.index('"r+b"'))
        self.assertLess(init.index("if (ro) {"), init.index('"wb"'))
        self.assertIn("!disk_out", block(shader, "void program_cache_save("))
        # (the pipeline cache and the notes)
        self.assertIn("cache_readonly()", block(draw, "void pipeline_cache_save(").split("\n")[1])
        self.assertIn("cache_readonly()", block(draw, "void pipeline_note("))
        self.assertIn("cache_readonly()", block(draw, "void state_record(const Program& p, const NvnState& s, const Targets& at, VkPrimitiveTopology topo, const uint8_t color_types[8]) {"))
        # (Hookshot's play files, kept beside the pipeline cache: none written, and at the start none removed or written
        # into the one file)
        self.assertIn('pcache_path = cache_path("mkw_vk_pipelines.bin")', block(draw, "void pcache_init("))
        self.assertIn("if (cache_readonly()) return;", block(draw, "void play_cache_save("))
        merge = block(draw, "size_t play_files_merge(")
        self.assertIn("if (!cache_readonly()) remove(path.c_str());", merge)
        self.assertIn("merged.size() >= kPlayFilesKept && !cache_readonly()", merge)
        # (the prepare's file and marker)
        prep = block(draw, "bool prepare_run(")
        self.assertIn("cache_readonly()", block(draw, "void prepared_write(").split("\n")[1])
        self.assertIn("&& !ro", prep)
        # (the precompile's marker, and the notes it drops after two failed starts)
        pre = block(draw, "void precompile_run(")
        self.assertIn('ro ? nullptr : fopen(running.c_str(), "rb")', pre)
        self.assertIn('ro ? nullptr : fopen(running.c_str(), "wb")', pre)
        self.assertIn("done_marker{running, !ro}", pre)


if __name__ == "__main__":
    raise SystemExit(unittest.main(verbosity=1))
