# Build a robust cheat table

## Goal

Turn the current address list into a table that survives restarts and small game updates, test it, and save it to
`{tablePath}` (ask for one when it is not given). Nothing is activated or changed without consent.

## Steps

1. `table_list_files()`: `{tablePath}` must be an absolute `.CT` path inside one of the `roots`, or `table_save`
   refuses (the user sets CheatEngineClient:AllowedTableRoots, then re-enables the plugin).
2. `module_get(module="<main module>")`: keep `pe.timeDateStamp`, the table's build.
3. `record_list(limit=1000)` pages the whole list, nested records included; `record_get(ids=[<id>])` for scripts. A
   module-relative address, or a chain (`offsets`) from one, is stable; a raw heap address or a script with
   hard-coded code addresses is fragile.
4. Heap addresses: find a static path ([pointer scan](pointer-scan.md)) or an
   [injection copy](injection-copy-base.md) symbol. With consent,
   `record_update(updates=[{id=<id>, address="game.exe+1A2B30", offsets=["10", "4C8"]}])`: hex offsets, nearest the
   base first, as `pointer_list_paths` gives them, sent with the address; `currentAddress` must be the value's address.
5. Fixed-address scripts, disabled first (signatures need original bytes):
   `aob_generate_signature(address="<injection point>")` must be `unique` and `verified`; keep `pattern` and `offset`.
   `asm_generate_injection(module="<module>", signature="<pattern>", expectedBytes="<original bytes>", symbolName="<unique name>", offset=<offset>)`
   gives the scaffold; port the old code into it ([AOB injection](aob-injection.md)).
6. `asm_check(script="<script>")` until `accepted` (see `failedSection`); review the `[DISABLE]`
   ([review a script](review-aa-script.md)). With consent, on an inactive record (an active one is refused),
   `record_set_script(id=<id>, script="<script>")`.
7. `record_group(description="<feature> (build <timeDateStamp>)", ids=[<id>, <id>])`;
   `record_move(id=<id>, parentId=<groupId>)` adds more.
8. Test each script after the user saves the game: `record_set_active(ids=[<id>], active=true)` (`pending`:
   `record_get` later), check the effect and `code_disassemble(address="<symbol>", count=3)`; then
   `record_set_active(ids=[<id>], active=false)`: `module_find_patches(module="<module>")` shows no range at the
   site. Twice.
9. Before the user restarts the game, `runtime_list_resources()` must be empty (with consent,
   `runtime_release_resources()`). Then `process_attach(process="<game.exe>")`; tell the user when `monoAutoAttach`
   is true. `record_get` each record (`currentAddress`, `value`); repeat step 8.
10. `table_save(path="{tablePath}", overwrite=false)`; if the file exists, ask before
    `table_save(path="{tablePath}", overwrite=true)`.

## Decisions

- A record fails after the restart: fix it; never save it broken.
- A pointer path dies on restart: rescan across more restarts.
- A chain needs no active script; an injection-copy symbol exists only while its script is enabled.
- `active` false after an enable: `failure.reason` (`assert_failed`, `aob_not_found`, ...) and `failure.text` say
  why; fix the script.
- `unsupported` from `asm_check`: the script needs another switch (`{$lua}`, `createthread`, `kalloc`), uses
  `globalalloc` or a non-hex `$`. Prefer pure Auto Assembler, else review it by reading; tell the user.
- `failedSection` disable over `symbol+N`: DISABLE is checked without ENABLE's symbols; use a bare `symbol:` line
  (none under `{$STRICT}`).
- Named values: `record_get(ids=[<id>], includeDropdown=true)`, then with consent
  `record_set_dropdown(id=<id>, items=[{value="0", description="Off"}, {value="1", description="On"}])`.

## Pitfalls

- `[DISABLE]` must undo all of `[ENABLE]`: restore the bytes, `unregistersymbol`, `dealloc`. After a bad disable the
  next enable fails its `assert`. Symbol names must be unique across the table.
- `record_set_active` checks only the autoAssembler gate, never the script: `asm_check` it first.
- Record activations are not MCP patches: `runtime_release_resources` ignores them; deactivate what you activated.
- By Cheat Engine's source, a `record_group` group reloads as a Byte record at address `0` showing `??` (children
  intact); a group the user adds in Cheat Engine does not.
- `partial_effect`, or a `hostEffect` of `started` or `unknown`: `record_get` before any repeat.

## Report

Per record: description, id, old and new form, two test cycles, after restart; the records still fragile, the build
time stamp and the saved path (`replacedExisting`). None stays active unless the user asks. See
[cheat tables](../Documents/cheat-tables.md), [AOB signatures](../Documents/aob-signatures.md) and
[auto assembler](../Documents/auto-assembler.md).
