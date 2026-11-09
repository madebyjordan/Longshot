# Changelog

Longshot's releases. Versions follow [Semantic Versioning](https://semver.org/): patch = fixes, minor = features,
major = changes to saves, settings or the build. Hookshot's history up to v2.0.5 is in [changelog.txt](changelog.txt).

## 1.0.0 (unreleased)

Based on Hookshot v2.0.5 (`232eecb`).

### Fixes
- Night no longer looks like day: the sky's ambient light (a float add through a `c1` offset) is written again, so the
  exposure no longer brightens the night. `EDEN_F32_ADD_TRACK=0` turns it off. Tested: RX 6700 XT, night matches the
  Switch 2 (dim deep blue).
- The game-free test `test_mem_trim` passes again (picked from Hookshot #806 by monsterlag).

### Changes
- Releases are made by hand after the build is played: the release workflow no longer runs on every push, and the
  Linux AppImage is not published.

### Known issues
- The launcher's **Update** button and the builder still update from Hookshot's releases, not Longshot's. Don't use
  them on a Longshot build.

### Kept from Hookshot v2.0.5
- AMD: terrain textures re-encoded at sizes whose BC levels halve evenly (no grid lines in Hyrule Field). Hookshot
  v2.0.6 turned this off.
