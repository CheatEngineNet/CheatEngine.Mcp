# Repair a table after a game update

## Goal

Fix the record `{recordDescription}` (from the table at `{tablePath}` when given) that stopped working after the game
was updated.

## Steps

1. If the table is not loaded: ask, then `table_load(path="{tablePath}", merge=false)`. `merge=false` replaces the
   current address list, and all earlier record ids become stale.
2. `record_find(descriptionContains="{recordDescription}")`, then `record_get(ids=[<id>])`.
3. `module_get(module="<main module>")`: the PE timestamp confirms the new build.
4. Take each AOB pattern from the script and run `aob_find(patterns=["<old pattern>"], module="<module>", limit=2)`.
   Zero or several matches means the signature broke.
5. Try a relaxed pattern: wildcard displacement and immediate bytes and scan again. With one match, compare
   `code_disassemble` at the match with the original code kept in the script's comments.
6. No match: find the value again ([known value](../workflows/find-known-value.md)
   or [unknown value](../workflows/find-unknown-value.md)), then its writer
   ([find what writes](../workflows/find-writer.md)), and compare the new instruction with the old one.
7. New signature: [make a signature](../workflows/make-aob-signature.md) on the new instruction.
8. Update the script: pattern, injection offset, shifted structure offsets (`[rsi+780]` may now be `[rsi+788]`), and the
   original bytes in `[DISABLE]`.
9. `asm_check(script=...)`, then with consent `record_set_script(id=<id>, script=...)`.
10. Test: `record_set_active(ids=[<id>], active=true)`, verify in game and in the disassembly, then `active=false` and
    verify the original bytes; do it twice.
11. Pointer records: rescan with the [pointer scan](../workflows/pointer-scan.md), then with consent
    `record_update(updates=[{id, address, offsets}])`.
12. `table_save(path=<new file under an allowed root>, overwrite=false)`; keep the old table until the user confirms.

## Decisions

- The logic changed, not only the addresses: rebuild the cheat with the user instead of forcing the old script.
- The relaxed pattern matches several places: add bytes; never pick one at random.
- `table_load` fails with `capability_disabled`: the table contains Lua or Mono options; tell the user which gate it
  needs.

## Pitfalls

- `table_load` inspects table Lua before loading and derives the required gates; use it only for tables the user trusts.
- Never activate the old script before its new signature is proven unique.
- An activation error whose `hostEffect` is not `not_started` or `not_applied`: inspect the code before any retry.

## Report

What broke (signature, offsets, logic), the new pattern and offsets, the test results, the build timestamp, and the
saved path. Scripts left active and any MCP resource from `runtime_list_resources()` must be listed with their release
calls. See [AOB signatures](../aob-signatures.md) and [cheat tables](../cheat-tables.md).
