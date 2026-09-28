# Dissect a structure

## Goal

Name the fields of the object at `{baseAddress}` over its first `{size}` bytes (default 256), compared with the
instances in `{compareWith}` when given. Reading changes nothing in the game.

## Steps

1. `memory_get_address_info(addresses=["{baseAddress}"], includeRtti=true, includePointerValue=true)`: `region` must
   be readable. `rttiClass` names an MSVC C++ class; a `pointerValue` into a module's `.rdata` is likely a vtable.
2. Layout source, best first, under a new name (`structure_list(nameContains="<name>")`):
   - .NET (CLR), when `dotnet_get_status()` reports `attached`: `dotnet_get_object(address="{baseAddress}")` gives
     `typeName` and offsets, then
     `structure_fill_from_dotnet(name="<name>", address="{baseAddress}", createIfMissing=true)`.
   - Mono (Unity), when `mono_get_status()` reports `attached`: `mono_get_object(address="{baseAddress}")` gives
     `className` and `fields` (it refuses generic and array objects; for those or a known class, `mono_find_class`,
     then `mono_list_fields(classHandle="<handle>", includeParents=true)`), then
     `structure_create(name="<name>", elements=[{offset="<hex>", name="<field>", valueType="<type>"}])`
     from the non-static fields (decimal `offset` in hex; `elementType` 8 int32, 12 float, 18 pointer). Not attached:
     attaching injects code, see [Unity Mono recon](unity-mono-recon.md).
   - PDB: `structure_get_pdb_layout(typeName="<type>")`; with `found` true,
     `structure_create(name="<name>", pdbTypeName="<type>")`.
   - Otherwise: `structure_autoguess(name="<name>", address="{baseAddress}", size={size}, createIfMissing=true)`.
3. `structure_read(name="<name>", addresses=["{baseAddress}", <each base in {compareWith}>])`: one column per base,
   16 at most.
4. Meanings: `memory_create_snapshot(name="dissect-a", address="{baseAddress}", size={size})`, the user changes one
   thing (damage, moving), snapshot `dissect-b` likewise, then
   `memory_compare_snapshot(name="dissect-a", compareTo="dissect-b", valueType="int32")`: each change's `offset` is a
   field (set `memory_compare_snapshot.valueType` to float for floats).
5. With `{compareWith}`:
   `structure_compare(groupA=["{baseAddress}", <each base in {compareWith}>], structureName="<name>", mode="differs")`
   lists the per-object fields; to set `{baseAddress}` apart:
   `structure_compare(groupA=["{baseAddress}"], groupB=[<bases>], structureName="<name>", mode="discriminate")`.
6. `structure_get(name="<name>")` for indices, then name and retype confirmed fields:
   `structure_update_elements(name="<name>", updates=[{index=12, name="health", valueType="float"}])`, one update per
   element. An 8-byte value split in two: `structure_remove_elements(name="<name>", indices=[13])`, then retype 12.
7. Pointer field: dissect its target likewise, remove the element, then
   `structure_add_elements(name="<name>", elements=[{offset="<hex>", name="<field>", valueType="pointer", childStructure="<child>"}])`.
8. Clean up: `memory_delete_snapshot(name="dissect-a")` and `dissect-b`. Ask whether to keep the structure (it is
   saved with the table); if not, `structure_delete(name="<name>")`.

## Decisions

- Compare only one class: same vtable, `rttiClass`, `typeName` or `className`.
- `mono_get_object` reports `offsetInObject` above 0: `{baseAddress}` lies inside the object; use its `address`.
- A field disagrees with the game: try float or int32, two int16, one double or pointer.
- One instance per side makes every differing cell a discriminator: add instances.
- Writing a field: read it, ask, then
  `structure_write_element(name="<name>", address="{baseAddress}", index=12, value="<value>")`.

## Pitfalls

- Autoguess reads current bytes: never int64 or bool, a null pointer or zero double as two zero ints, floats
  past ±100000 as hex ints, four printable bytes as text. Confirm each field.
- `structure_fill_from_dotnet` refuses (`busy`) while the game is paused or at a breakpoint, can block Cheat
  Engine for seconds, and adds to an existing structure.
- Beyond `<end>`: `structure_autoguess(name="<name>", address="{baseAddress}", offset="<end>", size=<n>)` reads at
  address plus offset. Past the object lies the next allocation.
- After a removal or `partial_effect`, `structure_get` before the next change.
- Structures belong to Cheat Engine: `runtime_release_resources` skips them. Delete only yours.

## Report

A layout table (offset, type, name, evidence, confidence), the class, instances compared, any discriminator, and the
structure kept or deleted. See [structures](../Documents/structures.md) and
[Mono and .NET](../Documents/mono-and-dotnet.md).
