# Hookshot (oot-recomp)

**Hookshot** is the port's name (as Skybound is TOTKPort's and BlueShell is mkw-recomp's); the maintainer's repository
folder stays `oot-recomp`. The game builds as `hookshot.exe` + `hookshot_vk.dll` (`hookshot` + `hookshot_vk.so` on Linux).

A static recompilation of the Switch 2 *The Legend of Zelda: Ocarina of Time* remake to native x86-64 Windows and Linux:
the game's code lifted to C (NotRexed's a64-kit, Rexcomp's AArch64 kit), the Horizon OS and NVN2 implemented
natively, the GPU work replayed on Vulkan. It is a fork of mkw-recomp (`..\mkw-recomp`, Mario Kart World), whose
paths it keeps so MKW's fixes apply here.

**No game files are in this repository.** The dump is read where it lies (`work/dump.txt`); everything made from it
(modules, image, lifted C, builds, runs) goes to `work/`, which git ignores.

Status: the game plays; the README's [status table](../readme.md#project-status) says how far. The design, its stages and
the lessons carried over from MKW: [design.md](design.md).

## Use

Requirements: Python 3 with `pip install -r requirements.txt` (`lz4`, `numpy`, `capstone`, `unicorn` 2.1,
`zstandard`, and the button-prompt packages); a compiler: on Windows llvm-mingw
(`HOOKSHOT_MINGW`, or an `llvm-mingw` folder in the clone), else emsdk's clang at `C:\emsdk` (or `EMSDK`) with Visual
Studio's C++ tools; on Linux clang and lld. For the Vulkan back end: distro Vulkan / SPIR-V / VMA headers under
`/usr`, or `HOOKSHOT_VULKAN_HEADERS` / `HOOKSHOT_SPIRV_HEADERS` / `HOOKSHOT_VMA_HEADERS`, or a CPM package cache
(`RX_CPM`, else `cpm` in the clone, else the old caches in mkw-recomp's `work/eg` or `..\Eglantine\.cache\cpm`).
Without those the standalone build stops. [CONTRIBUTING.md](../CONTRIBUTING.md#development-setup) has the rest.

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
| `powershell -File scripts/bench.ps1 [-Name x] [-Against <run>]` | a benchmark: a test run with no window on a pinned save, and its numbers as a table (see below) |
| `python -I scripts/benchlog.py <log or run folder> [<another>]` | a run's numbers from its log, or two runs side by side |
| `python -I scripts/prof.py <stacks file>` | a profile sampled with `HOS_PROFILE=<seconds>` + `HOS_SAMPLE=<thread>` + `HOS_SAMPLE_FILE=<stacks file>` (the sampler runs only with `HOS_PROFILE` set), named from the linker maps |

The dump can also come from `OOT_DUMP` (its folder) or `OOT_EXEFS`; `OOT_WORK` moves the work folder.

The built game's own arguments (`image.bin`, `--romfs`, `--user`, `--trace`, `--prepare-shaders`) are in the README's
[Running the game from the command line](../readme.md#running-the-game-from-the-command-line).

### The release and dev game folders

`scripts/build-game.ps1 -Channel release` builds `Longshot\game` with the installed Longshot Builder's own tools, as its
Update does: the published release. `-Channel dev` builds `Longshot\game-dev` from `Longshot\worktrees\dev` (the dev
branch as checked out), with AMD FSR 4. They share the game files (hard links) and keep their own caches, saves and
settings, so switching costs nothing. The window's title, the start-up screen, the Insert menu and the log's first line
say which one runs: a release its tag (`v1.0.0`), a dev build the release it is heading for and its commit (`pre-1.1.0 6353e9b`), any other its branch and commit.

Opening `Longshot.exe` in `game` shows the launcher (the Longshot Builder in launch mode, `builder/HookshotBuilder/Launcher.cs`):
one button that plays the version picked, or updates or builds the folder to it first, and **All Versions**, a bar with three
lists: **Releases** (the two newest, the rest folded into one row), **Pre-releases** (while one is ahead of the latest
release) and **Dev builds** (every dev build in a folder beside `game`: its `hookshot_build.txt` says `channel=dev`;
`build-game.ps1` writes it). A list with nothing in it is not shown. A version with no mark is on this PC, one with the
arrow is brought by the button (the folder holds one release at a time: it is replaced), a dim one has no builder to
download. Which list a version is in and its mark are decided in `builder/HookshotBuilder/LaunchList.cs`, checked without a
window by `dotnet run --project tests/builder_launcher/BuilderLauncherTests.csproj`. The pick is kept per folder (`%LOCALAPPDATA%\LongshotBuilder\launcher.json`). It is drawn in
Paper (the file Longshot) and built from that drawing. The button's word is in Marcellus (OFL, in the app); a font file of
your own in `%LOCALAPPDATA%\LongshotBuilder\fonts` is used instead. A commercial font such as FOT-Chiaro is never added to
the repository or the app: its license does not allow passing it on.

The launcher is the one window a player sees. The Longshot Builder opened with nothing asked is the launcher of the game it
built last, or of one whose own launcher was played from on this PC (its own pages when there is none yet; from the launcher, **Build from game files** at the foot of the bar opens
them in the same window, with the way back in their head). When the button brings another version, the window stays and
hidden programs do the work, each a program of its own: the setup in its engine mode (`LongshotBuilder-Setup.exe --engine
--tag <tag>`, `builder/setup/setup.c`) installs that release's builder beside the one in use, without making it the one in
use or removing any (a part the two share is linked, file by file, not downloaded), and then that version's own
`make_game.py --update` builds the folder. What they say on stdout (`@@setup`, `@@percent`, `@@stage`, `@@progress`, ...) is
read into the strip that fills in the button's place and the two lines under it by `builder/HookshotBuilder/LaunchWork.cs`,
which moves nothing on a line it does not know: a version's tools may be older or newer than the launcher. The shaders' step
at the end has its own share there, and "Prepare shaders now" unticked reaches only tools that know `--no-prepare-shaders`
(read from that version's `make_game.py`; an older one would stop on the argument). A release ahead of
the builder in use makes its builder the one in use from the next start (`versions\current.txt`); a pre-release or an older
release never does, so going back to one does not take the launcher back. After the build the builders no longer in use go,
as the build files do but the compiled objects (`Storage.Free(afterBuild: true)`).

`powershell -File tests/builder_launcher/rig/run.ps1` tries all of it without a game, a release or GitHub: made-up releases
handed out by a web server on this PC, a scratch install (`HOOKSHOT_SETUP_BASE`, which the builder honours as the setup does)
and a stand-in for a version's Python that says what `make_game.py` says (a setup list that `HOOKSHOT_SETUP_LIST` takes from
`http://127.0.0.1:` may name parts there; every other part link is https). It checks the setup's engine mode alone, then
presses the launcher's button for a pre-release, a release to go back to, a build that stops, one cancelled, a download that fails and
the latest release, and draws each to a PNG file.

To look at the launcher without a game: `HOOKSHOT_LATEST_TAG=v1.2.0-rc.1,v1.1.0,v1.0.0?` gives it those releases instead of
GitHub's (newest first; one with a part after its numbers is a pre-release, the first without is the latest, one ending in
`?` has no builder to download; `none`: no answer). `HOOKSHOT_UI_PREVIEW` takes any of `versions` (the bar open), `older`
(its older releases unfolded), `max` (the window maximised), `pick=release:v1.1.0` or `pick=dev:<folder>` (that version
picked), `go` (the button pressed), `cancel` (Cancel pressed when that is 40% done) and `pages` (Build from game files pressed), with
commas between them, and `HOOKSHOT_UI_SHOT=<file.png>` draws the window to that file and closes, away from
the screen.

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

`-Game` takes either folder: the builder's (`bin\hookshot.exe`) or a checkout's build such as `game-dev` (`hookshot.exe`
beside `image.bin`). `-User <folder>` and `-Options <file>` start the run from saves and settings of your choosing in
place of the game's. Started with `powershell -File`, `-Env "A=1","B=2"` arrives as one string; the script splits it at
each comma that a `NAME=` follows.

### Benchmarks

`scripts/bench.ps1` is a test run made repeatable, with its numbers read out of the log:

```powershell
powershell -File scripts/bench.ps1 -Name base
powershell -File scripts/bench.ps1 -Name try -Env "RX_VK_NO_WRITEBACK=1" -Against <Longshot>\runs\<time>-base
```

- **No window** (`HOS_HEADLESS`), muted, the game's own caches read-only: a warm start in about 10 s, and nothing in the game
  folder written. The picture is still rendered at the size the settings ask for (`output_res`, or `HOS_OUTPUT_RES`);
  only the last copy to the screen goes to a 1920 x 1080 image instead.
- **The same save and settings every run**: `<Longshot>\bench\pinned` (`user\` and `hookshot_options.ini`), made from the
  game folder's own the first time and again with `-Pin`. Save where you want to measure, then pin. The script presses A
  from 32 s to 56 s to get from the title into the newest save and then leaves the pad alone, so the view is the same
  every run; a save made facing something A acts on (a door, a person) may get one of those presses.
- **The table** (`scripts/benchlog.py`, also on any `hookshot_log.txt`): the frame rate in play (median, 10th and 90th
  percentile of the 120-present samples that draw a scene, the first four left out), the GPU's time a frame, the replay's
  time by part, the busiest threads (`HOS_PROFILE`), the present intervals, the virtual cores' waits, pipelines built and
  slow uploads in play, VRAM. `-Against` puts an earlier run beside it with the change. It is kept as `bench.txt` in the
  run's folder.
- `-Timing` adds the GPU's time by pass and program (`RX_VK_PASS_TIMING=1`). Its timestamps make the GPU time longer:
  compare a timed run only with another timed one.
- `-SampleAll` (test-run.ps1's too) samples every thread's stack every 2 ms from 60 s in, into `stacks_all.txt` in the
  run's folder, for `scripts/crit.py`, `scripts/vkprof.py` and `scripts/hotlist.py`. It costs a few percent of the frame
  rate and about 5 MB a second: not for a run whose frame rate is the result, and delete the file once read. Those
  scripts find the linker maps in `RX_MAPS`, this checkout's `work\sa\build`, or `%LOCALAPPDATA%\LongshotDev\sa\build`;
  they must be the maps of the build that ran.

Two runs of the same build on the same save agreed within 1% on the frame rate and the GPU's time and within 5% on the
replay's parts (one pair, Ryzen 5 5600 and RX 6700 XT, 2026-11-10). The virtual cores' wait counts and the present
intervals' tails differed by half or more: read those as orders of magnitude. Run the baseline twice on your own PC
before reading a small change as a gain.

Scripted test input (`HOS_INPUT_SCRIPT` and `HOS_INPUT_LIVE`) is applied before the window-focus check. A log saying
the game window is not in front does not mean those presses were discarded. `HOS_HEADLESS` and
`HOS_WINDOW_NOACTIVATE` change window creation, not this input routing. The game must still reach a state that reads
and responds to the supplied controller input.

Physical keyboard and mouse input stays with the foreground window. Physical controllers can also play in the
background when the existing "Controller in background" option or `HOS_INPUT_BACKGROUND=1` enables them;
`HOS_INPUT_BACKGROUND=0` overrides that option. With `HOS_INPUT_EXCLUSIVE` set (to any value, `0` too) the script
alone drives the game until 5 s after its last press; without it the player's devices add to the script's presses. It
is not a focus override.

## Switches for testing and debugging

The running game reads its switches from the environment or from `hookshot_env.txt` beside `image.bin` (one
`NAME=VALUE` a line, `#` for notes; `HOS_ENV_FILE=<path>` reads that file instead). The build reads its own from the
environment. The README has the ones players are asked to try (`RX_VK_ASTC_RGBA8`, `RX_VK_LOW_SPEC`,
`RX_VK_MOVIE_UPSCALE`, `RX_VK_SKIP_CODE`, `HOS_OOT_CHEATS`). There are several hundred more, most of them one-off
experiments: `grep -rn 'getenv("' host gpu/vk` lists them, and the comment where each is read says what it does.
"Set" below means any value, `0` included.

`RX_VK_MESH_NOCULL=<prefix,...>` turns off the culling in the task shaders whose native code key starts with one of
the prefixes. On a Steam Deck, `RX_VK_MESH_NOCULL=2c56dc1006942f74` avoids the same hang without leaving anything out:
that task shader walks a coarse depth grid cell by cell for every meshlet and ends with
`visible = (result & 1) | (c3[2320] & 4)`, bit 4 being the game's own "draw everything". The switch makes that answer
true, and dead code elimination removes the grid walk, so every meshlet is drawn and the rasterizer clips what is off
screen. It costs some GPU time on cards that cull quickly, so it is off unless set. The log says
"culling off, every meshlet drawn" for each task shader it changed.

### Running a test

| Switch | What it does |
|---|---|
| `HOS_HEADLESS` (set) | No window: the renderer draws offscreen, for screenshots and frame dumps |
| `HOS_WINDOW_SIZE=<w>x<h>` | The window's size in pixels (1280x720 by default, wider for an ultrawide picture) |
| `HOS_WINDOW_NOACTIVATE` (set) | The window opens behind the others and does not take the focus (Windows) |
| `HOS_FULLSCREEN=1` | Starts in fullscreen; the Insert menu's Fullscreen row is then left alone |
| `HOS_AUDIO=0` | No sound device is opened (the mixer still runs); `scripts/run-vk.ps1` sets it unless `-Sound` |
| `HOS_LANGUAGE=<code>` | The game's language for this run, a code from `language.txt` |
| `HOS_OUTPUT_RES=<0..3>`, `HOS_HANDHELD=1`, `HOS_ASPECT=<ratio>` | Over the options file: the output resolution (0 2048x1152, 1 2560x1440, 2 3840x2160, 3 1920x1080), the handheld profile, a picture wider than 16:9 (2.3889 for 3440x1440) |
| `HOS_DYNRES=0`, `HOS_REALTIME_STEP=0`, `HOS_TEXT_SPEED=<0..3>` | Over the options file: Resolution 100% (non-zero: Auto), Game speed Per frame (1: Real time), Text speed |
| `RX_VK_FRAMEGEN=0`, `RX_VK_FRAMEGEN_MULT=<2..8>`, `RX_VK_PACE=0`, `RX_VK_UPSCALE=<0..2>` | No frame generation whatever the menu says; over the options file: its multiplier, frame pacing, the upscaling filter |
| `RX_VK_DEVICE=<n>` | The graphics card listed as `vk: device <n>` in the log, over the menu's GPU device |
| `HOOKSHOT_NO_UPDATE_CHECK=1` | No look for a newer release at the start |
| `HOS_TRACE=1` | Every Horizon call logged, as `--trace` does |
| `RX_BACKEND=<name>` | Unset or `vulkan`: `hookshot_vk.dll`; any other value but `eden`: the WebGPU renderer, when the exe was built with one |

### Input for scripted runs

| Switch | What it does |
|---|---|
| `HOS_INPUT_SCRIPT=<presses>` | Presses timed from the game's first controller read |
| `HOS_INPUT_LIVE=<file>` | A file the test appends lines of presses to, read every 30 ms; each line's times count from when it is read. A line `!cheat <n>` sends a Cheats page action, `!synclog` logs the NVN syncs (`!dump`, `!shots`, `!rdoc` and `!flasharm` are the WebGPU renderer's) |
| `HOS_INPUT_FRAME_CLOCK=1` | The script's time runs at 60 game frames a second instead of the wall clock |
| `HOS_INPUT_EXCLUSIVE`, `HOS_INPUT_BACKGROUND`, `HOS_INPUT_SCRIPT_BACKGROUND` (set) | As above; the last lets every device count while the window is behind another one |
| `RX_VK_MENU_SCRIPT=<seconds>:<key>,...` | Timed Insert menu keys: `INS`, `UP`, `DOWN`, `LEFT`, `RIGHT`, `ESC`, `ENTER`, `TABL`, `TABR` |
| `RX_VK_UI_PROMPTS=pad` or `keys` | The menu's button prompts for a controller or the keys, whatever was used last (screenshots) |
| `RX_VK_UI_GAME_FONT=0` | The menu, the counter and the start-up screen in the included Libertinus, not the game's own font |

The presses are `<seconds>:<button>[+<button>...][/<ms>]`, separated by commas, for example `2:A,5.5:LUP/1500,9:ZL+R`.
The buttons are `A B X Y L R ZL ZR PLUS MINUS LS RS LEFT UP RIGHT DOWN`, and the sticks pushed to their edge `LUP
LDOWN LLEFT LRIGHT RUP RDOWN RLEFT RRIGHT`. A press is held for 200 ms unless `/<ms>` says otherwise. The names are
case-sensitive, and an unknown one is ignored.

### Pictures and frames

| Switch | What it does |
|---|---|
| `RX_VK_SHOTS=<dir>` | A PNG every `RX_VK_SHOT_EVERY` presents (300) as `vk_<present>.png`, at `RX_VK_SHOT_W` x `RX_VK_SHOT_H` (1280x720) |
| `RX_VK_FLASH=1` | The flash detector, with `RX_VK_SHOTS` and `RX_VK_SHOT_EVERY=1`: writes the frames around a flash and dumps the next one |
| `RX_VK_FRAMELOG=<file>` | Every frame shown, at 320x180, while `<file>.go` exists; `python -I scripts/framelog.py <file>` finds the flashes |
| `RX_VK_DUMP=<present>[+<count>][,...]` | Every step of the frame after that present as PNGs, in `dump_<present>` under `RX_VK_DUMP_DIR` (or `RX_VK_SHOTS`, or the current folder); `RX_VK_DUMP_STEPS=<lo>-<hi>` and `RX_VK_DUMP_PROGS=<key>,...` narrow it |
| `RX_VK_PASS_TRACE=1` | The passes of the last frames, logged at a flash or a dark frame (`RX_VK_PASS_TRACE_AT=<n>`: every n-th present) |
| `RX_VK_PROBE=1` or `2` | The centre texel after every pass: `probe_<frame>.txt` at a flash (1), or `probe_all_<n>.txt` at speed (2); `python -I scripts/probe.py` reads them |
| `RX_VK_DUMP_FAILED=<dir>` | The native code of every program that does not translate, for `nx2/lower_test.py` (game data: keep it local) |

### Time and profiles

| Switch | What it does |
|---|---|
| `HOS_PROFILE=<seconds>` | Every interval, the busiest threads in the log; it also starts the samplers below |
| `HOS_SAMPLE=<thread>` + `HOS_SAMPLE_FILE=<file>` | One thread's stacks, sampled every millisecond (needs `HOS_PROFILE`); `python -I scripts/prof.py <file>` reads them |
| `HOS_SAMPLE_ALL=<file>` | Every game and renderer thread every 2 ms while `<file>.go` exists (needs `HOS_PROFILE`); `scripts/crit.py`, `scripts/vkprof.py` and `scripts/hotlist.py` read it (on Linux with `RX_MAPS=<game folder>`: they name frames from its `hookshot` and `hookshot_vk.so`) |
| `HOS_THREAD_TIMES=<seconds>` | Every interval, the threads that used over 5% of a core |
| `HOS_FPS_LOG` (set), `HOS_PRESENT_TRACE=<csv>` | The game's frame limiter, VRR flags and nominal rate every 300 presents; a CSV line per game present |
| `RX_VK_FRAME_LOG=<csv>` | A CSV line per present: frame, seconds, frame time in ms |
| `RX_VK_PASS_TIMING=1` or `2` | GPU time per pass (2: per program too), logged every 600 presents |
| `RX_VK_SLOW_LOG=<ms>` | The replay's times for each frame that took longer than this |
| `HOS_PGO=<file>` | In a run of an `RX_PGO=gen` build: `<file>.reset` clears the counters, `<file>.dump` writes them to `<file>`. It is read before `hookshot_env.txt`, so set it in the environment |

### The GPU

| Switch | What it does |
|---|---|
| `RX_VK_VALIDATION` (set) | The Khronos validation layer |
| `RX_VK_CHECKPOINTS=0` | No NVIDIA checkpoints (on by default; after a lost device they name the draw it stopped at) |
| `RX_VK_HANG_DUMP=<file>` | Every thread's stack to the file once no frame was shown for `RX_VK_HANG_SEC` seconds (20) (Windows) |
| `RX_VK_NOOPT` (set) | Pipelines built without the driver's optimisations |
| `RX_VK_SKIP_PROG=<prefix>,...` | Leaves out the draws of these programs (default `53509044401d78d2`; `-` leaves out none) |
| `RX_VK_PIPE_SKIP=0` | A draw whose pipeline is not ready waits for it, instead of being left out when its targets hold an earlier frame |
| `RX_VK_PRECOMPILE=0` or `1` | The shader preparation before the title off or on (on in builds made with `RX_RELEASE=1`, as the builders' are) |
| `RX_VK_NO_PROGRAM_CACHE` (set) | Neither reads nor writes `hookshot_programs.bin` and the pipeline notes |
| `RX_VK_MIRROR=0`, `RX_VK_MIRROR_MB=<MB>` | No VRAM mirrors of the game's memory pools (on for discrete cards); their budget |
| `RX_VK_VRAM_MB=<MB>` | The texture cache's budget (default 70% of what the driver allows, at most 6 GB) |
| `RX_VK_NO_WRITEBACK` (set) | GPU writes to mirrored pools are not copied back to guest memory (an experiment, for A/B) |
| `RX_VK_WB_DIFF=0` | Write-backs copy every byte again instead of only what changed against a VRAM shadow (the diff is on by default; v2.0.9 and v2.0.10 had it off, issue 1161) |
| `RX_VK_DLSS=0/1/2`, `RX_VK_DLSS_PRESET=<letter>`, `RX_VK_DLSS_LOG=1`, `RX_VK_REFLEX`, `RX_VK_REFLEX_LOG=1`, `RX_VK_ANTILAG=0/1` | NVIDIA DLSS and Reflex, and AMD Anti-Lag 2, for one run (`=-` on Reflex or Anti-Lag 2: its extensions not even enabled): the readme's NVIDIA DLSS section. `RX_VK_DLSS_TRACE=<frames>` logs one frame's large draws every that many frames, `RX_VK_DLSS_DEBUG=motion` shows the motion vectors, `=alternate` the game's own upscale every other frame |
| `RX_VK_FSR4=0/1/2`, `RX_VK_FSR_DIR=<folder>` | AMD FSR 4 for one run (0 never, 1 FSR 4, 2 its Native AA), and another folder for AMD's DLLs (not from `hookshot_env.txt`): the readme's AMD FSR 4 section. `RX_VK_FSR4=1/2` and `RX_VK_DLSS=1/2` go over the Upscaler row, a saved FSR 3.1 too where the engine they ask for runs; `RX_VK_FSR=1` goes over them (one upscaler at a time). The DLSS trace and debug switches above apply to FSR 4 too, on the frame chain they share |
| `RX_VK_ASYNC=1` | The replay on a thread of its own instead of the game's submitting thread |
| `HOS_VCORES=0`, `HOS_CORES=<1..8>`, `HOS_RESERVE_CORE=0` | No virtual cores; the cores the game sees (6); no core kept for the replay (one is, with 8 or more logical processors) |

### Building

A build reads and writes tens of gigabytes. When the game folder or the build files folder is on a hard drive,
`make_game.py` warns that an SSD is much faster and builds anyway (the builders ask before they start).

| Switch | What it does |
|---|---|
| `HOOKSHOT_JOBS=<n>` (or `--jobs`) | Compilers at once (default one per CPU thread, each started when the free memory holds it; `HOOKSHOT_MEM_GATE=0` turns that check off) |
| `HOOKSHOT_LIFT_JOBS=<n>` | Processes that write the lifted C (default the cores, at most 16) |
| `HOOKSHOT_KEEP_BUILD_FILES=1` | Keeps the lifted C and the objects after a build, so that the next one compiles only what changed |
| `HOOKSHOT_MINGW=<dir>`, `HOOKSHOT_MSVC=1` | The llvm-mingw to build with; the Visual Studio route instead |
| `RX_CPM=<dir>`, `WGPU_NATIVE=<dir>` | The CPM cache (Vulkan and SPIR-V headers, VMA, FFmpeg) and wgpu-native |
| `RX_TARGET=windows` | On Linux: the Windows game, with llvm-mingw's Linux build in `HOOKSHOT_MINGW` |
| `RX_RELEASE=1` | A release build (`make_game.py` sets it): no console window, the shaders prepared before the title |
| `RX_PGO=gen` or `RX_PGO=<file>` | The hot code counting its branches for a profile, or built with one (`make_game.py` passes the shipped profile) |
| `RX_MARCH=<level>` | The CPU level to build for (default this PC's: `x86-64-v3`, else `x86-64-v2`, else `x86-64`) |
| `RX_VK_ONLY=1` | Only the Vulkan back end, with no lift |
| `OOT_WORK`, `OOT_DUMP`, `OOT_EXEFS`, `OOT_ROMFS` | The work folder and the dump's folders, over `work/dump.txt` |

## The options file

`hookshot_options.ini`, in the game folder, holds the Insert menu's rows as `<key>=<number>`: the row's name in lower
case with `_` for spaces (`frame_generation=2`, `invert_camera_x=1`), except Output resolution, which is `output_res`.
`gpu/vk/vk_menu.cpp` writes the whole file at every change (`save`), with `options_version=4`,
`microphone_device=<name>` when a microphone is chosen by name, and the controls (`pad_<button>` and `key_<button>`:
the README's [Controls](../readme.md#controls)). A value outside a row's range is clamped when it is read. Where the
number is not the obvious one:

| Key | Values |
|---|---|
| `quality_preset` | 0 Custom, 1 Minimum (4GB), 2 Low (6GB), 3 Balanced (8GB), 4 High (12GB), 5 Ultra (16GB+) |
| `gpu_device` | 0 Auto, n the n-th card with Vulkan 1.3 as the row lists them |
| `frame_generation` | 0 Off, 2 to 8 the multiplier |
| `resolution` | 0 Auto, 1 100%, 2 75%, 3 67%, 4 50% |
| `output_res` | 0 2048x1152, 1 2560x1440, 2 3840x2160, 3 1920x1080, 4 5120x1440, 5 5120x2880 |
| `upscaling` | 0 Off, 1 Catmull-Rom, 2 Lanczos |
| `sharpness`, `rumble_strength`, `motion_sensitivity_x`, `motion_sensitivity_y` | percent |
| `mouse_sensitivity` | hundredths: 100 is 1.00 |
| `robust_buffers` | 0 Auto (on), 1 Off, 2 On |
| `game_speed` | 0 Per frame, 1 Real time |
| `text_speed` | 0 Normal, 1 2x, 2 4x, 3 Instant |
| `microphone` | 0 Off, 1 Default, 2 and up a microphone (the name in `microphone_device` decides which) |
| `mouse_camera` | 0 As the stick, 1 Native |
| `layout_preset` | 0 Nintendo, 1 Xbox / PC, 2 Swap Face, 3 Custom |

The other rows are 0 Off and 1 On. Some are read at the start, before the menu exists, by the code that needs them:
`handheld_profile` and `output_res` (`host/nn_applet.c`), `ultrawide` (`host/hos_main.c`), `robust_buffers`
(`gpu/vk/vk_main.cpp`) and the controls (`host/nn_hid.c`).

## Layout

| Path | What |
|---|---|
| `docs/design.md` | why mkw-recomp's pipeline, the architecture, MKW's lessons as rules, stages, testing |
| `nx2/` | the lift pipeline and its tests (mkw-recomp's, made game-neutral; `kit.py` has the paths), and `make_game.py`, which the builders run |
| `host/` | the standalone Horizon runtime (TOTKPort's `hos/` as adapted by mkw-recomp), with the native NVN2 layer `nvn_native.c`; `host/posix` has the Win32 calls on Linux |
| `gpu/vk/` | the Vulkan back end (`hookshot_vk.dll`, `hookshot_vk.so`), with Eden's shader recompiler and sirit |
| `gpu/render/`, `gpu/third_party/` | the WebGPU renderer the Windows exe falls back to when it was built with wgpu-native, its op stream (`rx_nvn.h`), suyu's decoders |
| `builder/` | Hookshot Builder: the Windows app (.NET 8 WPF), its setup and MSI, and the Linux builder and launcher scripts |
| `scripts/` | test runs, the profile and log readers, the save copier (`oot_saves.py`), release tooling, `scripts/linux` (dependencies, AppImage) |
| `tests/` | the game-free checks, all run by `tests/run_all.py` |
| `third_party/` | a64-kit (Hookshot's few changes marked `(hookshot)`), libopus, zbic (Atmosphere's zstd with ZBIC, for unpacking only), SDL2's licence, Dear ImGui and its Vulkan backend, zstd's decoder, the Libertinus fonts |

License: GPL-3.0-or-later, as mkw-recomp: a64-kit's translator is suyu's (GPL-2.0-or-later) and Eden's code is
GPL-3.0.
