# Picks

Every change that came into Longshot from somewhere else, and how it was tested here. Picked with `git cherry-pick -x`,
so each commit keeps its author and names the commit it came from.

**Base:** Hookshot v2.0.5 (`232eecb`, NotRexed and contributors).

| Change | From | Author | Longshot commit | Tested |
|---|---|---|---|---|
| Night lighting: sky ambient float add past a `c1` offset | Longshot (also in Hookshot v2.0.6 as `0b286cc`) | madebyjordan | `517df7e` | RX 6700 XT, Windows 11: night dim deep blue, as on the Switch 2 |
| Memory-trim test accepts the resized BC3 scratch argument | Hookshot #806 (`65da32c`) | monsterlag | `e7f1c26` | `tests/test_mem_trim.py` passes (it failed on v2.0.5 since #705) |
| Precompile: compute pipelines with `RX_VK_NOOPT` | Longshot (`88230fd`, made on the Hookshot fork) | madebyjordan | `c1db06c` | RX 6700 XT: precompile's compute served by the driver's cache after the shaders' step |
| Start-up screen: the window opens at once and shows the shaders' preparation | Hookshot #860 (`86546b6`) | hckrman101 | `2f0867c` | RX 6700 XT: cold first starts in the window, including closing it mid-way and starting again |
| Insert menu: rounded selection rows | Hookshot #860 (`ab1d4c1`) | hckrman101 | `6d6db05` | Builds with the start-up screen; the menu was not opened in testing |
| Background shader threads leave the replay's uniform update list | Hookshot #968 (`400caf1`) | ozwaldorf | `3ce80f8` | RX 6700 XT: two sessions in play; `tests/test_jobs_wait.py` |
| Pipeline state list | Hookshot #939 (`b9730dc`) | hckrman101 | `9201a17` | RX 6700 XT: recorded 2,648 pipelines, then a cold first start with them and Hyrule Field and Castle Town in play |
| Pipeline state list: counts checked before records are read | Hookshot #1054 (`d1f513f`) | klimovich008 | `afbaa62` | `tests/test_pipeline_state_counts.py` |
| Pipeline state list's build workers leave the uniform update list | Hookshot `8b2e1d3` | NotRexed | `edc5cfc` | As the pipeline state list (the screensaver test hunk is left out: Longshot has no such test) |
| 8 GB cards kept inside the VRAM Windows allows | Hookshot #887 (`a6ee8ba`, not merged there) | milal | `c0ddfc1` | RX 6700 XT: Hyrule Field laps and Castle Town with no black flash; `tests/test_vram_squeeze.py` |

## Not taken

Changes from Hookshot and other forks that were looked at and left out, and why.

| Change | From | Why not |
|---|---|---|
| BC3 re-encode one layer a run | Hookshot #956 (v2.0.6) | Untested; works around a likely driver-side multi-layer copy/blit issue with 8x the dispatches and barriers |
| AMD terrain re-encode turned off | Hookshot v2.0.6 | Blamed for a GPU-time regression (#823) without a test; our RX 6700 XT runs with it on show no such cost |
