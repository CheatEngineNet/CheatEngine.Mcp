# Unreal Engine 4 and 5 targets

Unreal Engine (UE) games keep every object in a global object array, every name in a global name table and the current
level in `GWorld`: better roots than a blind pointer scan. Read this after
[engine triage](../Workflows/engine-triage.md) names Unreal; the guided version is
[unreal_recon](../Workflows/unreal-recon.md).

Most steps only read; ask consent before a write, a debugger capture or a Lua script. Work only on single-player or
offline games the user owns or may modify. Some UE games ship Easy Anti-Cheat or BattlEye (an `EasyAntiCheat` or
`BattlEye` folder in the game): stop there ([safety](safety.md)).

The layouts below are typical x64 ones from public research and Cheat Engine's UnrealEngineTools. Forks, versions and
patches change them: each offset is UNVERIFIED for the build in front of you until a check on a known object passes.

## Recognise UE and its version

- **Process.** The root `<Project>.exe` is a small launcher; the game is
  `<Project>/Binaries/Win64/<Project>-Win64-Shipping.exe`. Find it with `process_list(nameContains="Shipping")`, then
  `process_attach(process="<Project>-Win64-Shipping.exe")`. A renamed game still has `<Project>/Content/Paks/*.pak`
  (often also IoStore `.utoc` and `.ucas` files, common in UE5).
- **Build.** `module_get(module="<exe>")` returns the path, the size and `pe.timeDateStamp`, the build identity: keep
  it, offsets hold for one build only.
- **Version.** The file's ProductVersion (Explorer, Details tab) sometimes reads `++UE5+Release-5.3`, but no tool reads
  it; search the engine's build text instead:
  `aob_find_value(valueType="wstring", value="+UE5+Release-", module="<exe>", limit=5)`, then
  `memory_read(address="<match>", valueType="wstring", length=64)` reads for example `+UE5+Release-5.3`
  (`memory_read.length` counts bytes, so 64 gives up to 32 UTF-16 characters: [value types](value-types.md)).
  Otherwise try `+UE4+Release-`, then both texts as UTF-8 `string`. A licensee's custom branch name may contain
  neither.

| Version | What changes |
|---|---|
| 4.11 to 4.20 | the object array is one flat array of items |
| 4.21 and later | the object array is chunked, 65536 items per chunk |
| before 4.23 | names live in `TNameEntryArray` (GNames) |
| 4.23 and later | names live in `FNamePool` |
| 4.25 and later | properties are `FField`/`FProperty` in `ChildProperties`, no longer UObjects |
| 5.0 and later | Large World Coordinates: vectors, rotators and transforms are doubles |

## How values look

- **UE5 positions are doubles**: `FVector`, `FVector2D`, `FRotator`, `FQuat`, `FTransform`, `FMatrix`, `FPlane` and
  `FBox` default to double (an `FVector` is 24 bytes); the float variants such as `FVector3f` remain for GPU data and
  wherever code asks for them. Use [find_position](../Workflows/find-position.md) with `double`, or
  `scan_first(valueType="double", comparison="unknown")`. UE4 vectors are 3 floats.
- **Units**: one unit is a centimetre and Z is up: positions are large numbers, and a jump changes Z.
- **Health and stats** are usually floats. A Gameplay Ability System attribute (`FGameplayAttributeData`) holds
  roughly {vtable, float BaseValue, float CurrentValue} (offsets UNVERIFIED). The game recomputes CurrentValue from
  the base and the active effects, so a write to one alone can be undone: write both and check after the next hit.
- **Text**: `FString` is a `TArray` of UTF-16 characters. Object names are `FName` indexes: a text scan finds the
  name table entry, never the object ([value types](value-types.md)).

## Core layouts (typical x64)

