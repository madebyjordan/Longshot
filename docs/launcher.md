# The launcher

`launcher/Hookshot` is the Hookshot launcher: one Avalonia 11.3 app (C#, .NET 8) for Windows and Linux, Steam Deck
included. It is meant to be the one thing a player downloads: a portable zip on Windows, an AppImage on Linux. It
downloads the builder of each release itself.

## Build and run

You need the .NET 8 SDK. A developer build:

```sh
dotnet build launcher/Hookshot/Hookshot.csproj
dotnet run --project launcher/Hookshot -- --launch <game folder>
```

`--launch <folder>` opens the launcher of that game folder (any folder works, a missing one too). It takes the 2.x
Builder's command line too, as [launcher-upgrade.md](launcher-upgrade.md) section 4 asks: `--update <folder> [--tag <tag>]
[--fetched]` (that release brought into the folder; with no tag, the latest when it is newer) and `--play [<folder>]` (the
game started with no window; the folder built last when none is named).

The build runs the trim and AOT analyzers (NativeAOT is on in `Publish.props`) and treats warnings as errors. Code that
needs reflection the trimmer cannot see, such as `JsonSerializer` without a source-generated context, fails
`dotnet build` instead of breaking the published launcher.

## Design work: seeing changes as you make them

Two scripts, both in scratch folders under `build/launcher/` (no builder, game or GitHub of this PC is used):

- **`scripts/launcher/live.ps1`**: the launcher in a real window that follows your edits. A saved `.axaml` file shows in it
  within a second ([HotAvalonia](https://github.com/Kira-NT/HotAvalonia), MIT, in Debug builds only: nothing of it is in a
  Release build or a publish). The window is then made again where it was, so what its code draws (the version line,
  Update beside Play) stays right. A saved `.cs` file is applied by `dotnet watch`, or the window restarts by itself.
  `-State` opens it in a given state, `-NoGame` with no game yet, `-Fail` with every build stopping half way. Alt+F5
  reloads it by hand. Mistakes in a saved `.axaml` show in the script's terminal.

  Every button in it can be pressed: it runs against made-up releases (`scripts/launcher/dummy.ps1`, made afresh at each
  start). v2.2.0, v2.2.0-rc.1 and v2.0.14 are served by a web server on 127.0.0.1 with their setup lists and parts, v2.1.0
  is installed, and the builders' `python.exe` is the test rig's stand-in for `make_game.py`
  (`tests/builder_launcher/rig/fakepy.c`), which goes through a build's steps in about 10 seconds without reading anything.
  Update installs v2.2.0's builder with the real setup (`--engine`) from that server and "builds" the folder; Build makes a
  new game folder from an empty game file; Play starts a stand-in that says so (`scripts/launcher/dummygame.c`).
  `HOOKSHOT_DUMMY_RELEASES` (Builders.cs) keeps each download on that server: a first builder from GitHub and AMD's FSR
  runtime are refused.
- **`scripts/launcher/sheet.ps1`**: every screen on one page (`build/launcher/sheet/index.html`), drawn off-screen again on
  each save, about 10 seconds for all of them: the main screen with and without an update, no game yet, building, build
  stopped, Releases, updating, update stopped, Settings. A Paper export named like a state
  (`build/launcher/sheet/paper/<state>.png`) shows beside it.

The states a build, an update or a failure is in are made up for both, nothing run: `HOOKSHOT_UI_PREVIEW` takes
`building[=0..1]`, `stopped`, `updating[=0..1]` and `updatestopped` beside `menu`, `tab=build|releases|settings`, `older`
and `pre` (`MainWindow.Preview`).

## Publish

```powershell
scripts/launcher/publish.ps1    # Windows: build/launcher/Hookshot-<version>-Windows-x64-portable.zip
```

```sh
scripts/launcher/publish.sh     # Linux: build/launcher/Hookshot-Launcher-<version>-x86_64.AppImage
```

Both publish with NativeAOT, which compiles for the system you publish on:

- **Windows** needs Visual Studio's C++ tools ("Desktop development with C++"). The script finds them through vswhere.
  It also builds `HookshotBuilder-Setup.exe` from `builder/setup/setup.c` into the zip, beside `HookshotLauncher.exe`, with
  llvm-mingw (`LLVM_MINGW`, or the one an installed Hookshot Builder has). The launcher installs each release's builder
  with that setup's engine mode, which the setups in earlier releases do not have.
- The Linux AppImage is `Hookshot-Launcher-<version>-x86_64.AppImage`, apart from the Builder's
  `Hookshot-<version>-x86_64.AppImage`, which the launcher downloads for a first builder.
- **Linux** needs `clang` and `zlib1g-dev`. The executable needs the glibc of the system it was published on or newer.
  Publish on Ubuntu 22.04 (glibc 2.35), as CI does and as the Builder's AppImage is.

Without those tools, `-Jit` / `--jit` publishes the JIT build instead: trimmed, ReadyToRun and one file. It is slower
to start and larger, but needs no C++ tools and can cross-publish from Windows to `linux-x64`.

The version is `HOOKSHOT_VERSION`, or else `git describe`'s, as the release workflows name their files. The publish
keeps its own `bin` and `obj` under `build/launcher/`, apart from a `dotnet build`. The AppImage uses the
appimagetool and runtime that `scripts/linux/deps.sh` pins, from `build-linux/deps` (`$DEPS`); the script fetches them
there, checked against the same SHA-256, when deps.sh has not fetched them yet.

[`.github/workflows/launcher.yml`](../.github/workflows/launcher.yml) runs both scripts on every change to the
launcher and keeps the zip, the AppImage and the smoke-test screenshots as the run's artifacts.

## The installer

`publish.ps1` also makes `build/launcher/Hookshot-Installer.exe`: `launcher/installer/installer.c` with the portable zip appended
to it, then the zip's length and `HSZIP001`, which it reads from its own end. It needs no administrator:

- the launcher goes in `%LOCALAPPDATA%\HookshotLauncher\app` (`HOOKSHOT_LAUNCHER_HOME` moves the home), where a 2.x
  Builder's hand-over installs it too, so a PC has one launcher whichever way it came. It is unpacked beside it with
  Windows' own `tar.exe` and swapped in whole, so an install that stops half way, or one over a launcher that is running,
  leaves the one before as it was. A zip without `HookshotLauncher.exe` at its root is refused;
- Hookshot goes in the Start menu, on the desktop and in Settings > Apps, whose Uninstall runs `Uninstall.exe` (the installer
  without its zip), kept in the home beside `app`, so a hand-over or update that swaps `app` whole leaves it there;
- `/S` installs with no questions and starts nothing; `Uninstall.exe /uninstall [/S]` uninstalls.

Uninstalling removes the launcher, its shortcuts and its Apps entry. The games, their saves and (unless the player asks) the
downloaded builders stay. `tests/test_launcher_installer.py` checks all of it against temporary folders and a registry key
of its own, and `publish.ps1` installs, draws and uninstalls the real one.

## Game folders open the launcher

The launcher writes where it runs from at every start: `%LOCALAPPDATA%\HookshotBuilder\launcher.txt` on Windows,
`~/.local/share/hookshot/builder/launcher` on Linux (the AppImage's path, not its mount). Each game folder's starter
(`hookshot.exe`, `builder/setup/setup.c` launch mode; `hookshot`, `builder/linux/hookshot-folder`) and the game's own update
offer on Linux (`host/hos_update.c`) open it first, with `--launch <folder>` or `--update <folder> --tag <tag>`. A launcher
moved or removed since is passed over for the installed builder, as before.

Releases before the launcher put starters in their game folders that do not know it, so after each build or update the
launcher puts in its own: the setup it ships on Windows, the script it carries on Linux.

## From a 2.x Builder

A Builder from 2.2 on hands a player over to this launcher when the release it would update to ships one
([launcher-upgrade.md](launcher-upgrade.md), NotRexed/Hookshot#2722). This launcher's side of that contract:

- **Where it lives.** `%LOCALAPPDATA%\HookshotLauncher\app\HookshotLauncher.exe` on Windows (the executable is named so, and
  the portable zip's root holds it), `${XDG_DATA_HOME:-~/.local/share}/hookshot/launcher/Hookshot.AppImage` on Linux.
  `HOOKSHOT_LAUNCHER_HOME` moves both.
- **What it takes in** (`launcher/Hookshot/Handover.cs`). At every start, before the window shows, each `<home>/import/*.json`
  the Builder dropped is read and deleted (one it cannot read is kept as `.json.bad`). With no settings of its own yet, the
  import's settings become its settings (the game files, DLSS, FSR 4, prepared shaders, build memory, the track, and the
  work folder on Windows); with settings already, they stay, and the import's folder becomes the one opened with nothing
  asked only when the saved one holds no game. On Linux, a first start with nothing dropped reads the Builder's
  `~/.config/hookshot/builder.ini` instead. On Windows nothing needs reading: the launcher and the Builder share
  `builder-settings.json`.
- **Its builds stay the Builder's.** The contract leaves `builds.json` and the versions to the launcher. This one installs
  and drives each release's builder in the Builder's own folder (`HookshotBuilder`, `hookshot/builder`), as the WPF and Tk
  launchers do, so there is nothing to move. A 2.x Builder's "Free up space" can still remove versions there; after the
  hand-over the Builder only passes its starts on to the launcher.
- **The release.** `hookshot-launcher.txt` names the zip and the AppImage, with their SHA-256 and size
  (`scripts/launcher/manifest.py`). `launcher.yml` writes and uploads it after them, only while the repository variable
  `CI_LAUNCHER_HANDOVER` is `true`: every 2.2+ Builder that updates to such a release offers the move.

`tests/test_launcher_handover.py` checks this against the contract's sample files and an import file the Linux upgrader
writes itself; `tests/test_launcher_manifest.py` checks the release file with the Builders' own parser.

## Smoke test without a screen

Both scripts run the published launcher once with:

- `HOOKSHOT_UI_SHOT=<file.png>`, which draws the window with Skia and Avalonia.Headless to that PNG and exits 0
  (`launcher/Hookshot/Shot.cs`). No display is needed.
- `HOOKSHOT_LATEST_TAG=v2.1.0,v2.0.14`, which stands in for GitHub's releases, so the test needs no network.
- `HOOKSHOT_SETUP_BASE` set to a temporary folder, plus `XDG_DATA_HOME` and `XDG_CACHE_HOME` on Linux, so no builder
  installed on the PC is read.

The PNG must exist and be 960x640. On Linux it runs twice: from the AppDir, then from the AppImage itself with
`--appimage-extract-and-run` (no FUSE needed). The PNGs are `build/launcher/smoke-*.png`. To try it by hand:

```powershell
$env:HOOKSHOT_UI_SHOT = "$PWD\shot.png"; $env:HOOKSHOT_LATEST_TAG = 'v2.1.0,v2.0.14'
build\launcher\win-x64\zip\HookshotLauncher\HookshotLauncher.exe --launch C:\nowhere
```

## Sizes and start times

These are win-x64 numbers on the development PC. Each publish draws the window headlessly. The start time is the time
until the PNG is written, as a median of warm runs.

| Publish | Files | Size | Zip | Start |
|---|---|---|---|---|
| **NativeAOT (chosen)** | 4 + SDL2 | 42.5 MB | 17.8 MB | ~80 ms |
| trimmed + ReadyToRun + single file (`-Jit`) | 4 + SDL2 | 57.5 MB | 24.1 MB | ~260 ms |
| trimmed + ReadyToRun | 81 | 57.5 MB | 25.0 MB | ~320 ms |
| ReadyToRun, not trimmed | 216 | 107.8 MB | 46.9 MB | ~350 ms |
| self-contained, nothing else | 216 | 96.1 MB | 41.9 MB | ~630 ms |
| trimmed only | 81 | 42.8 MB | 18.3 MB | ~820 ms |
| trimmed + single file, compressed | 1 | 20.1 MB | 15.1 MB | ~1040 ms |

With SDL2 for controllers, the NativeAOT zip is 46.7 MB unpacked and 19.5 MB zipped. On Linux (the Ubuntu 22.04 VM),
the NativeAOT AppImage is 19 MB: the launcher's folder is 49.7 MB, with an executable of 35.9 MB. It starts in ~120 ms
from its folder and ~230 ms as an AppImage. The JIT build's AppImage is 24 MB and starts in ~430 ms and ~480 ms.

Notes on the table:

- Trimming alone starts slower than no trimming. The trimmer rewrites the framework's assemblies without their
  precompiled code, so they are compiled again at every start. ReadyToRun puts that code back.
- A compressed single file saves nothing that the zip or the AppImage's squashfs does not already save. It adds
  ~100-800 ms to every start.
- With `IncludeNativeLibrariesForSelfExtract`, the single file writes its native libraries to a temporary folder on
  the first start. The libraries stay beside the executable instead.
- NativeAOT's `OptimizationPreference=Size` or `Speed` changed the size by under 3 MB and the start time by nothing
  measurable, so the default is kept.

Every variant drew the same pixels.

## What it needs at run time

**Windows**: Windows 10 or 11, x64. Nothing to install. The executable uses the Universal C Runtime that Windows
ships. The zip also holds:

| Library | Purpose |
|---|---|
| `libSkiaSharp.dll` | Skia, which draws the window |
| `libHarfBuzzSharp.dll` | Text shaping |
| `av_libglesv2.dll` | ANGLE, for GPU drawing over Direct3D. Without it, Avalonia draws in software. |
| `SDL2.dll` | Controllers |

**Linux**: x86_64 with glibc 2.35 or newer, the same floor as the Builder's AppImage (Ubuntu 22.04, Debian 12, Fedora
36, current SteamOS). Published on 22.04, the executable asks for `GLIBC_2.34` at most. Its libraries ask for less:
`libSDL2-2.0.so` 2.27, `libSkiaSharp.so` 2.17, `libHarfBuzzSharp.so` 2.14. `publish.sh` prints these. The AppImage
carries the same libraries as the zip, except ANGLE: `libSkiaSharp.so`, `libHarfBuzzSharp.so` and `libSDL2-2.0.so`.
It loads them from its own folder without `LD_LIBRARY_PATH`, so the builders and games it starts see the system's
libraries.

It expects these from the system, which every desktop distribution and the Steam Deck have:

| Library | Purpose |
|---|---|
| `libfontconfig.so.1` and FreeType | Skia's fonts |
| `libX11.so.6`, `libXext.so.6`, `libXi.so.6`, `libXrandr.so.2`, `libXcursor.so.1`, `libXfixes.so.3` | Avalonia's X11 window |
| `libICE.so.6`, `libSM.so.6` | X session management |
| `libGL.so.1` (optional) | GPU drawing. Without it, Avalonia draws in software. |
| `libgtk-3.so.0` (optional) | File dialogs where there is no xdg-desktop-portal |

Avalonia 11 draws through X11. On Wayland it runs under XWayland, which GNOME, KDE and the Steam Deck's Game Mode
(gamescope) all provide. The AppImage runtime (type2-runtime, pinned in deps.sh) is static: running the AppImage
directly needs only `fusermount` or `fusermount3`. Without FUSE, use `--appimage-extract-and-run`.
