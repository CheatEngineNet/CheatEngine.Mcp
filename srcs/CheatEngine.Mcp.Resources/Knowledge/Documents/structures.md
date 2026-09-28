# Structures and dissect

A structure is Cheat Engine's named layout of an object in memory, as in its Structure Dissect window: elements with an
offset, a name, a `valueType`, an integer display and a size. Use one to label an object you found, read several
instances side by side, find the field that tells entities apart, and export the layout as C. Scalar encodings (floats,
flags, strings) are in [value-types](value-types.md); the guided version is
[dissect_structure](../Workflows/dissect-structure.md).

## Find and identify the base

- From a writer instruction: `mov [rbx+000004C8],eax` usually means the object starts at `RBX` and the value is the
  field at `4C8` ([debugger](debugger.md), [find_writer](../Workflows/find-writer.md)).
- From a known field: address parameters take expressions, so `<field>-4C8` is the base.
- From a pointer chain: the address before the last offset ([pointers](pointers.md)). From an injection copy:
  [injection_copy_base](../Workflows/injection-copy-base.md). Managed objects: [mono-and-dotnet](mono-and-dotnet.md).

Then `memory_get_address_info(addresses=["<base>"], includeRtti=true, includePointerValue=true)`:

- `region` should be committed and readable, usually `private` (heap); a base inside a module image is a static object.
- `pointerValue` is the first pointer-sized field. When a second `memory_get_address_info` call puts it inside a module
  with a read-only `section` such as `.rdata`, it is a vtable and the object is a C++ class instance.
- `rttiClass` comes from CE's `getRTTIClassName`. For MSVC objects it reads the Complete Object Locator at vtable[-1],
  which must lie inside a loaded module, and keeps only names of types declared `class` (`.?AV`); it also tries a Free
  Pascal class layout. Types declared `struct`, builds without RTTI (`/GR-`; Unreal Engine disables RTTI by default),
  classes without virtual functions and vtables outside any module (JIT code) give no name. Before trusting a name,
  check that the address is the object start, not a field.

## Choose a layout source

| Object | Call |
|---|---|
| .NET (CoreCLR, .NET Framework) | `structure_fill_from_dotnet(name="<class>", address="<object>", rename=false)` |
| Mono or IL2CPP (Unity), from an address | `mono_get_object(address="<object or scan hit>")`, then `structure_create` with its instance fields |
| Mono or IL2CPP (Unity), a known class | `mono_find_class(namespace="...", className="...")`, `mono_list_fields(classHandle="<handle>", includeParents=true)`, then `structure_create` with those offsets |
| Loaded PDB type | `structure_get_pdb_layout(typeName="<type>")`, then `structure_create(name="<type>", pdbTypeName="<type>")` |
| Fields you know | `structure_create(name="Player", elements=[{offset="4C8", name="health", valueType="float"}])` |
| A variant | `structure_create(name="Boss", cloneFrom="Enemy")` |
| Anything else | `structure_autoguess(name="Player", address="<base>", offset="0", size=1024, createIfMissing=true)` |

- `structure_fill_from_dotnet` uses CE's .NET data collector (a separate process), for .NET only: give the object
  address, not a field. It refuses with `busy` while the target is paused or stopped in the debugger and can block CE
  for seconds. It adds a `Vtable` element at 0 and one element per instance field to the structure without removing
  anything, so a second call duplicates them. With `structure_fill_from_dotnet.rename` true the structure takes the .NET
  class name: use the returned `name`.
- Mono needs the collector ([unity_mono_recon](../Workflows/unity-mono-recon.md)); `mono_attach` injects it (target
  code execution; ask first).
- `mono_get_object` is the way from an address whose class you do not know. It searches down from the address, at
  most `mono_get_object.maxBacktrack` bytes (default 4096), for the object start, reading target memory only. It
  returns the start `address`, `offsetInObject` (how far the given address lies inside the object), `className`,
  `classHandle` and `fields`, inherited ones first and statics included: skip the `isStatic` ones. It refuses
  generic-instance and array objects with `unsupported`, and returns `not_found` when no known object start lies in
  range ([mono-and-dotnet](mono-and-dotnet.md)).
