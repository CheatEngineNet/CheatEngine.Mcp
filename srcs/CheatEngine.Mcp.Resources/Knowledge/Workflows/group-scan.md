# Find an object by several known values

## Goal

Find the object that holds `{values}` (each `type:value@hexOffset`) with one byte-pattern search in `{region}` memory
(`heap`: writable private memory), narrow the hits while the user changes one value, then verify the object and find
its owners. The searches only read; records need the user's consent.

## Steps

1. `process_get_current()`: confirm the target and its `pointerSize`. Parse `{values}` (memory types such as `int32`,
   `float`, `wstring`) and sort them by offset. A hit is where the smallest offset lies: its base is the hit minus
   that offset, and a field sits at `<base>+<offset>`.
2. Each value's bytes: `util_convert_value(value="100", sourceType="int32", targetType="bytes")` gives `64 00 00 00`
   (util_convert_value.pointerSize 4 for a 32-bit target's pointer).
3. One pattern: each value's bytes at its offset minus the smallest, `??` for every byte between.
   `int32:100@0,int32:12@10` becomes `64 00 00 00`, twelve `??`, `0C 00 00 00` (offset 10 is hexadecimal: 16
   bytes). At most 4096 positions.
4. Search by `{region}`. Except `module`, each pattern scans the whole target and blocks Cheat Engine meanwhile; tell
   the user first. Keep aob_find.alignment at 4 only when the first value is 4 or 8 bytes wide.
   - `heap`: `aob_find(patterns=["<pattern>"], writable="required", executable="excluded", alignment=4, limit=200)`,
     then `memory_get_address_info(addresses=["<hit1>", "<hit2>"])` keeps hits whose region type is `private`;
   - `module`: `aob_find(patterns=["<pattern>"], module="<module>", writable="required", alignment=4, limit=200)`,
     the main module unless the user names another;
   - `any`: `aob_find(patterns=["<pattern>"], includeMapped=true, alignment=4, limit=200)`, which adds mapped
     memory (file views, emulated RAM) and ends other scan-region overrides.
5. The hits are `matches` of `results[0]`; check its `count` and `exact`. Several hits: the user changes one value
   in game, then `memory_read_batch(items=[{address="<base1>+10", valueType="int32"}, ...])` keeps the hits whose
   field shows the new value. Repeat with another value.
6. Verify: `memory_read_samples(addresses=["<base>", "<base>+10"], valueType="int32", durationMs=5000)` (one call
   per type) while the user plays; every field follows its game value.
7. `memory_get_address_info(addresses=["<base>"], includeRtti=true)` names an MSVC class when the base is the object
   start; on Unity, when `mono_get_status()` reports `attached`, `mono_get_object(address="<base>")` names the class.
   `pointer_find_references(target="<base>", limit=20)` lists its holders (a `symbol` is a static root). Then
   [dissect the structure](dissect-structure.md).
8. With consent: `record_create(records=[{description="<field>", address="<base>+10", variableType=2}])`, one entry
   per field with its type's code (0 to 3 for 1-, 2-, 4- and 8-byte integers, 4 float, 5 double; text is 6 with
   `length=<characters>`, plus `unicode=true` for `wstring`); with no `value`, nothing is written. Keep the ids.

## Decisions

- Pick distinctive values (37 ammo, not 0, 1 or 100); two or three fields suffice.
- Floats match exact bits only: use a value the game sets exactly (100.0, 0.5), or `??` for its bytes.
- Pointers change every run: use `??`. An MSVC vtable at +0 is module+offset:
  `symbol_resolve(expressions=["game.exe+<rva>"])` gives this run's value to include as `pointer`.
- Unknown offsets: find each value with [find a known value](find-known-value.md); the offset is
  `util_calculate(expression="0x<a2> - 0x<a1>")`.
- Several real hits (every enemy shares the layout): choose values only the wanted one has.
- Big-endian guest RAM (GameCube, Wii, PS3): util_convert_value.byteOrder `big_endian` gives the bytes; read the
  fields and keep records as in [emulator memory](emulator-memory.md); plain records show them swapped.
- Cheat Engine's grouped scan (ranges, any order) needs a [Lua script](write-lua-script.md).

## Pitfalls

- The values must not change while a search runs.
- Zeros and common bytes match everywhere: leave such fields out, above all at the pattern's start.
- A wrong alignment drops real hits; when unsure, leave aob_find.alignment out.
- `exact` false: the limit cut the list (raise aob_find.limit or add a value) or, with `count` 0, the scan may have
  failed (search again).
- Heap addresses, and records on them, last one run: search again after a restart or anchor a pointer path.

## Report

A table of base address, region (heap, or module+offset), class, each field (offset, type, value), proof,
holders and record ids. aob_find keeps no scanner state; records stay until `record_delete(ids=[<id>])`. See
[value scans](../Documents/value-scans.md), [value types](../Documents/value-types.md) and
[structures](../Documents/structures.md).
