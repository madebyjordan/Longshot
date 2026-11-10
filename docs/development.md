# Hookshot (oot-recomp)

**Hookshot** is the port's name (as Skybound is TOTKPort's and BlueShell is mkw-recomp's); the repository folder
stays `oot-recomp`. The game builds as `hookshot.exe` + `hookshot_vk.dll`.

A static recompilation of the Switch 2 *The Legend of Zelda: Ocarina of Time* remake to native x86-64 Windows:
the game's code lifted to C (NotRexed's a64-kit, Rexcomp's AArch64 kit), the Horizon OS and NVN2 implemented
natively, the GPU work replayed on Vulkan. It is a fork of mkw-recomp (`..\mkw-recomp`, Mario Kart World), whose
paths it keeps so MKW's fixes apply here.

**No game files are in this repository.** The dump is read where it lies (`work/dump.txt`); everything made from it
(modules, image, lifted C, builds, runs) goes to `work/`, which git ignores.

Status: the dump is read and lifted (100 % translated, the instruction test passes); the standalone build is next. Plan, stages and the lessons carried over from MKW: [DESIGN.md](DESIGN.md).

## Use

Requirements: Python 3 with `lz4`, `numpy`, `capstone`, `unicorn` (2.1), `zstandard`; emsdk's clang at `C:\emsdk` (or `EMSDK`);
for the Vulkan back end, Eden's CPM package cache (found in mkw-recomp's `work/eg` or `..\Eglantine\.cache\cpm`, or
`RX_CPM`).

Run everything with `python -I` from this folder.

| Command | What |
|---|---|
| `python -I nx2/nup.py <file.nup> --extract work/dump` | the `.nup` dump package to `work/dump/exefs` + `romfs` (newest title; ZBIC NSOs made plain by `nx2/unpack.py`) |
| `python -I nx2/dump.py --dump <folder with exefs/ and romfs/> --set` | what the dump is (Switch 1 or 2, ids, modules, SDK, NVN); `--set` records it in `work/dump.txt` |
| `python -I nx2/lift.py [--write]` | image + discovery + coverage (`work/image/coverage.txt`); `--write` emits the C to `work/gen` |
| `python -I nx2/census.py --nx2` | instruction census per module, after the Switch 2 forms |
| `python -I nx2/test_insn.py [--per 6 --states 12]` | every instruction form the game uses, Unicorn vs lifted C |
| `python -I nx2/test_leaf.py [--count 3000 --states 4] [--calls]` | random whole functions (or call trees), Unicorn vs recompiled native code on the real image |
| `python -I nx2/lift.py --standalone --write` then `python -I nx2/build.py --standalone --jobs 4` | the standalone game: `work/sa/build/hookshot.exe` + `hookshot_vk.dll` |
| `powershell -File scripts/run-vk.ps1 [-Window] [-Seconds 120] [-Name x]` | a muted test run on Vulkan, with screenshots in `work/runs/<name>/shots` |
| `powershell -File scripts/test-run.ps1 -Game <game folder> [-Renderer <dll>] [-Cache cold\|game\|<folder>]` | a test run on the game folder you play, without copying it or changing it (see below) |
| `powershell -File scripts/build-vk.ps1` | the renderer alone, in `%LOCALAPPDATA%\LongshotDev` (see below) |
| `python -I scripts/prof.py <stacks file>` | a profile sampled with `HOS_SAMPLE=<thread>` + `HOS_SAMPLE_FILE=<stacks file>`, named from the linker maps |

The dump can also come from `OOT_DUMP` (its folder) or `OOT_EXEFS`; `OOT_WORK` moves the work folder.

