# Dissect a structure

## Goal

Understand the object at `{baseAddress}` (first `{size}` bytes, default 256): field offsets, types and names, optionally
compared with the bases listed in `{compareWith}`.

## Steps

1. `memory_get_address_info(addresses=["{baseAddress}"], includeRtti=true)`: readable region, module or heap, and a C++
   class name (`rttiClass`) when available.
2. Pick the best source of types:
    - .NET or Mono object: `structure_fill_from_dotnet(name="<class>", address="{baseAddress}", createIfMissing=true)`
      gives real field names (the game must not be paused or stopped in the debugger).
    - PDB symbols and a known type: `structure_get_pdb_layout(typeName="<type>")`, then
      `structure_create(name="<type>", pdbTypeName="<type>")`.
    - Otherwise: `structure_autoguess(name="<name>", address="{baseAddress}", size={size}, createIfMissing=true)`.
3. `structure_read(name="<name>", addresses=["{baseAddress}", <each base in {compareWith}>])`: values side by side.
4. Ask the user to change something in game (health, position, ammo) and read again; changed fields reveal their
   meaning. A known value's address minus `{baseAddress}` is its offset (`util_calculate`).
5. With `{compareWith}`:
   `structure_compare(groupA=["{baseAddress}"], groupB=[<bases>], structureName="<name>", mode="discriminate")`.
6. Name and retype confirmed fields: `structure_update_elements(name="<name>", updates=[{index, name, valueType}])`.
7. Follow pointer fields (`childStructure`) by dissecting the pointed object the same way.
8. Ask whether to keep the structure; if not, `structure_delete(name="<name>")`.

## Decisions

- An 8-byte field at offset 0 that points into a module's read-only data is usually a vtable (C++ object).
- A value that disagrees with the game after a change: the guessed type is wrong (float vs int32, or two int16).
- Objects of different classes: compare only instances of the same class.
- Editing a value in the structure is a write: use `structure_write_element` only with consent.

## Pitfalls

- Autoguess is only a guess; confirm each field before naming it.
- Structures belong to CE: they outlive the plugin and `runtime_release_resources()` does not remove them; delete them
  explicitly.
- Strings are often pointers to text, not inline characters.
- The object may be longer than `{size}`; extend it when fields continue.

## Report

The layout table (offset, type, name, confidence), the class name, the instances compared, and the structure left in CE
(or deleted with `structure_delete`). No other resource is owned. See [structures](../structures.md)
and [Mono and .NET](../mono-and-dotnet.md).