- `mono_get_object` and `mono_list_fields` give decimal offsets from the object start (instance fields begin at +0x10
  on x64): write them as hex for `structure_create`. `mono_list_fields.nameContains` keeps only the fields whose name
  contains that text. For a value type (struct), Mono's field offsets still include the 0x10 header: subtract 0x10
  where the struct is embedded inline, as CE's `monoscript.lua` does. IL2CPP games: [unity-il2cpp](unity-il2cpp.md).
- PDB layouts need symbols with structures: `symbol_add_module(path="<file.pdb>", baseAddress="<module base>",
  enumStructures=true)`, then check `found` and `truncated` in `structure_get_pdb_layout`. A create refuses a type
  with more than 4096 fields (`limit_exceeded`). Shipped game PDBs are rare; Windows types such as `_PEB` are the
  usual use.
- `structure_create` is all or nothing, takes at most 1024 elements and refuses a taken name with `invalid_state`.
  Offsets are signed hexadecimal text; `byteSize` is required for `string`, `wstring` and `bytes` and refused for
  other types. `display` must match the type: `signed` for `intN`, `unsigned` or `hex` for `uintN`.
- A `childStructure` must already exist, so a self-referencing layout is created first and its pointer added after.

**Autoguess is a draft.** It types the current bytes only, with the dissect window's rules (`byteinterpreter.pas`):

- It never produces 8-byte integers, bools or bit fields. A 64-bit counter shows as two 4-byte ints, a null pointer as
  two zero ints, 0.0 as a zero int.
- A float must be non-zero and within ±100000 without an exponent: large coordinates and tiny values become ints,
  often shown in hex.
- 4 printable ASCII bytes become a string, so a packed int such as `0x41424344` can read as text. Addresses that are
  not 4-aligned become bytes or words.
- On x64 a value becomes a pointer only at an 8-aligned address: when CE names it as a module or symbol address
  (vtables, function pointers) or, if it is not float-like, when it points to readable memory. A pointer to an object
  with an RTTI name is named "Pointer to instance of <Class>" and gets the structure of that exact name as its child
  when one exists.
- Keep `structure_autoguess.size` (1-65536, default 4096) to the object, usually 1024-4096 bytes. Reading past it
  shows the next allocation; a vtable-like value mid-range may be the next object.
- CE adds the guessed elements without removing existing ones, so guessing into an existing structure can duplicate or
  overlap elements. Guess into a new name when the user's layout must stay as it is.
- CE reads the bytes at `address` plus `structure_autoguess.offset` and labels the new elements from that offset, as
  its own dissect window does: pass the object's base, not base+offset. The offset is hexadecimal, zero or greater. To
  extend a layout from 0x400:
  `structure_autoguess(name="Player", address="<base>", offset="400", size=512, createIfMissing=false)`.

## Read, name and retype fields

1. `structure_read(name="Player", addresses=["<player>", "<enemy 1>", "<enemy 2>"], fromOffset="0", toOffset="1FF")`
   returns one column per address (up to 16) and one row per element, 100 rows per page by default (`limit` up to
   256; continue with `structure_read.offset` from `nextOffset`). Integers come back in decimal even with a `hex`
   display, pointers in hex, and null where a column is unreadable past its `confirmedLength`.
   `structure_read(name="Player", addresses=["<player>"], format="detailed")` adds each element's `byteSize`, `display`
   and `raw` bytes. One page spans at most 65536 bytes.
2. Change one thing in game (take damage, move, spend money), read again, and see which row changed. For a whole range,
   `memory_create_snapshot(name="before", address="<base>", size=1024)`, act, take a second snapshot named `after`
   the same way, then `memory_compare_snapshot(name="before", compareTo="after", valueType="int32", change="changed")`:
   each change's `offset` is a field offset (set `memory_compare_snapshot.valueType` to float for floats). Snapshots are
   MCP's own; free them with `memory_delete_snapshot`.
3. Name and retype confirmed fields with
   `structure_update_elements(name="Player", updates=[{index=12, name="health", valueType="float"}])`. Indices come
   from `structure_get` and refer to the state before the call; each index may appear once (a repeat is
   `invalid_argument` before anything runs; combine its changes). `display` alone also sets signedness: `signed`
   turns a `uint32` into an `int32`, `hex` an `int32` into a `uint32`.
