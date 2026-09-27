# Build a robust cheat table

## Goal

Turn the current address list into a table that still works after restarts and small game updates, and save it to `{tablePath}`.

## Steps

1. `table_list_files()`: shows the allowed table roots. `{tablePath}` must be an absolute path under one of them, otherwise `table_save` refuses; ask the user to configure a root if needed.
2. `record_list(depth=8, limit=500)`: inventory of values, pointers, groups and scripts. `record_get(ids=[...], includeScript=true)` for details.
3. Classify each record: module+offset address (stable; keep it module-relative), heap address (fragile), pointer (check it), script with fixed addresses (fragile).
4. Heap addresses: replace them with a pointer path ([pointer scan](../workflows/pointer-scan.md)) or an injection-copy symbol ([injection copy](../workflows/injection-copy-base.md)), then, with consent, `record_update(updates=[{id, address, offsets}])`.
5. Fixed-address scripts: rebuild them as AOB scripts ([make a signature](../workflows/make-aob-signature.md), [AOB injection](../workflows/aob-injection.md)) using `aobscanmodule`, `registersymbol`, and a `[DISABLE]` that restores the bytes and deallocates. `asm_check(script=...)`, then with consent `record_set_script(id=..., script=...)`.
6. Organize: `record_group(ids=[...], description="<feature>")` and `record_move(ids=[...], parentId=...)`.
7. Test each script with consent: `record_set_active(ids=[<id>], active=true)`, verify (disassembly at the injection point, in-game effect), then `active=false` and verify the original code is back. Do this twice.
8. Ask the user to restart the game, attach to the new process (release owned resources first), then activate and verify every record again.
9. `table_save(path="{tablePath}", overwrite=false)`; if the file exists, ask before `overwrite=true`.

## Decisions

- A record fails after the restart: fix it now; never save a broken entry silently.
- A pointer path dies on restart: rescan with more restarts.
- A script needs Lua: loading the table will then need the unsafeLua gate; prefer pure Auto Assembler.
- `capability_disabled` on scripts: the autoAssembler gate is off; report it.

## Pitfalls

- `[DISABLE]` must undo everything `[ENABLE]` did; a second enable after a bad disable fails its assertion.
- Symbol names from `registersymbol` must be unique across the table.
- Record ids go stale after any `table_load`; list them again.
- A `partial_effect` from a batch record call: inspect the reported ids before repeating anything; never retry a mutation blindly.

## Report

Records converted and how, test results (two enable and disable cycles, after restart), records still fragile, and the saved path. Records stay in CE's address list; confirm with `runtime_list_resources()` that no MCP resource is left, or release it. See [cheat tables](../cheat-tables.md), [AOB signatures](../aob-signatures.md) and [auto assembler](../auto-assembler.md).
