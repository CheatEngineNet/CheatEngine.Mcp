# Repair a table after a game update

## Goal

Fix the record `{recordDescription}` (ask which when not given) that stopped working after a game update,
in the table at `{tablePath}` when given, else in the loaded table, and save the repair as a new file.

## Steps

1. Table not loaded: ask, have unsaved work saved and active scripts deactivated (a replace runs no `[DISABLE]`),
   then `table_load(path="{tablePath}", merge=false)`. Earlier ids go stale; it may wait on a Cheat Engine dialog.
   Read `inspection`, and tell the user when `monoAutoAttach` is true.
2. `record_find(descriptionContains="{recordDescription}")` searches the whole list, nested records included. Then
   `record_get(ids=[<id>])`: `script`, `address`, `offsets`, `currentAddress`, `active`.
3. `module_get(module="<module in the script>")`: a `pe.timeDateStamp` other than one noted in the script or its
   group confirms a new build.
4. Unpatch the site, with consent: deactivate the record (`record_set_active(ids=[<id>], active=false)`), then
   release an MCP patch on it (`asm_list_patches()`, `asm_release_patch(patchId="<patchId>")`).
5. For each `aobscanmodule` pattern, mask displacement and immediate bytes with `??` for a relaxed copy, then
   `aob_find(patterns=["<old pattern>", "<relaxed pattern>"], module="<module>", executable="required", limit=2)`.
   The old one still `unique`: the site holds; check the `assert` bytes and field offsets. One relaxed match:
   compare `code_disassemble(address="<match>+<script offset>", count=10)` with the original code kept in the script.
6. No match: find the value again ([known value](find-known-value.md)) and its writer
   ([find what writes](find-writer.md)), and compare the new instruction with the old one.
7. New signature: `aob_generate_signature(address="<new instruction>")` must be `unique` and `verified`; keep
   `pattern` and `offset`.
8. Edit the script: the pattern, the injection offset, the `assert` and `db` bytes and the copied original code, shifted
   field offsets (`[rsi+780]` may now be `[rsi+788]`), and a comment with the new build stamp. Then
   `asm_check(script="<script>")` until `accepted` (see `failedSection`; `[DISABLE]` needs bare `symbol:` lines,
   not `symbol+N`, and none under `{$STRICT}`).
9. With consent, `record_set_script(id=<id>, script="<script>")`.
10. Test after the user saves the game: `record_set_active(ids=[<id>], active=true)` (`active` false: `failure.reason`
    such as `aob_not_found` says why), check the game and `code_disassemble(address="<symbol>", count=3)`; then
    `record_set_active(ids=[<id>], active=false)`: `module_find_patches(module="<module>")` shows no range at the
    site. Twice.
11. Pointer record, wrong `currentAddress`: show its `address` and `offsets` (hex, base first);
    `pointer_read_chain(base="<address>", offsets=["10", "4C8"])` shows each hop. Often one field offset shifted (the
    writer now uses `[rsi+4D0]`): check
    `pointer_read_chain(base="<address>", offsets=["10", "4D0"], valueType="float")`, then with consent
    `record_update(updates=[{id=<id>, offsets=["10", "4D0"]}])`. Dead base: a [pointer scan](pointer-scan.md).
12. `table_save(path="<new .CT under a table root>", overwrite=false)`; keep the old table until the user confirms.

## Decisions

- The logic changed, not only the bytes: rebuild the cheat with the user ([AOB injection](aob-injection.md)).
- The relaxed pattern matches several places: add bytes; never pick one at random.
- `capability_disabled` from `table_load` names the switch the table's content needs
  ([cheat tables](../Documents/cheat-tables.md) lists them). Tell the user; never work around it.

## Pitfalls

- A table's Lua runs with Cheat Engine's rights: load only tables the user trusts. After a `table_load` timeout,
  never repeat it; check `record_list()`.
- Never activate a script whose signature is not proven unique.
- `record_set_active` checks only autoAssembler, not the script's content: `asm_check` every edit
  (`unsupported`: it needs another switch; review it by reading).
- An activation error whose `hostEffect` is not `not_started` or `not_applied`: inspect the code before any retry.

## Report

What broke (signature, bytes, offsets, logic or pointer), the new pattern and offsets, both build stamps, the test
results and the saved path. List any script left active and anything `runtime_list_resources()` still shows, with its
release call. See [AOB signatures](../Documents/aob-signatures.md) and [cheat tables](../Documents/cheat-tables.md).