4. Add, update and remove take up to 256 elements per call. `structure_add_elements` is all or nothing, returns where
   each element landed in `indices`, and adds them again if repeated.
   `structure_remove_elements(name="Player", indices=[3, 7])` takes distinct indices.
5. Updates and removals stop at the first refusal with `partial_effect` (`details.applied`, `details.failedIndex`):
   re-read with `structure_get` before retrying ([errors-and-recovery](errors-and-recovery.md)).
6. Elements stay sorted by offset, so indices shift after a move, add or remove: re-read before the next batch.
7. Nest objects: a `pointer` element with a `childStructure`. No update changes the child; remove the element and add
   it again with `childStructure` set. `structure_get(name="Player", format="detailed")` shows child starts, custom
   types and bit fields made in CE.

`structure_write_element(name="Player", address="<base>", index=12, value="100")` writes the target through an element
and reads it back. Read the old value first so it can be restored, and ask before writing. After an error, check
`hostEffect` before trying again.

## Recognise common data

Scalar clues (float bit patterns, bools, bit-field masks, pointer ranges, strings) are in [value-types](value-types.md).
Object-level patterns on x64 (heuristics: confirm each in game):

| Pattern | Looks like | Confirm |
|---|---|---|
| vtable | +0 points into a module's `.rdata`; its slots point into `.text` | `mov rax,[rcx]` then `call [rax+NN]` calls slot NN/8 |
| Pointer | 8-aligned; high dword at most `7FFF`, so two ints with a small second one | retype as `pointer`, add a child |
| Health and max | often adjacent, same type, current first | damage changes current only; level-ups change max |
| Position (vec3) | 3 floats (12 bytes, often padded to 16); UE5: 3 doubles | move on one axis; a velocity vec3 nearby is non-zero only while moving |
| Euler angles | degrees (yaw 0-360 or ±180, pitch about ±89) or radians (±π) | turn a full circle and watch the wrap |
| Quaternion | x, y, z, w each in [-1, 1], length 1, identity (0,0,0,1) | q and -q are the same rotation |
| 4×4 matrix | 16 floats; unscaled 3×3 rotation in [-1, 1]; translation in elements 12-14; elements 3, 7, 11, 15 are 0,0,0,1 | a world matrix's translation is the position |
| `std::vector<T>` | first, last, end pointers | count = (last - first) / sizeof(T) |
| `std::list`, `std::map` (MSVC) | head pointer, size; list node next, prev, value at +0x10; map node value at +0x20 (8-aligned values) | the head is a sentinel node |
| `std::shared_ptr` (MSVC) | object pointer, control-block pointer | use count at control block +8 |
| Unreal `TArray<T>` | Data pointer, int32 Num, int32 Max (16 bytes) | Num ≤ Max |
| Managed 1-D array | Mono or IL2CPP: length +0x18, elements +0x20; CoreCLR: length +0x8, elements +0x10 | length matches the item count |
| Linked list | next pointer at a fixed offset | ends in null or cycles; doubly linked: next's prev is the node |

- Managed instance fields start after the header: +0x8 on CoreCLR, +0x10 on Mono and IL2CPP.
- MSVC aligns int32 and float to 4, int64, double and pointers to 8, `__m128` to 16, unless the code uses
  `#pragma pack`; a pointer at an offset that is not a multiple of 8 is suspect. The access instruction tells the type:
  `movss` float, `movsd` double, `movzx r,byte ptr` a byte or bool ([code-analysis](code-analysis.md)).
- Engine conventions (Unity Y-up metres, Unreal Z-up centimetres, UE5 doubles): [game-engines](game-engines.md),
  [unreal-engine](unreal-engine.md); coordinates: [find_position](../Workflows/find-position.md).

## Compare instances to find a discriminator

Shared code (one instruction serving the player and enemies) needs a field that says whose object it is
([shared_code_filter](../Workflows/shared-code-filter.md)).

