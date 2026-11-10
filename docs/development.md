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
| `powershell -File scripts/bench.ps1 [-Name x] [-Against <run>]` | a benchmark: a test run with no window on a pinned save, and its numbers as a table (see below) |
| `python -I scripts/benchlog.py <log or run folder> [<another>]` | a run's numbers from its log, or two runs side by side |
| `python -I scripts/prof.py <stacks file>` | a profile sampled with `HOS_SAMPLE=<thread>` + `HOS_SAMPLE_FILE=<stacks file>`, named from the linker maps |

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
which moves nothing on a line it does not know: a version's tools may be older or newer than the launcher. A release ahead of
the builder in use makes its builder the one in use from the next start (`versions\current.txt`); a pre-release or an older
release never does, so going back to one does not take the launcher back. After the build the builders no longer in use go,
as the build files do (`Storage.Free`).

`powershell -File tests/builder_launcher/rig/run.ps1` tries all of it without a game, a release or GitHub: made-up releases
handed out by a web server on this PC, a scratch install (`HOOKSHOT_SETUP_BASE`, which the builder honours as the setup does)
and a stand-in for a version's Python that says what `make_game.py` says. It checks the setup's engine mode alone, then
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
