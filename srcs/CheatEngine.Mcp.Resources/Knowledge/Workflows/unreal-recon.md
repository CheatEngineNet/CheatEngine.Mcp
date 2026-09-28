# Explore a UE4 or UE5 game

## Goal

Find `{goal}` in an Unreal Engine game (`{ueVersion}`; auto reads the version string) and anchor it on the name pool
and `GWorld`. Offsets below are typical x64 layouts, UNVERIFIED until a check on this build passes. Offline or
single-player games the user owns or may modify only.

## Steps

1. Ask whether the game is offline and free of anti-cheat: an `EasyAntiCheat` or `BattlEye` folder in it means stop.
2. `process_list(nameContains="Shipping")`, then `process_attach(process="<Project>-Win64-Shipping.exe")`, not the
   launcher.
3. `module_get(module="<exe>")`: keep `pe.timeDateStamp`; offsets hold for this build only.
4. Only with `{ueVersion}` auto:
   `aob_find_value(valueType="wstring", value="+UE5+Release-", module="<exe>", limit=5)`, then `+UE4+Release-`, then
   both as `string`; `memory_read(address="<match>", valueType="wstring", length=64)` reads for example
   `+UE5+Release-5.3` (memory_read.length counts bytes). Names use a pool from 4.23 on; UE5 positions are doubles.
5. Find the value with [find coordinates](find-position.md) (`double` on UE5) or [a known value](find-known-value.md),
   then [find what writes](find-writer.md) it: the base register is the object `<obj>`, the offset the field.
6. Object header: `memory_read(address="<obj>+18", valueType="int32")` is its name index and
   `memory_read(address="[<obj>+10]+18", valueType="int32")` its class's (ClassPrivate at +10, Outer at +20).
7. Name pool (4.23+):
   `aob_find(patterns=["?? 01 4E 6F 6E 65 ?? 03 42 79 74"], writable="required", alignment=65536, limit=10)` finds
   the first block (`None`, `ByteProperty`), scanning all writable memory while CE waits.
   `pointer_find_references(target="<block>", maxOffset=0, module="<exe>")` gives its holder; tell the user, then
   `symbol_register(name="ueNamePool", address="<holder>-10")`.
8. Resolve an index: the `hex` of `util_calculate(expression="0x10 + 8 * (<index> >> 16)")` is `<slot>`, of
   `util_calculate(expression="(<index> & 0xFFFF) * 2")` `<entry>`;
   `memory_read(address="[ueNamePool+<slot>]+<entry>", valueType="uint16")` is the header: length header >> 6,
   UTF-16 if bit 0 is set. Text (no terminator):
   `memory_read(address="[ueNamePool+<slot>]+<entry>+2", valueType="string", length=<length>)` (wstring: 2 * length).
9. `GWorld`: `memory_read(address="[<obj>+20]+20", valueType="pointer")` is an actor's world (its level's Outer; a
   component adds a `+20` hop), whose class name reads `World`.
   `pointer_find_references(target="<world>", maxOffset=0, module="<exe>")` gives the holder and its `symbol`;
   `symbol_register(name="ueWorld", address="<holder>")`, then check the offsets to `<obj>` (Decisions) with
   `pointer_read_chain(base="ueWorld", offsets=["<offset>", "..."], valueType="pointer")`.
10. With consent, from the holder's module+offset `symbol`, which survives restarts:
    `record_create(records=[{description="{goal}", address="<symbol>", offsets=["<offset>", "...", "<field>"], variableType=4}])`
    (5 for a double).

## Decisions

- `{ueVersion}` ue4 or ue5 skips step 4; a position scan that finds nothing as `double` tries `float`, and the reverse.
- No name block: UE 4.22 or older, or a fork: use the [pointer scanner](pointer-scan.md).
- Several holders in step 7: keep the one that makes step 8 read the names of step 6.
- Field offsets come from a shipped PDB, the user's own SDK dump or a [structure dissect](dissect-structure.md).
  Dumpers (Dumper-7, UE4SS) inject a DLL: the user's choice, never run here; check dumped offsets on this build.
- A writer that serves every actor needs the [shared code filter](shared-code-filter.md) before any patch.
- After an update, find a holder again through `aob_generate_signature(address="<code that reads it>")`; a Shipping
  exe over 64 MiB gets generator `managed` ([signatures](../Documents/unreal-engine.md#signatures-after-an-update)).

## Pitfalls

- Level loads and garbage collection free objects and reuse slots: re-resolve from `GWorld`, never keep heap addresses.
- Positions have several copies (root component, cached transforms): prove which one the game obeys.
- RTTI is usually off: name classes through the name pool, not `memory_get_address_info`.

## Report

A table of global, module+offset and check (name pool, `GWorld`), the version, `pe.timeDateStamp` and the chain to
`{goal}`. What remains: the records, and the symbols (a saved table omits them) until
`symbol_unregister(name="ueNamePool")` and `symbol_unregister(name="ueWorld")`, or `runtime_release_resources()`,
which releases everything MCP holds. Background:
[Unreal Engine](../Documents/unreal-engine.md), [pointers](../Documents/pointers.md) and
[structures](../Documents/structures.md).