1. Collect bases. After consent to attach the debugger,
   `debugger_start_capture(address="<shared instruction>", trigger="execute", groupByEffectiveAddress=true)`, play,
   then `debugger_poll_capture(jobId=..., afterSequence=0)`: each item is one accessed address, `effectiveAddress`,
   with its `hitCount`, so a base is `effectiveAddress` minus the displacement (4C8 for `[rbx+4C8]`). This is CE's
   "Find out what addresses this instruction accesses"; it refuses `lea`, `fs:`/`gs:` operands and instructions
   without exactly one memory operand ([debugger](debugger.md)). Without grouping, each hit's registers give a base
   (`RBX`). Stop it with `runtime_stop_job(jobId=...)`.
2. `structure_compare(groupA=["<player>", "<ally>"], groupB=["<enemy 1>", "<enemy 2>"], size=1024, granularity=4, interpretAs="auto", mode="discriminate")`,
   or `structure_compare(groupA=[...], groupB=[...], structureName="Entity")` to compare along a layout's elements.
3. Read each row's `classification`: `discriminator` (constant inside each group, different between them),
   `constant`, `varies_a`, `varies_b`, `varies_both` or `unreadable`. Page with `structure_compare.offset` and
   `nextOffset` (`limit` up to 128).
4. Prefer, in order: a team or faction id, an is-player or controller flag, an owner pointer, the vtable (a different
   class), a name string.
5. Validate: two or more members per group, a second fight or level, and a restart. With one instance per side every
   differing cell is a discriminator.
6. Filter with it: `cmp dword ptr [rbx+14],1` then `jne` to the original code ([auto-assembler](auto-assembler.md),
   [x64-injection](x64-injection.md)).

- The comparison is byte for byte; `structure_compare.interpretAs` only changes how size-mode values are shown.
- CE's dissect window colours group columns by their displayed values: green (equal inside the column's group), red
  (varies inside it), and with two or more consistent groups blue (the same in every group) or purple (different
  between groups: the discriminator). `structure_compare` returns no colours: read its rows.
- It compares the bytes at each base only. CE's Structure Compare window can follow pointers; here, read the pointer
  field and compare the child objects.
- With `structure_compare.groupB` omitted, `structure_compare.mode` constant or differs shows what instances of one
  class share or not.
- No field separates the groups: compare the captured registers across hits instead ([debugger](debugger.md)).

## Find more instances

- Same class: the vtable identifies it. `pointer_find_references(target="<vtable>", maxOffset=0, limit=1000)` scans
  once (CE is blocked meanwhile) and lists the holders; each heap holder is a candidate object base, since the vtable
  sits at +0. Check `truncated`. Freed or stale objects can match: read them before use.
- Inline arrays repeat every stride (the address difference, or a code multiplier such as `imul r,r,1A8h`): check with
  `structure_read(name="Entity", addresses=["<b>", "<b>+1A8", "<b>+350"])`, whose columns must line up.
- Pointer arrays are 8-aligned pointers to same-class objects, nulls as free slots, a count nearby
  ([find_entity_list](../Workflows/find-entity-list.md)). Objects by several known values:
  [group_scan](../Workflows/group-scan.md).
- Managed runtimes: `mono_start_instance_search` (with the class handle that `mono_get_object` or `mono_find_class`
  returns) or `dotnet_start_instance_search`. Both are jobs: poll with `mono_poll_instance_search` or
  `dotnet_poll_instance_search`, then `runtime_stop_job`. Mono results are candidates: read each before use.

## Rename and export

- `structure_set_name(name="Entity", newName="Enemy")` renames in place: pointer elements of other structures keep
  their child, unlike a `cloneFrom` copy plus a delete. A name any structure already has (internal ones included) is
  `invalid_state`; the current name is a no-op. A global structure is saved with the table under its new name; an
  internal one is never saved. Rename only what you created, or with the user's agreement.
- `structure_generate_c_header(names=["Player"], generator="managed")` returns `text`, the `generator` used and
  `structures`: the requested ones, then every child their pointer elements reach. Omit the generator to use CE's own
  (`generate_c_header` from `autorun/structureExportToCHeader.lua`, behind its "Export to C header file" menu) when
  present, else managed. Managed also takes over when CE's generator fails or the element sizes add up to more than
  1 MiB; with `structure_generate_c_header.generator` set to cheat_engine those cases are refused. CE's generator
  writes byte-array, bit-field and custom elements as one `int`, which shifts the layout. Limits: 1-64 distinct names,
  256 structures with children, 8192 elements, 1 MiB of text.