| Type | Layout |
|---|---|
| `UObject` (0x28) | +0 vtable, +8 ObjectFlags, +C InternalIndex, +10 ClassPrivate, +18 NamePrivate, +20 OuterPrivate |
| `FName` (8) | int32 ComparisonIndex, int32 Number; Number n above 0 displays as `Name_<n-1>` |
| `TArray<T>` (16) | T* Data, int32 Num, int32 Max |
| `FUObjectItem` | +0 object pointer, +8 Flags, +C ClusterRootIndex, +10 SerialNumber; stride 0x18 (0x10, 0x14 exist) |
| `FNamePool` (4.23+) | `Blocks[]` at +10; each block is a 0x20000-byte allocation |
| `FNameEntry` (4.23+) | 2-byte header (bit 0 set = UTF-16, length = header >> 6), then the characters, no terminator |

`FUObjectArray` (4.21+): +0 ObjFirstGCIndex, +4 ObjLastNonGCIndex, +8 MaxObjectsNotConsideredByGC, +C
OpenForDisregardForGC, +10 Objects (the chunk table), +18 PreAllocatedObjects, +20 MaxElements, +24 NumElements, +28
MaxChunks, +2C NumChunks. Item i is `Objects[i >> 16][i & 0xFFFF]`.

Mirror the header in CE and read an object, its class and its outer side by side (a distinct name avoids a clash with
a structure the user's table already has):

```
structure_create(name="UEObjectHeader", elements=[{offset="0", name="VTable", valueType="pointer"},
  {offset="8", name="ObjectFlags", valueType="uint32", display="hex"},
  {offset="C", name="InternalIndex", valueType="int32"}, {offset="10", name="ClassPrivate", valueType="pointer"},
  {offset="18", name="NameIndex", valueType="int32"}, {offset="20", name="OuterPrivate", valueType="pointer"}])
structure_read(name="UEObjectHeader", addresses=["<obj>", "[<obj>+10]", "[<obj>+20]"])
```

Remove it with `structure_delete(name="UEObjectHeader")` when done ([structures](structures.md)).

## Find the globals

`GUObjectArray`, the name pool, `GWorld` (the current `UWorld`) and `GEngine` are statics of the Shipping module:
module+offset roots that survive a restart.

**A shipped PDB** (rare): when `pe.pdb` names a file the user has,
`symbol_add_module(path="<pdb path>", baseAddress="<exe>", enumStructures=true)`, then
`symbol_find(nameContains="GUObjectArray", module="<exe>")` (also `GWorld`, `NamePoolData`) and
`structure_get_pdb_layout(typeName="UObjectBase")` give exact answers.

### The name pool, from data

UnrealEngineTools finds it the same way (UE 4.23 and later):

1. `aob_find(patterns=["?? 01 4E 6F 6E 65 ?? 03 42 79 74"], writable="required", alignment=65536, limit=10)` finds the
   first name block: entry 0 is `None` (header, then 4 characters), entry 1 `ByteProperty`. It scans all writable
   memory while CE waits. No match suggests UE 4.22 or older, or a fork: use the
   [pointer scanner](../Workflows/pointer-scan.md).
2. `pointer_find_references(target="<block>", maxOffset=0, module="<exe>")` lists the statics pointing at it. Keep the
   holder whose next pointers are blocks too (or 0 in a small game):
   `memory_get_address_info(addresses=["[<holder>+8]", "[<holder>+10]"])` reports `region.size` 131072 for each.
3. `symbol_register(name="ueNamePool", address="<holder>-10")`.

Resolve a name index (an object's +18):

1. `memory_read(address="<obj>+18", valueType="int32")` gives the index; `util_calculate(expression="<index> >> 16")`
   is the block and `util_calculate(expression="(<index> & 0xFFFF) * 2")` the offset: use their `hex` results.
2. `memory_read(address="[ueNamePool+10+8*<block>]+<offset>", valueType="uint16")` reads the header;
   `util_calculate(expression="<header> >> 6")` is the length, and an odd header means UTF-16.
3. `memory_read(address="[ueNamePool+10+8*<block>]+<offset>+2", valueType="string", length=<length>)`; for UTF-16,
   `memory_read(address="[ueNamePool+10+8*<block>]+<offset>+2", valueType="wstring", length=<2 * length>)`, since
   `memory_read.length` counts bytes. Always pass it: entries have no terminator, and the default 256 would run into
   the next entries. Index 0 reads `None` for any holder, so check with a real name: the class of a known object
   (index at `[<obj>+10]+18`) must read a plausible class name.

### The object array, from a known object

Take any UObject, such as the pawn whose base [find what writes](../Workflows/find-writer.md) gave you.

1. `memory_read(address="<obj>+C", valueType="int32")` gives its index.
2. `pointer_find_references(target="<obj>", maxOffset=0, limit=100)` lists its holders. Its item `<item>` is the
   holder whose neighbour holds the next object: `memory_read(address="[<item>+18]+C", valueType="int32")` reads
   index + 1 (stride 0x18; try 10 or 14, or `[<item>-18]+C` for index - 1 when the next slot is free).
3. `util_calculate(expression="0x<item> - (<index> & 0xFFFF) * 0x18")` gives the chunk start;
   `pointer_find_references(target="<chunk>", maxOffset=0)` finds the chunk table slot that holds it.
4. `util_calculate(expression="0x<slot> - 8 * (<index> >> 16)")` gives the table.
   `pointer_find_references(target="<table>", maxOffset=0, module="<exe>")` gives `GUObjectArray+10`:
   `symbol_register(name="ueObjects", address="<holder>-10")`. A flat array (before 4.21) has no table: the static
   at `GUObjectArray+10` holds item 0, `util_calculate(expression="0x<item> - <index> * 0x18")`.
5. Check: `memory_read(address="ueObjects", valueType="int32", count=12)` shows the first and third values equal, the
   second one less than them and the fourth 0 (UnrealEngineTools' test); NumElements (the tenth) is at most
   MaxElements (the ninth). `memory_read(address="[[ueObjects+10]+8*<chunk>]+<item offset>", valueType="pointer")`
   must return the object, with the `hex` of `<index> >> 16` and of `(<index> & 0xFFFF) * 0x18`.

### GWorld, from the player

In standard UE an actor's Outer is its `ULevel`, the level's Outer is its `UWorld`, and a component's Outer is its
actor.

1. `memory_read(address="[<pawn>+20]+20", valueType="pointer")` gives the world; its class name (index at
   `[<world>+10]+18`) reads `World`. A pawn in a streaming sublevel gives that sublevel's world, which no static holds.
2. `pointer_find_references(target="<world>", maxOffset=0, module="<exe>")` lists the statics holding it; `GWorld` is
   the one that holds the new world after a level change. `symbol_register(name="ueWorld", address="<holder>")`.
3. The usual chain: `GWorld` → OwningGameInstance → LocalPlayers (a `TArray`, element 0) → PlayerController →
   AcknowledgedPawn → RootComponent → RelativeLocation. Offsets are per build, from a PDB, a user's dump or a
   [structure dissect](../Workflows/dissect-structure.md). Check with
   `pointer_read_chain(base="ueWorld", offsets=["<OwningGameInstance>", "<LocalPlayers>", "0", "<PlayerController>", "<AcknowledgedPawn>"], valueType="pointer")`:
   it must return the pawn ([pointers](pointers.md)).

Registered symbols last until `symbol_unregister(name="ueWorld")`, `runtime_release_resources()` (which releases
every MCP resource) or the end of the plugin activation, and a saved table omits them. A record that must outlive
them starts at the holder's module+offset, the `symbol` of step 2, with the offsets as hexadecimal strings in
dereference order. With the user's agreement:
`record_create(records=[{description="Player X", address="<exe>+<offset>", offsets=["<OwningGameInstance>", "<LocalPlayers>", "0", "<PlayerController>", "<AcknowledgedPawn>", "<RootComponent>", "<RelativeLocation>"], variableType=5}])`
(5 is a double, for UE5; 4 a float, for UE4). See [build a robust table](../Workflows/build-robust-table.md).

### Signatures after an update

- Code loads a global with a 7-byte RIP-relative `mov` such as `48 8B 1D <disp32>` (`mov rbx,[rip+disp32]`);
  `code_disassemble` prints the resolved target as absolute hexadecimal
  ([RIP-relative globals](code-analysis.md)). Once the user agrees to attach the debugger, find a reader with an
  access capture on the static: `debugger_start_capture(address="ueWorld", trigger="access", size=8)`, then
  `debugger_poll_capture` and `runtime_stop_job` ([find what accesses](../Workflows/find-writer.md)).
- `aob_generate_signature(address="<reader>")` proves a pattern unique in the module. Many Shipping executables are
  over 64 MiB: there `aob_generate_signature.generator` reads `managed`, a pattern of whole instructions from the
  address (RIP-relative, absolute and branch displacements wildcarded), at most 64 bytes, always verified, with
  `offset` 0.
- When `unique` is false (its `triedPattern`, when present, still matched more than once), build one from
  `code_disassemble(address="<reader>", before=4, count=8)`: keep opcodes and ModRM, wildcard disp32 and rel32, and
  extend it until
  `aob_find(patterns=["48 8B 1D ?? ?? ?? ?? 48 85 DB 74"], module="<exe>", executable="required", limit=2)`
  reports `unique` ([make an AOB signature](../Workflows/make-aob-signature.md)). Community patterns like this one
  for `GWorld` fit single builds (UNVERIFIED in general) ([AOB signatures](aob-signatures.md)).
- The data routes above need no code signature. Compare `pe.timeDateStamp` to notice an update, then follow
  [repair after an update](../Workflows/repair-after-update.md).

## Reflection: field offsets without a dump

- A `UClass` is a `UStruct`: SuperStruct (the parent), Children (functions), ChildProperties (the `FField` list, 4.25+;
  older builds keep properties in Children) and PropertiesSize (the instance size).
- Each `FProperty` has a name, ElementSize and Offset_Internal, the field's offset in the instance; walk SuperStruct
  for inherited fields. Where these members sit varies by version: confirm them on a class you know.
- A whole class needs a bounded `lua_execute` script (unsafeLua gate, consent; [Lua](lua.md)) or a user-run dumper.

## External SDK dumpers (the user's choice)

- **Dumper-7** writes a C++ SDK with every class, field offset and function (to `C:\Dumper-7` by default). **UE4SS**
  has an object viewer and dumpers, with custom AOBs in `UE4SS_Signatures/*.lua`. Both run as a DLL inside the game.
- **UnrealEngineTools** (cheat-engine on GitHub) is a CE Lua extension, not part of CE 7.7: an Unreal Engine menu, a
  name cache and names in structure dissect.
- The user decides and runs them; this server never does. Check every offset from their output on the live build
  (same `pe.timeDateStamp`) with `structure_read`.

## Pitfalls

- A position has several copies (root component, cached transforms, movement component), and the code writing it
  serves every actor: use the [shared code filter](../Workflows/shared-code-filter.md) before any patch. With consent
  to attach the debugger, `debugger_start_capture(address="<writer>", trigger="execute", groupByEffectiveAddress=true)`
  lists each address the instruction touches ([debugger](debugger.md)). Each minus the displacement is an object:
  its class name (index at `[<base>+10]+18`), or its actor's for a component (Outer at +20), tells the player from
  the rest.
- Level loads and garbage collection free objects and reuse array slots (SerialNumber tells them apart): re-resolve
  from `GWorld`, never keep raw heap addresses.
- UE builds disable RTTI by default, so `memory_get_address_info(addresses=["<obj>"], includeRtti=true)` returns no
  `rttiClass`: read ClassPrivate's name instead.

## Sources

- https://github.com/cheat-engine/UnrealEngineTools (UEInfoScanner.LUA: name pool and object array heuristics)
- https://github.com/cheat-engine/cheat-engine (memscan.pas: a fast-scan parameter is hexadecimal, so
  UnrealEngineTools' 10000 is 65536; LuaHandler.pas readStringEx: the string bound counts bytes)
- https://apple1417.dev/posts/2025-01-15-unreal-object-layouts
- https://shhoya.github.io/ue_namenobjects.html
- https://dev.epicgames.com/documentation/unreal-engine/large-world-coordinates-project-conversion-guidelines-in-unreal-engine-5
- https://github.com/Encryqed/Dumper-7
- https://docs.ue4ss.com/dev/guides/fixing-compatibility-problems.html
- https://github.com/AniLeo/ue-version
