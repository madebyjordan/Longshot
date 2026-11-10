# Changelog

Longshot's releases. Versions follow [Semantic Versioning](https://semver.org/): patch = fixes, minor = features,
major = changes to saves, settings or the build. Hookshot's history up to v2.0.5 is in [changelog.txt](changelog.txt).

## 1.0.0 (unreleased)

Based on Hookshot's main branch after v2.0.6 (`0828863`), with AMD FSR 4 from Hookshot PR #1008. Everything Hookshot
changed after v2.0.5 comes with it (NVIDIA DLSS and DLAA from #841 among it), except the two changes under "Kept from
Hookshot v2.0.5".

### Features
- **AMD FSR 4** and **AMD FSR 4 Native AA** in the Graphics tab's **Upscaler** row: AMD's temporal upscaler in place of
  the game's own, on the same inputs as DLSS (Hookshot PR #1008 by madebyjordan, not yet in Hookshot). Tick **AMD FSR 4**
  in Longshot Builder, or `make_game.py --fsr`; AMD's DLLs are not included. See
  [docs/hookshot-readme.md](docs/hookshot-readme.md#build-from-source). Tested: pending.

### Fixes
- Night no longer looks like day: the sky's ambient light (a float add through a `c1` offset) is written again, so the
  exposure no longer brightens the night. `EDEN_F32_ADD_TRACK=0` turns it off. Tested: RX 6700 XT, night matches the
  Switch 2 (dim deep blue).
- The game-free test `test_mem_trim` passes again (picked from Hookshot #806 by monsterlag).

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
  v2.0.6 turned this off. `RX_VK_UNEVEN_BC=1` turns it off here.
- BC3 re-encode of ASTC arrays in runs of up to 8 layers. Hookshot v2.0.6 (#956) made it one layer a run.