- Managed writes fixed-width types, offset comments, `pack(1)`, padding arrays for gaps and a commented
  `static_assert` size check; compile it for the target's bitness, since pointer members take the compiler's size.
  - A nested child (set in CE's dissect window) is embedded as its C type only when the element's size equals the
    child's size and none of the child's members reaches past it; otherwise it is a byte array commented
    "nested X as bytes", so later members keep their offsets.
  - CE's structure size ends at the element with the highest offset, so an earlier, longer element can make the C
    struct larger: a comment "An element ends at 0x.., past Cheat Engine's size 0x.." then follows the struct, and
    the size check states the real C size.
  - A pointer that lands inside its child (a non-zero child start) is a `uint8_t *` commented "pointer to X+0xN".
  - An element C cannot place (a negative offset, no size, or an overlap such as a union or a bit field sharing
    storage) becomes a comment line. Names that are C or C++ keywords or `stdint.h` names such as `SIZE_MAX` gain a
    trailing underscore, and repeated names `_2`, `_3`.

## Ownership and cleanup

- Structures belong to CE, not to MCP: they are global, shared with the user, and outlive a plugin disable or process
  switch. `runtime_list_resources` does not list them and `runtime_release_resources` does not remove them.
- Check `structure_list(nameContains="Player")` before creating (names are case-sensitive; the filter ignores case).
- `structure_create(name="Vec3", elements=[...], internal=true)` makes an internal structure: selectable as a child,
  hidden from `structure_list`, never saved, still readable with `structure_get`. CE documents it as mostly read-only
  once flagged, so pass its elements in the creating call.
- `table_save` writes the global structures into the table. Unless `table_load.merge` is true, a load makes CE free
  every global structure, yours included, before it adds the table's own (a .CETRAINER always replaces); save or
  re-create what you need first ([cheat-tables](cheat-tables.md)).
- Live views: `cheatengine://instance/structures` and `cheatengine://instance/structures/Player`, both resource
  templates whose name completion offers the first 1000 structure names (through the gateway,
  `cheatengine://instances/{instanceId}/` replaces `cheatengine://instance/`).
- Keep the names you create. At the end, ask, then `structure_delete(name="Player")`: there is no undo, and pointer
  elements of other structures lose that child. Never delete a structure you did not create unless the user asks
  ([cleanup_session](../Workflows/cleanup-session.md)).
- Game updates move fields: re-verify key offsets before trusting a saved layout
  ([repair_after_update](../Workflows/repair-after-update.md)).

Reading and naming change nothing in the game. `structure_write_element` and filters built on a discriminator do: only
on single-player or offline software the user owns or may modify, never on online, competitive or anti-cheat-protected
games, and only after explaining each change ([safety](safety.md)).

## Sources

- https://wiki.cheatengine.org/index.php?title=Help_File:Dissect_data/structures
- https://fearlessrevolution.com/viewtopic.php?t=1865
- CE source (github.com/cheat-engine/cheat-engine, `Cheat Engine/`): `StructuresFrm2.pas`, `LuaStructure.pas`,
  `byteinterpreter.pas`, `rttihelper.pas`, `frmstructuresconfigunit.pas`, `OpenSave.pas`; local CE 7.7 `celua.txt`,
  `autorun/structureExportToCHeader.lua`, `autorun/monoscript.lua`
- https://learn.microsoft.com/cpp/cpp/run-time-type-information
- https://learn.microsoft.com/cpp/build/x64-software-conventions
- https://learn.microsoft.com/cpp/c-language/storage-and-alignment-of-structures
- https://devblogs.microsoft.com/oldnewthing/20230802-00/?p=108524 (vector) ;
  https://devblogs.microsoft.com/oldnewthing/20230804-00/?p=108547 (list) ;
  https://devblogs.microsoft.com/oldnewthing/20230807-00/?p=108562 (map) ;
  https://devblogs.microsoft.com/oldnewthing/20230821-00/?p=108626 (shared_ptr)
- https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/object.h ;
  https://github.com/mono/mono/blob/main/mono/metadata/object-internals.h
- https://dev.epicgames.com/documentation/en-us/unreal-engine/large-world-coordinates-in-unreal-engine-5
- https://docs.unity3d.com/ScriptReference/Quaternion.html
