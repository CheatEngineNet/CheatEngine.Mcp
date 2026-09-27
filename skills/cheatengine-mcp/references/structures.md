# Structures and dissect

A structure is CE's named layout of an object in memory: a list of elements, each with an offset, name, `valueType`, display and size. Use structures to label an object you found, read many instances side by side, and find the field that tells entities apart.

## Find a base address

- From a writer instruction: `mov [rbx+000004C8],eax` means the object starts at `RBX` and the value is the field at `4C8` (see [debugger](debugger.md)).
- From a pointer chain: the address before the last offset (see [pointers](pointers.md)).
- From an injection copy (`[playerBase]`) or a managed runtime (see [mono-and-dotnet](mono-and-dotnet.md)).
- `memory_get_address_info(addresses=[base], includeRtti=true)` reports the region and module and, for C++ objects compiled by MSVC with RTTI and virtual functions, the class name in `rttiClass`. The first pointer-sized field of such an object is its vtable.

## Create a layout

| Source | Tool | Notes |
|---|---|---|
| CE's guess | `structure_autoguess(name, address, offset="0", size, createIfMissing=true)` | fast first draft; a guess |
| explicit fields | `structure_create(name, elements=[...])` | offsets in hex, `valueType`, `display` (`unsigned`, `signed`, `hex`) |
| copy | `structure_create(name, cloneFrom="Player")` | start a variant |
| PDB symbols | `structure_get_pdb_layout(typeName)`, then `structure_create(name, pdbTypeName=typeName)` | exact layout when symbols are loaded |
| .NET object | `structure_fill_from_dotnet(name, address, rename=false)` | layout from CE's out-of-process .NET collector |

- Autoguess only looks at the current bytes. It mislabels pointers as integers, floats as integers, zeros and padding as fields, and misses strings. Treat every guessed type as unconfirmed until a read or an in-game change supports it.
- Pick `size` (a decimal byte count) to cover the object, not its neighbours: start with 1024-4096 bytes (hex 400-1000). Reading past the object shows the next allocation, which is noise.
- PDB layouts need symbols: load a game-shipped PDB with `symbol_add_module(path, baseAddress, enumStructures=true)`, then check `found` in `structure_get_pdb_layout`.
- `structure_fill_from_dotnet` refuses with `busy` while the target is paused or stopped in the debugger. It reads the object's managed type, so give it the object address, not a field address.
- For Mono (Unity) games, take field offsets from `mono_list_fields` and build the structure with `structure_create(elements=...)`.

## Read and name fields

- `structure_read(name, addresses=[player, enemy1, enemy2], fromOffset, toOffset)` returns one column per address (up to 16) and one row per element, like CE's dissect window. Unreadable values come back as null.
- Identify a field by cause and effect: read, change one thing in game (take damage, move, spend money), read again, and see which row changed.
- Rename and retype confirmed fields with `structure_update_elements(name, updates=[{index, name, valueType, display}])`. The batch stops at the first failure with `partial_effect`; re-read with `structure_get` before retrying.
- Add missed fields with `structure_add_elements` and drop noise with `structure_remove_elements(indices=[...])`.
- Nest objects: give a `pointer` element a `childStructure` so the layout of the pointed-to object is known.
- `structure_write_element(name, address, index, value)` writes the target through an element and reads it back. It is a mutation: after an error, check `hostEffect` before trying again.

## Compare groups to find a discriminator

Shared code (one instruction serving the player and enemies) needs a field that says whose object it is. CE's dissect window does this by comparing groups; `structure_compare` does it without the UI.

1. Collect bases: the player (and allies, if any) for group A, two or more enemies for group B. Capture the shared instruction with `debugger_start_capture(trigger="access")` and compute each hit's base from its registers.
2. `structure_compare(groupA=[...], groupB=[...], size=1024, granularity=4, interpretAs="auto", mode="discriminate")`, or pass `structureName` to compare along an existing layout.
3. Read `rows`: `discriminator` rows are constant inside each group but different between groups; `constant` rows are equal everywhere; `varies_a`/`varies_b` change inside a group.
4. Prefer, in order: a team or faction id, an is-player or controller flag, an owner pointer, the vtable pointer (a different class), a name string.
5. Validate: more than one member per group, a second fight or level, and a restart. A discriminator from one sample of each side is often a coincidence.
6. Use it in the filter: `cmp dword ptr [rbx+14],1` then `jne` to the original code (see [auto-assembler](auto-assembler.md) and [x64-injection](x64-injection.md)).

When no field separates the groups, compare the captured registers across hits instead: a register that differs between player and enemy calls can serve as the filter (see [debugger](debugger.md)).

## Ownership and cleanup

- Structures belong to CE, not to MCP. They are global: `structure_list` shows those the user made too, they outlive plugin disable and MCP never deletes them on its own.
- CE saves global structures into the cheat table on `table_save`, and a loaded table can bring its own (see [cheat-tables](cheat-tables.md)).
- Delete temporary structures you created with `structure_delete(name)` at the end of the session. Never delete a structure you did not create unless the user asks.
- Names are case-sensitive. Check `structure_list(nameContains=...)` before creating, to avoid clobbering the user's work.
- Layouts change with game updates. Re-verify key offsets after an update before trusting a saved structure.

## Sources

- https://wiki.cheatengine.org/index.php?title=Help_File:Dissect_data/structures
- https://fearlessrevolution.com/viewtopic.php?t=137
- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://wiki.cheatengine.org/index.php?title=Tutorials:Create_cheat_table_full:health_hack
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt
- https://learn.microsoft.com/cpp/cpp/run-time-type-information
