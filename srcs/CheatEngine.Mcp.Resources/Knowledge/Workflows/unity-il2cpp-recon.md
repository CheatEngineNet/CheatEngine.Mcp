# Explore a Unity (IL2CPP) game

## Goal

Find class `{className}` and field `{fieldName}` in a Unity IL2CPP game in `{mode}` mode: `offline` (the default)
reads memory only, with the user's own dump; `collector` injects CE's Mono data collector, which names classes live
but changes the game (consent first).

## Steps

1. Ask whether the game is offline and free of anti-cheat; stop otherwise. `module_list(nameContains="GameAssembly")`
   must find `GameAssembly.dll`; `mono-2.0-bdwgc.dll` instead means [Unity Mono](unity-mono-recon.md).
2. `module_get(module="GameAssembly.dll")`: keep `size`, `is64Bit` (the offsets below assume true) and
   `pe.timeDateStamp` (the build).
3. `module_list_exports(module="GameAssembly.dll", nameContains="il2cpp_")`: no match means renamed exports, a
   protected game: stop.

### mode: offline

4. MCP runs no dumper. Ask the user for numbers from their own dump (such as Il2CppDumper's): the offset of
   `{fieldName}` (`// 0x...` in `dump.cs`), the RVA of the `{className}_TypeInfo` global for statics (decimal in
   `script.json`: `util_calculate(expression="<Address>")` gives `hex`), method RVAs; write them in hex without `0x`.
   A dumper error about invalid metadata means encrypted metadata: stop. No dump:
   [offline without a dump](../Documents/unity-il2cpp.md#offline-without-a-dump).
5. Use the RVA, not the file `Offset`; it must be below `size`. A VA minus the image's preferred base gives the RVA,
   such as `util_calculate(expression="0x<VA> - 0x180000000")` for the x64 default base. Check code with
   `code_disassemble(address="GameAssembly.dll+<RVA>", count=20)`: a function start.
6. Object: find the value with [find a known value](find-known-value.md) or [find what writes](find-writer.md); the
   object `<obj>` is its address minus the field offset. `memory_read(address="[[<obj>]+10]", valueType="string")`
   must read `{className}` (class pointer at `+0`, its name pointer at `+10`), then
   `memory_read(address="<obj>+<offset>", valueType="int32")`.
7. Static: `memory_read(address="[[GameAssembly.dll+<TypeInfoRVA>]+10]", valueType="string")` must read
   `{className}` (the global may be unset until the game has used the class), then
   `memory_read(address="[[GameAssembly.dll+<TypeInfoRVA>]+B8]+<offset>", valueType="int32")`. `B8` holds for metadata
   v24.1 to v29 (about Unity 2018.3 to 2022; UNVERIFIED per build): confirm it on a class you know.
8. With consent, map the fields: `structure_create(name="{className}", elements=[{offset="<offset>",
   name="{fieldName}", valueType="int32"}])`, then `structure_read(name="{className}", addresses=["<obj>"])`.

### mode: collector

4. `mono_get_status()`: `attached` true means a collector already runs: use it from step 7 and leave it attached.
5. Get explicit consent: the DLL stays until the game exits, CE hooks run until detach, CE may set Uses Mono (then
   each process open or table load injects again), a thread registers compiled methods as symbols, and CE may show
   a dialog a human must dismiss.
6. `mono_attach()`: `il2Cpp` should be true; keep `hostEffects`. `invalid_state`: resume
   (`process_set_paused(paused=false)` or `debugger_continue()`), retry; `host_refused`: `mono_get_status()` first.
7. `mono_find_class(namespace="<namespace>", className="{className}")` gives `handle`; otherwise
   `mono_list_classes(imageHandle="<imageHandle>", nameContains="{className}")`, the image from
   `mono_list_assemblies(limit=200)`. No class name: scan the value ([find a known value](find-known-value.md)), then
   `mono_get_object(address="<hit>")` gives `classHandle` (the handle) and the fields: the one whose `offset` is
   `offsetInObject` holds the value (none: wrong object). `unsupported` for an image the collector lists no classes
   for: offline mode.
8. `mono_list_fields(classHandle="<handle>", nameContains="{fieldName}")`: `offset`
   (`util_calculate(expression="<offset>")` gives `hex`), `isStatic`, `staticAddress`. No `staticAddress` and
   `mono_get_static_field_address(classHandle="<handle>")` `not_found`: read `[<hex handle>+B8]+<hex offset>`.
9. Instances: `mono_start_instance_search(classHandle="<handle>", maximumResults=100)`, then
   `mono_poll_instance_search(jobId="<jobId>", afterSequence=0)` until `job.state` is not `running`, then
   `runtime_stop_job(jobId="<jobId>")`. Every hit holds the class pointer (the IL2CPP header): drop hits inside
   `GameAssembly.dll`, then compare field values with `mono_get_object(address="<hit>", maxBacktrack=0)`.
10. Methods: `mono_list_methods(classHandle="<handle>", nameContains="<method>")`. Code address:
    `symbol_find(nameContains="{className}.<method>")` (a miss proves nothing while `symbolsLoaded` is false), or the
    handle's first pointer: `memory_read(address="<hex handle>", valueType="pointer")`, the hex from `util_calculate`;
    0 means no native code.
11. Ask whether to stay attached; otherwise `mono_detach()`.

## Decisions

- Encrypted metadata, renamed exports, a packed `GameAssembly.dll`, `Obscured...` field types or an anti-cheat: a
  protected game; stop and tell the user.
- RVAs survive restarts of one build, not an update: anchor code on a [signature](make-aob-signature.md).
- Several objects: prefer a static singleton (`Instance`) as the root.
- Collector `invalid_state` after a pipe timeout: follow its hint (retry once: the next Mono call reconnects).

## Pitfalls

- Offsets depend on the metadata version and bitness (32-bit: fields from `8`, statics `5C`) and include the object
  header: subtract `10` for a struct embedded in another object.
- Statics exist only after the class is initialised; `isConst` fields have no storage.
- `mono_find_class` wants plain names: a nested (`+`) or generic name makes CE run `Type.GetType` in the game.
- Generic sharing: one native body serves many types, so a hook there fires for all of them.

## Report

Mode, build `timeDateStamp`, a table (field, hex offset, static address or object, value), method RVAs or addresses,
structures created (`structure_delete(name="{className}")`), searches stopped, whether the collector is attached
(the DLL outlives `mono_detach()` until a restart; Uses Mono stays in the table). See
[Unity IL2CPP](../Documents/unity-il2cpp.md), [Mono and .NET](../Documents/mono-and-dotnet.md) and
[safety](../Documents/safety.md).
