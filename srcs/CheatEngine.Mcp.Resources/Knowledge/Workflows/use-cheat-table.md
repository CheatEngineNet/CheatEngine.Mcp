# Load and use a cheat table

## Goal

Load the cheat table at `{tablePath}` (the user picks one when it is not given), review its scripts, then activate
`{cheat}` (or the cheat the user picks) one at a time with consent, verify it in game and leave an undo. Only tables
from a source the user trusts, for single-player or offline games without anti-cheat.

## Steps

1. No `{tablePath}`: `table_list_files(offset=0, limit=100)` lists each table's `path` under the `roots`; the user
   picks one. No roots: the user adds a folder to `CheatEngineClient:AllowedTableRoots` and re-enables the plugin.
2. Ask where the table comes from and which game build it targets. A table's Lua runs with Cheat Engine's rights and
   its scripts patch code: load only a table the user trusts, made for this game.
3. `record_list(offset=0, limit=100)`: when records are already listed, ask, then
   `table_save(path="<new .CT under a table root>", overwrite=false)` so nothing is lost.
4. Explain the switches the load may need: Mcp:EnableUnsafeLua for Lua or forms, Mcp:EnableAutoAssembler for
   scripts, Mcp:EnableTargetCodeExecution for `UsesMono`, all three for a `.CETRAINER` or protected table. With
   consent, `table_load(path="<{tablePath} or the chosen path>", merge=true)`; `merge=false` only when the user wants
   the list replaced (a `.CETRAINER` always replaces it). It may wait on a Cheat Engine dialog: tell the user.
5. Read `inspection`: `requires`, `containsLua` and `assemblerScriptCount`. When `monoAutoAttach` is true, Cheat
   Engine starts its Mono data collector in the game: tell the user.
6. `record_find(descriptionContains="{cheat}")` when given, else `record_list(offset=0, limit=100)` to show the
   cheats. Ids from before the load are stale.
7. `record_get(ids=[<id>])`. A script (`variableType` 11): `asm_check(script="<script>")` must be `accepted`; read
   it with the user, or run [review an Auto Assembler script](review-aa-script.md). A value record: `currentAddress`
   and `value` must look right.
8. With consent, one cheat at a time: `record_set_active(ids=[<id>], active=true)`. Expect `active` true; `pending`
   means read `record_get(ids=[<id>])` again; `failure` gives Cheat Engine's reason.
9. The user checks the effect in game. To stop it: `record_set_active(ids=[<id>], active=false)`.

## Decisions

- `capability_disabled` from `table_load`: the table needs a switch that is off. Tell the user which one and never
  work around it; the user changes the setting, then disables and re-enables the plugin.
- The `failure` reason names a missing signature or a failed assert: the table was made for another game build; see
  [repair after an update](repair-after-update.md).
- A script with `{$lua}`, `luacall`, `createthread` or `loadlibrary`: `record_set_active` checks only
  Mcp:EnableAutoAssembler, and `asm_check` refuses such a script as unsupported. Read it with the user and enable it
  only if they accept what it runs.
- A group record: `record_list(parentId=<id>)` shows its children, which a table can activate with their parent.
  Activate script records by their own ids.
- A value record on a raw heap address (no module and no offsets) is stale in a new session: do not freeze it.

## Pitfalls

- A replace (`merge=false`, or any `.CETRAINER`) drops the list without running a `[DISABLE]`: deactivate active
  scripts first.
- After a `table_load` timeout never load again; `record_list()` shows what arrived. `host_refused` with `hostEffect`
  started may have left part of the table and its Lua in place.
- A load does not reactivate records, but the table's Lua can, and it can add timers or hotkeys: check `active` in
  the records after the load.
- Records and their scripts belong to Cheat Engine: `runtime_release_resources()` does not undo them.

## Report

The table path, its `inspection` (`format`, `requires`) and `monoAutoAttach`, then a table of record id,
description, type and `active` for each cheat used. Undo: `record_set_active(ids=[<id>], active=false)` for each
active cheat; the table saved in step 3 restores the earlier list once the scripts are off. See
[cheat tables](../Documents/cheat-tables.md), [Auto Assembler](../Documents/auto-assembler.md) and
[safety](../Documents/safety.md).
