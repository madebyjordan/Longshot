# Merging Hookshot into dev

`dev` follows Hookshot's `main` (github.com/NotRexed/Hookshot). The daily job
(`.github/workflows/hookshot-into-dev.yml`, on `main`) merges it; when Git cannot, Claude Code resolves the clashes
by these rules, the tests decide, and anything left over becomes an issue. A merge by hand follows the same rules.

Hookshot's `main`, not its releases: releases are cut from side branches (`rc/...`), so each one would clash again with
what `main` already brought in.

## The rules, file by file

1. **Hookshot's own work wins.** Where dev only carries an earlier copy of something Hookshot has since merged (a PR of
   ours that was taken upstream, such as AMD FSR 4, #1008), take Hookshot's version of the file
   (`git checkout --theirs`). Check first: `git log --no-merges upstream..dev -- <file>` lists dev's own changes to it.
2. **Longshot's name.** Where dev's side only renamed Hookshot to Longshot (builder text, setup, updater, CI, the
   Linux updater), take Hookshot's side with the same renames: user-visible "Hookshot" becomes "Longshot", and
   `HookshotBuilder-Setup.exe` / `dist/HookshotBuilder.exe` become `LongshotBuilder-…`. File names, folders inside the
   game folder (`hookshot_*.bin`, `bin/hookshot.exe`), environment variables (`HOOKSHOT_*`, `RX_VK_*`) and
   `recomplauncher.json`'s names stay as Hookshot has them.
3. **Longshot's features are kept, on Hookshot's new code.** When Hookshot rewrote the code a Longshot change sits in,
   put the change back into the new code; never drop it, and never bring the old code back. The Longshot changes:
   - first start (`gpu/vk/vk_draw.cpp`): `compute_combination` (compute variants of two or more texture answers left
     to play), `pipelines_noopt` (no driver optimizer with AMD's driver), `state_list_noted` (predict skipped with a
     pipeline state list), the start-up counters `prep_translated_n` / `prep_compiled_n` / `prep_workers_n`;
   - `RX_VK_CACHE_DIR` / `RX_VK_CACHE_READONLY` (`gpu/vk/vk_paths.h`): every cache file through `cache_path()`, no
     cache or marker written when `cache_readonly()`;
   - the texture budget (`gpu/vk/vram_squeeze.h`): the cut is only what the blocks' free space cannot take;
   - the start-up screen: the `LONGSHOT` wordmark and the diagnostics lines (`StartupView::diag`);
   - `lists/pipeline_states.bin`, shipped by `nx2/make_game.py` in place of Hookshot's
     merged list (`assets/pipeline_states/`) when the source has it;
   - the night fix (`EDEN_F32_ADD_TRACK`), the AMD terrain re-encode kept on (`RX_VK_UNEVEN_BC`);
   - the launcher and version list in the builder, `scripts/build-game.ps1`, `nx2/version.py`'s dev label.
4. **readme.md is Longshot's.** Keep dev's side. Hookshot's readme lives on as `docs/hookshot-readme.md`. The same for
   `CHANGELOG.md` (Longshot's releases; Hookshot's, which release-please writes, lives on as `docs/hookshot-changelog.md`).
5. **Tests.** A Hookshot test that checks code Longshot changed is adjusted to Longshot's code, as little as it takes
   (a stand-in for `cache_readonly()` in a fixture, for example). A Longshot test is never deleted to make a merge pass.

## Done means

- No conflict markers left; the merge committed as "Merge Hookshot main (<short sha>) into dev".
- `python -I -B tests/run_all.py` fails on nothing that does not also fail on Hookshot's `main` on the same machine.
- On Windows also: the renderer builds (`scripts/build-vk.ps1`).
- The commit message says how each clash was resolved, and that it was not played yet.
