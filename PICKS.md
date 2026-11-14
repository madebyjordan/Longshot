# Picks

Every change that came into Longshot from somewhere else, and how it was tested here. Picked with `git cherry-pick -x`,
so each commit keeps its author and names the commit it came from.

**Base:** Hookshot's main branch after v2.0.6 (`0828863`, NotRexed and contributors), merged in `a3b0aa3`. Longshot
started from Hookshot v2.0.5 (`232eecb`); it moved to Hookshot's main for 1.0.0 because AMD FSR 4 (below) runs on the
frame chain of Hookshot's DLSS (#841, by hckrman101), which needs much of Hookshot after v2.0.5. That merge is the one
change not picked one at a time. The two changes under "Not taken" are reverted after it (`0d9050d`).

| Change | From | Author | Longshot commit | Tested |
|---|---|---|---|---|
| AMD FSR 4 and FSR 4 Native AA in the Upscaler row | Hookshot PR #1008 (`d0f32d6`, not yet merged there) | madebyjordan | `a3b0aa3` (merge) | PR #1008 build on madebyjordan's and other players' PCs; RX 6700 XT, Windows 11: FSR 4.1.2 upscales 1536x864 to 2048x1152 |
| Night lighting: sky ambient float add past a `c1` offset | Longshot (also in Hookshot v2.0.6 as `0b286cc`) | madebyjordan | `517df7e` | RX 6700 XT, Windows 11: night dim deep blue, as on the Switch 2 |
| Memory-trim test accepts the resized BC3 scratch argument | Hookshot #806 (`65da32c`) | monsterlag | `e7f1c26` | `tests/test_mem_trim.py` passes (it failed on v2.0.5 since #705) |
| Precompile: compute pipelines with `RX_VK_NOOPT` | Longshot 1.0.0 (`c1db06c`) | madebyjordan | `c37772a` | RX 6700 XT: precompile's compute served by the driver's cache after the shaders' step (tested on 1.0.0) |
| 8 GB cards: 32 MB image blocks (Hookshot's own 8.5 GB BC3 threshold kept) | Hookshot #887 via Longshot 1.0.0 (`c0ddfc1`) | milal | `bb71908` | RX 6700 XT on 1.0.0: Hyrule Field laps and Castle Town with no black flash |
| A save gives the virtual core up while the host's file calls run: WriteFile, SetFileSize, FlushFile, CloseFile of a file opened for writing, CreateFile, DeleteFile, CreateDirectory, DeleteDirectoryRecursively and RenameFile, as OpenFile and ReadFile already did | unixman69/Hookshot `fix/save-io-vcore` (`2bb345d`, `eb6276a`, on Hookshot's main `8c359bc`; made after Hookshot went offline, never a Hookshot PR) | unixman69 | `cbad58a`, `e8b68cc`; `a065169` gives Longshot's 250 ms escape in their comments | RX 9070 XT, Mesa RADV 26.2.6, Linux, on Hookshot's main with #2614, #1975 and the fix (`8c359bc`; its escape is 2 s): without the fix, a save in Kakariko kept the save thread's core 2 for about 20 s (26 "ran without a core" escapes, waits up to 3.8 s, one frame of 10.2 s); with it, a lab run that wrote `account.sav` (320 bytes, before its teleport to Kakariko) had 0 escapes and no device lost, as had a lab run without it, so the slow save was not reproduced. `tests/test_vcore_fs_save.py` passes (16 failures on dev's `nn_fs.c`). Not yet played on Longshot's dev |

## Not taken

Changes from Hookshot and other forks that were looked at and left out, and why.

| Change | From | Why not |
|---|---|---|
| BC3 re-encode one layer a run | Hookshot #956 (v2.0.6) | Untested; works around a likely driver-side multi-layer copy/blit issue with 8x the dispatches and barriers |
| AMD terrain re-encode turned off | Hookshot v2.0.6 | Blamed for a GPU-time regression (#823) without a test; our RX 6700 XT runs with it on show no such cost |
