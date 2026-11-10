# Changelog

Longshot's releases. Versions follow [Semantic Versioning](https://semver.org/): patch = fixes, minor = features,
major = changes to saves, settings or the build. Hookshot's history up to v2.0.5 is in [changelog.txt](changelog.txt).

## 1.0.0 (2026-11-10)

Based on Hookshot v2.0.5 (`232eecb`).

### Fixes
- Night no longer looks like day: the sky's ambient light (a float add through a `c1` offset) is written again, so the
  exposure no longer brightens the night. `EDEN_F32_ADD_TRACK=0` turns it off. Tested: RX 6700 XT, night matches the
  Switch 2 (dim deep blue).
- The game-free test `test_mem_trim` passes again (picked from Hookshot #806 by monsterlag).
- No black flash and broken ground after a lap of Hyrule Field: the texture budget was cut from 6 GB to 2.6 GB near the
  VRAM limit while 4 GB sat free inside the memory blocks. Image blocks are smaller, and the budget is cut only by what
  that free space cannot take. With Hookshot #887 by milal (8 GB cards kept inside the VRAM Windows allows).

- AMD: pipelines are built without the driver's optimizer by default with AMD's own driver (it is the
  `RX_VK_NOOPT=1` test switch, now on by itself there). Its compiler took 10 to 60 seconds on some shaders and froze
  the game while it did. Every Longshot test ran this way. `RX_VK_NOOPT=0` turns it off.

### First start
- **A start-up screen.** The game window opens at once and shows the shaders' preparation, with the Longshot logo and a
  few lines on how it is going (GPU and workers, rate and CPU use, counts). `RX_VK_STARTUP_DIAG=0` hides those lines.
  From Hookshot #860 by hckrman101.
- **No pauses where the list was recorded.** A list of 2,648 pipelines recorded in Kokiri Forest, Hyrule Field and
  Castle Town (`lists/pipeline_states.bin`) ships with the game, and the first start builds them before the title.
  Tested: in Hyrule Field and Castle Town the game built 2 pipelines in play (slowest 0.6 s), against 200+ (slowest
  2.4 s) without it. The pipeline state list is Hookshot #939 by hckrman101, with #1054 by klimovich008, `8b2e1d3`
  by NotRexed and #968 by ozwaldorf.
- **A shorter wait.** About 18 minutes on an RX 6700 XT with a Ryzen 5 5600, the list included (12 for the shaders,
  6 for the pipelines; Hookshot v2.0.5 took 27 for the shaders alone and then compiled the pipelines in play):
  - compute variants for two or more texture answers are compiled when play first asks for them, not before the game
    (the shaders' step went from 27 to 12 minutes);
  - Hookshot's pipeline guesses are left out when a list was built: from the list's 2,648 it guessed 26,838, about
    90 minutes more. `RX_VK_PREDICT=1` builds both;
  - compute pipelines are compiled with `RX_VK_NOOPT` as the shaders' step builds them, so the driver's cache serves
    them the second time.
- A pipeline a draw is waiting for is built by the waiting thread when no worker has started it yet, instead of
  waiting behind the queue.

### Changes
- Releases are made by hand after the build is played: the release workflow no longer runs on every push, and the
  Linux AppImage is not published.
- Longshot's own name and updates: the game folder's launcher is **Longshot.exe**, the game window says Longshot, and
  **Longshot Builder** (in `%LOCALAPPDATA%\LongshotBuilder`) replaces Hookshot Builder. The builder, the launcher and the
  game check Longshot's releases for updates, not Hookshot's. A folder's old `hookshot.exe` launcher (which opened
  Hookshot Builder) is removed when the folder is built again. The files inside keep their names (`bin\hookshot.exe`,
  `hookshot_options.ini`, `hookshot_env.txt`, the shader caches), so settings and caches carry over.

### Known issues
- Longshot Builder has no download yet: its setup needs Longshot's own copy of the build tools on GitHub. Build from
  source for now.

### Kept from Hookshot v2.0.5
- AMD: terrain textures re-encoded at sizes whose BC levels halve evenly (no grid lines in Hyrule Field). Hookshot
  v2.0.6 turned this off.
