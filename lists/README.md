# Pipeline state list

`pipeline_states.bin` is the list a Longshot build ships beside the game as `hookshot_pipeline_states_shipped.bin`
(`nx2/make_game.py`; a build without this folder ships Hookshot's own list, merged from `assets/pipeline_states/`). The first start builds these
pipelines before the title screen, so the areas they came from do not pause to compile them in play.

It holds hashes, formats and draw state only (528 bytes a pipeline), no game code: the shaders come from the player's
own game.

## What is in it

| Recorded | Where | Pipelines |
|---|---|---|
| 2026-11-09 | Kokiri Forest, Hyrule Field, Castle Town (RX 6700 XT) | 2648 |

## Adding to it

1. Play the new areas. The game writes what it builds to `hookshot_pipeline_states.bin` in the game folder.
2. Merge, keeping this list's pipelines first:

       python -I nx2/pipeline_states.py --out lists/pipeline_states.bin lists/pipeline_states.bin <game folder>/hookshot_pipeline_states.bin

3. Test a first start with the new list before it is committed: every pipeline adds to the first start's wait.
