"""gpu/vk/vk_jobs.cpp's jobs_wait runs a waited-for job that no worker has taken, no game or GPU required.

python -I -B tests/test_jobs_wait.py [--cxx clang++]

vk_jobs.cpp is built beside stand-ins for vk_internal.h (its log) and vk_draw.h (ub_thread_off), with tests/test_jobs_wait.cpp.
"""
import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STUB = """#pragma once
#ifdef _WIN32
#include <windows.h>
#else
#define SetThreadPriority(t, p) ((void)0)
#define SetThreadDescription(t, d) ((void)0)
#endif
namespace mkwvk {
inline void logf(const char*, ...) {}
}
"""


DRAW_STUB = """#pragma once
namespace mkwvk {
inline void ub_thread_off() {}
}
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cxx", default="clang++")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="jobs-wait-") as tmp:
        tmp = Path(tmp)
        shutil.copy(ROOT / "gpu" / "vk" / "vk_jobs.cpp", tmp / "vk_jobs.cpp")
        shutil.copy(ROOT / "gpu" / "vk" / "vk_jobs.h", tmp / "vk_jobs.h")
        (tmp / "vk_internal.h").write_text(STUB, encoding="utf-8")
        (tmp / "vk_draw.h").write_text(DRAW_STUB, encoding="utf-8")
        exe = tmp / "jobs-wait.exe"
        subprocess.run([args.cxx, "-std=c++20", "-O1", "-I", str(tmp), str(tmp / "vk_jobs.cpp"),
                        str(ROOT / "tests" / "test_jobs_wait.cpp"), "-o", str(exe), "-pthread"], check=True)
        subprocess.run([str(exe)], check=True, timeout=60)


if __name__ == "__main__":
    main()
