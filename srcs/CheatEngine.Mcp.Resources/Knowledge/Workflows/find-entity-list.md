# Find the list that holds entities

## Goal

From the entity at `{entityAddress}`, find the array or list that holds every entity of its kind, its count (the game
shows `{expectedCount}`, when given) and a static path to it, on the `{runtime}` runtime. Memory reads; a scratch
structure, the debugger and the recon's Mono collector need consent. Single-player or offline software only.

## Steps

1. `{runtime}` auto: `module_list(nameContains="mono")` finds Mono, `module_list(nameContains="clr")` .NET, else
   native (IL2CPP's GameAssembly.dll included).
2. `memory_get_address_info(addresses=["{entityAddress}"], includeRtti=true, includePointerValue=true)`: `rttiClass`
   names an MSVC class; `pointerValue` (vtable, managed class or method table) is shared by every object of the class.
3. `pointer_find_references(target="{entityAddress}", maxOffset=0, limit=50)`: the holders. Around each one,
   `memory_read(address="<holder>-40", valueType="pointer", count=24)`: a run of pointers to objects with the same
   `pointerValue` (step 2's call on them) is a pointer array; nulls are free slots.
4. The array starts at its first slot; an x64 managed array starts before its items: Mono and IL2CPP keep the length
   at +18 and items from +20, .NET at +8 and +10. `pointer_find_references(target="<array start>", maxOffset=0)`
   finds the container; `memory_read(address="<container>", valueType="pointer", count=3)`: Unreal `TArray` is {data,
   int32 count, int32 max}; `std::vector` is {first, last, end}, count = (last - first) / pointer size.
5. The count must match `{expectedCount}` and move by one when the user spawns or removes an entity.
6. No holder: `pointer_find_references(target="{entityAddress}", maxOffset=65536)`. An inline array's data pointer
   has an `offset` of index * stride (the distance between two entities, or a code multiplier: `imul r,r,1A8h`).
7. Still unclear, with consent for the debugger: [find what accesses](find-writer.md) a field of this entity, such as
   `mov eax,[rbx+000004C8]`. Then
   `debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true)` and
   `debugger_poll_capture(jobId=..., afterSequence=0)` while entities act: an `effectiveAddress` minus 4C8 whose
   `pointerValue` matches step 2 is an entity; step 3 on two of them finds the container. Then
   `runtime_stop_job(jobId=...)` and `debugger_detach()`.
8. With consent (`structure_list(nameContains="McpEntity")` must list none):
   `structure_autoguess(name="McpEntity", address="{entityAddress}", size=1024)`, then
   `structure_read(name="McpEntity", addresses=["<e0>", "<e1>", "<e2>"])`: the columns must line up.
9. Managed field offsets (decimal): .NET, with the collector of [.NET recon](dotnet-recon.md),
   `dotnet_get_object(address="<container>")` shows a `List<T>`'s `_items` and `_size`. Mono, after
   [Unity Mono recon](unity-mono-recon.md): `mono_get_object(address="<holder>")` names the object around a holder
   and its fields, statics with their `address` (not a `List<T>` or array: unsupported).
10. Anchor the container, not an entity: a [manual pointer chain](manual-pointer-chain.md) or the
    [pointer scan](pointer-scan.md).
11. Clean up: `structure_delete(name="McpEntity")` unless the user keeps it.

## Decisions

- A holder inside another entity of the kind is a linked list: follow
  `memory_read(address="<node>+<next offset>", valueType="pointer")` until null or back to the start.
- Several containers (render, physics, AI): keep the one whose count follows spawns and deaths.
- Every instance, not a list: `pointer_find_references(target="<pointerValue>", maxOffset=0, limit=1000)`; each heap
  holder is an object base.
- No holder at all: entities go by index or handle; step 7's code shows the table it indexes.
- Off by one: the player is in the list, or a pooled slot is free.

## Pitfalls

- A growing array is reallocated: anchor the container, and read its data pointer each time.
- .NET's garbage collector moves objects and Unity's die on a scene change: re-find entities after each level load.

## Report

Container address and kind (pointer array, inline array, linked list, `List<T>`), count field and value against
`{expectedCount}`, stride or next offset, the entity structure and the static path. Still held: the structure
(`structure_delete`), the debugger (`debugger_detach`) and the recon's Mono collector (`mono_detach()`).
See [structures](../Documents/structures.md), [pointers](../Documents/pointers.md),
[memory model](../Documents/memory-model.md) and [game engines](../Documents/game-engines.md).