The built game's own arguments (`image.bin`, `--romfs`, `--user`, `--trace`, `--prepare-shaders`) are in the README's
[Running the game from the command line](../readme.md#running-the-game-from-the-command-line).

### The release and dev game folders

`scripts/build-game.ps1 -Channel release` builds `Longshot\game` with the installed Longshot Builder's own tools, as its
Update does: the published release. `-Channel dev` builds `Longshot\game-dev` from `Longshot\worktrees\dev` (the dev
branch as checked out), with AMD FSR 4. They share the game files (hard links) and keep their own caches, saves and
settings, so switching costs nothing. The window's title, the start-up screen, the Insert menu and the log's first line
say which one runs: a release its tag (`v1.0.0`), any other build its branch and commit (`dev 86148ce`).

### Test runs without another game folder

A new game folder costs 24 GB (the romfs, unless hard-linked) and a played one another 7 GB of caches; none is needed to
test a build. `scripts/test-run.ps1` makes a run folder beside the game folder out of hard links (the exe, `image.bin`,
the shader list), copies in the renderer under test (`-Renderer`, 6 MB) and the saves (1 MB), passes the game's romfs with
`--romfs`, and deletes the run folder afterwards. The game folder is not written. The log, a summary and any screenshots
stay in `runs\<time>-<name>`.

- `-Cache cold` (the default): a first start, caches empty in the run folder.
- `-Cache game`: the game's own caches, read-only (`RX_VK_CACHE_DIR` + `RX_VK_CACHE_READONLY`). A warm start that cannot
  change them, and a test build whose translator stamp differs cannot start the 6.7 GB program cache again. Renderers
  from before these switches start cold instead.
- `-Cache <folder>`: a warm cache of its own, read and written, kept for the next run.

For renderer work, build only the DLL and run it. `scripts/build-vk.ps1` builds it with the builder's compiler and
packages in a work folder of its own (`%LOCALAPPDATA%\LongshotDev`). Not in the builder's: a checkout's build there made
the builder's next build throw away every compiled object.

```powershell
powershell -File scripts/build-vk.ps1
powershell -File scripts/test-run.ps1 -Game <game folder> -Renderer "$env:LOCALAPPDATA\LongshotDev\sa\build\hookshot_vk.dll" -Cache game
```

Switches for one experiment go in with `-Env "NAME=1"` instead of a build of their own. A fully cold first start (AMD's
driver cache moved aside too) is still `bench\bench_prepare.ps1`'s.

Scripted test input (`HOS_INPUT_SCRIPT` and `HOS_INPUT_LIVE`) is applied before the window-focus check. A log saying
the game window is not in front does not mean those presses were discarded. `HOS_HEADLESS` and
`HOS_WINDOW_NOACTIVATE` change window creation, not this input routing. The game must still reach a state that reads
and responds to the supplied controller input.

Physical keyboard and mouse input stays with the foreground window. Physical controllers can also play in the
background when the existing "Controller in background" option or `HOS_INPUT_BACKGROUND=1` enables them;
`HOS_INPUT_BACKGROUND=0` overrides that option. `HOS_INPUT_EXCLUSIVE=1` controls merging the player's devices while
the script's existing exclusive interval is active; it is not a focus override.

## Layout

| Path | What |
|---|---|
| `DESIGN.md` | why mkw-recomp's pipeline, the architecture, MKW's lessons as rules, stages, testing |
| `nx2/` | the lift pipeline and its tests (mkw-recomp's, made game-neutral; `kit.py` has the paths) |
| `host/` | the standalone Horizon runtime (TOTKPort's `hos/` as adapted by mkw-recomp), with the native NVN2 layer `nvn_native.c` |
| `gpu/vk/` | the Vulkan back end (`hookshot_vk.dll`), with Eden's shader recompiler and sirit |
| `gpu/render/`, `gpu/third_party/` | the WebGPU renderer the exe falls back to, its op stream (`rx_nvn.h`), suyu's decoders |
| `scripts/` | test runs and the profile reader |
| `third_party/` | a64-kit (unmodified), libopus, zbic (Atmosphere's zstd with ZBIC, for unpacking only) |

License: GPL-3.0-or-later, as mkw-recomp: a64-kit's translator is suyu's (GPL-2.0-or-later) and Eden's code is
GPL-3.0.
