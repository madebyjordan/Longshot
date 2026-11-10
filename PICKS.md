# Picks

Every change that came into Longshot from somewhere else, and how it was tested here. Picked with `git cherry-pick -x`,
so each commit keeps its author and names the commit it came from.

**Base:** Hookshot's main branch after v2.0.6 (`0828863`, NotRexed and contributors), merged in `a3b0aa3`. Longshot
started from Hookshot v2.0.5 (`232eecb`); it moved to Hookshot's main for 1.0.0 because AMD FSR 4 (below) runs on the
frame chain of Hookshot's DLSS (#841, by hckrman101), which needs much of Hookshot after v2.0.5. That merge is the one
change not picked one at a time. The two changes under "Not taken" are reverted after it (`0d9050d`).

| Change | From | Author | Longshot commit | Tested |
|---|---|---|---|---|
| AMD FSR 4 and FSR 4 Native AA in the Upscaler row | Hookshot PR #1008 (`d0f32d6`, not yet merged there) | madebyjordan | `a3b0aa3` (merge) | Pending |
| Night lighting: sky ambient float add past a `c1` offset | Longshot (also in Hookshot v2.0.6 as `0b286cc`) | madebyjordan | `517df7e` | RX 6700 XT, Windows 11: night dim deep blue, as on the Switch 2 |
| Memory-trim test accepts the resized BC3 scratch argument | Hookshot #806 (`65da32c`) | monsterlag | `e7f1c26` | `tests/test_mem_trim.py` passes (it failed on v2.0.5 since #705) |

## Not taken

Changes from Hookshot and other forks that were looked at and left out, and why.

| Change | From | Why not |
|---|---|---|
| BC3 re-encode one layer a run | Hookshot #956 (v2.0.6) | Untested; works around a likely driver-side multi-layer copy/blit issue with 8x the dispatches and barriers |
| AMD terrain re-encode turned off | Hookshot v2.0.6 | Blamed for a GPU-time regression (#823) without a test; our RX 6700 XT runs with it on show no such cost |
