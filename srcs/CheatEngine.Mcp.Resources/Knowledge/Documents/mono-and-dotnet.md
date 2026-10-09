# Unity (Mono) and .NET targets

A managed runtime knows its own classes, fields and methods: asking it for a field offset or a static address beats
scanning. Read this page before any `mono_*` or `dotnet_*` tool, because the two families work very differently. The
Mono tools work through a data collector that Cheat Engine (CE) injects into the game. The .NET tools read the game
from a separate helper process and inject nothing. Unity IL2CPP builds are covered in [Unity IL2CPP](unity-il2cpp.md).
The guided workflows are [unity_mono_recon](../Workflows/unity-mono-recon.md) and
[dotnet_recon](../Workflows/dotnet-recon.md).

Scope: only single-player or offline software that the user owns or may modify ([safety](safety.md)). Never use these
tools on online or competitive games or on anti-cheat-protected titles. Renamed runtime exports, encrypted metadata or
obfuscated field types mean deliberate protection: stop and tell the user. Before `mono_attach`, `mono_compile_method`,
`mono_invoke_method` or `exec_inject_dotnet`, explain what the call changes and get the user's consent, and ask again
before each method invocation.

## Pick the runtime

Call `module_list(nameContains="mono")` and `module_list(nameContains="clr")`, or use
[engine_triage](../Workflows/engine-triage.md).

| Loaded module | Runtime | Tools |
|---|---|---|
| `mono-2.0-bdwgc.dll`, `mono.dll`, `mono-*.dll` | Mono (Unity or another Mono app) | `mono_*` after `mono_attach` |
| `GameAssembly.dll` and `UnityPlayer.dll` | Unity IL2CPP (C# compiled to native) | [Unity IL2CPP](unity-il2cpp.md) |
| `coreclr.dll` (.NET Core, .NET 5+), `clr.dll` (.NET Framework 4.x) | Microsoft .NET | `dotnet_*` (no injection) |
| only `UnityPlayer.dll` | Unity, backend not loaded yet | check again later |

- CE's collector needs the runtime export `mono_thread_attach` (Mono) or `il2cpp_thread_attach` (IL2CPP).
- A process can load both a Mono and a CLR module: work with the runtime whose assemblies hold the game logic.
- A .NET app published as Native AOT loads no CLR module: treat it as native code ([code analysis](code-analysis.md)).

## Mono: what mono_attach changes

`mono_attach()` needs `Mcp:EnableTargetCodeExecution` (`runtime_get_info.gates` shows it; off gives
`capability_disabled`). It is `may_prompt`: it runs CE's own launcher, `LaunchMonoDataCollector` in
`autorun/monoscript.lua`, which can block CE for several seconds. A collector version mismatch, or CE's "mono is not
usable in the target proces (yet)" (for example with renamed runtime exports), shows a modal error a human must dismiss.

Before calling it:

- Call `mono_get_status()`. If `attached` is already true, a collector is already connected (CE's Mono menu, Uses
  Mono, the speedhack, or an earlier `mono_attach`): `mono_attach` refuses with `busy`, but the read tools work on
  that collector. MCP never closes an attachment that CE or the user made.
- Make sure the game runs: a paused target (`process_set_paused(paused=false)`) or a debugger stopped at a breakpoint
  (`debugger_continue()`) gives `invalid_state` with `not_started`.
- `unsupported` means CE has not loaded its Mono extension (`monoscript.lua`).
- `host_refused` with `hostEffect` `unknown` means CE did not confirm the attach. Read `mono_get_status()` before
  anything else, and never call `mono_attach` again blindly.

On success the result gives `attached`, `il2Cpp` (CE's reduced IL2CPP mode), `collectorVersion` and
`mono_attach.hostEffects`:

- `collector_injected`: `MonoDataCollector64.dll` (or `32`) is loaded, with a pipe, until the game exits.
- `mono_error_ok_patched` (never on IL2CPP): `mono_error_ok` in `mono-2.0-bdwgc.dll` returns 1 until the game
  restarts. MCP lists it for every Mono target, even a `mono.dll` game with nothing to patch.
- `cheat_engine_hooks_installed`: CE-wide symbol, address, structure-name and dissect hooks and a replacement
  `createStructureFromName`, until a detach.
- `uses_mono_table_option`: the table option Uses Mono is set (or unreadable). CE sets it on each attach while its
  setting 'Enable "Uses Mono" option on successful mono attach' is on (the default); it stays in the table.

Other effects:

- **Symbols.** While attached, CE resolves Mono names in address expressions. On Mono, `Namespace.Class:Method`
  JIT-compiles that method in the game, so it runs target code. `Class:field` gives a static field's address or an
  instance field's offset ([address expressions](address-expressions.md)). On IL2CPP it resolves fields only; method
  symbols are covered in [Unity IL2CPP](unity-il2cpp.md).
- **Target changes.** The attach is an MCP resource of kind `mono` in `runtime_list_resources`. While it is held,
  `process_attach` to another process, `process_create`, `process_open_file` and `debugger_detach` refuse with `busy`.
  Release it with `mono_detach()`; `runtime_release_resources()` releases every MCP resource, so keep it for the end of
  [cleanup_session](../Workflows/cleanup-session.md).
- **Speedhack.** In a Mono or IL2CPP process, CE's first speedhack activation (`speedhack_set_speed` with a speed
  other than 1) starts the same collector to hook Unity's `Time.set_timeScale` ([speedhack](speedhack.md)).

## Uses Mono: CE injects again by itself

The table option Uses Mono is saved with the table (`table_save`). While it is set, CE launches the collector by
itself:

- when a process is opened, unless the CE setting 'Ignore the "Uses Mono" option inside a table' is on (CE does it for
  the first process it opens in a session and for a process it attached the collector to before);
- when a table that sets the option is loaded while a process is open, whatever that setting says.

MCP checks this before CE can act on it:

- `process_attach`, `process_create` and `process_open_file` read the option and the setting first. When the option is
  set (or unreadable) and the setting is off, they need `Mcp:EnableTargetCodeExecution` (else `capability_disabled`,
  `not_started`) and report `process_attach.monoAutoAttach` (or the same field) true: tell the user and check
  `mono_get_status()`. Reselecting the current process is exempt.
- `table_load` needs the same switch when a process is open and the current or the loaded table sets the option;
  `table_load.monoAutoAttach` reports it.
- An Auto Assembler script with `USEMONO()` also launches the collector. `asm_apply` and `table_load` then also need
  `Mcp:EnableUnsafeLua` and `Mcp:EnableTargetCodeExecution`, and `asm_check` refuses it as `unsupported`
  ([auto assembler](auto-assembler.md)). Record scripts are not classified: activating a record whose script calls
  `USEMONO()` needs only `Mcp:EnableAutoAssembler`, so read its `script` with `record_get` and ask consent first.
- To stop the re-injection, the user can untick Uses Mono in CE's table options and save the table. The option stays
  in the table after a detach: say so.

## Mono guard and recovery

Every Mono tool that talks to the collector (all but `mono_attach`, `mono_detach`, `mono_get_status` and
`mono_poll_instance_search`) checks it first. `mono_start_instance_search` still returns a `jobId`; its refusal shows
up as a failed job with `job.error`.

| Kind | Cause | Fix |
|---|---|---|
| `not_attached` | No collector on the target | with consent, `mono_attach()`; on `busy`, `mono_detach()` first |
| `not_attached` | CE aborted the collector (Abort in its timeout dialog) | the recovery below |
| `invalid_state` | The debugger is stopped | `debugger_continue()` |
| `invalid_state` | The target is paused | `process_set_paused(paused=false)` |
| `invalid_state` | CE shows its Mono timeout dialog | the user answers it (Wait, Cancel or Abort), then retry |
| `invalid_state` | A pipe timed out or failed, and reconnecting failed | the recovery below |

All but the last row are `not_started`: fix the cause and call again. After a pipe timeout or error, the next Mono
tool closes the failed pipe (without CE's own teardown), connects again once and proceeds. Only if that fails does it
refuse, with `hostEffect` `started` when it closed a pipe, else `not_applied`. Until a Mono tool reconnects, and
while the target is paused, `mono_get_status` omits `collectorVersion` and `domains`.

Recovery: call `mono_detach()`. If it returns `not_found`, or `mono_detach.alreadyEnded` is true, CE owns the current
attachment: ask the user to untick, then tick Activate mono features in CE's Mono menu. Otherwise call `mono_attach()`
while the game runs, with consent.

MCP runs Mono calls on CE's main thread. When one collector call waits about 5 s (CE's Mono timeout), CE shows its
modal "Mono script: about to timeout" dialog (Wait, Cancel, Abort), which a human must answer. Cancel leaves a failed
pipe that the next Mono tool replaces; Abort ends the connection. A call that outlives the gateway's call timeout
returns `timeout` with `hostEffect` `unknown` ([errors and recovery](errors-and-recovery.md)): check that the game
responds and read `mono_get_status()` first.

`mono_detach()` closes only the attachment this activation made: it sends the collector its terminate command and
runs CE's own detach, which removes the hooks. If that attachment already ended in CE (the user detached or
re-activated Mono, or the target changed), it succeeds with `alreadyEnded` true, releases only the MCP resource and
leaves CE's current attachment alone (`runtime_release_resources` reports `externally_removed`). `not_found` means this
activation holds no attachment. `mono_detach.remainingEffects` lists what stays: the DLL, the `mono_error_ok` patch
(none on IL2CPP) and the Uses Mono option. `partial_effect` means CE's teardown failed and the attachment stays
tracked: call `mono_detach()` again once the game responds, or restart the game, the only way to a clean process.

## Mono recipe: class, fields, statics, code

1. `mono_get_status()`, then, with consent, `mono_attach()`. Keep `il2Cpp`: on IL2CPP, also read
   [Unity IL2CPP](unity-il2cpp.md).
2. `mono_list_assemblies(limit=200)` gives the `imageHandle` of the game code (`Assembly-CSharp` in Unity).
3. `mono_find_class(namespace="", className="Player")` needs both names exactly; `""` is the global namespace.
   Otherwise `mono_list_classes(imageHandle="<imageHandle>", nameContains="Player")` keeps the classes whose
   `Namespace.Name` contains the text, literally and ignoring ASCII case. CE still enumerates the whole image first;
   `total` counts the matches and `nextOffset` pages them. With only an address, see the next section.
4. `mono_list_fields(classHandle="<classHandle>", includeParents=true, nameContains="health")` returns `name`,
   `typeName`, `offset`, `isStatic`, `isConst`, `flags` and, for statics, `staticAddress`. The filter also matches
   `<Health>k__BackingField` and applies before `maximumFields` (512 by default, at most 2048). Instance offsets count
   from the object start, header included (0x10 on 64-bit), also for a struct: subtract the header for a struct
   embedded in an object or array. Static offsets count from the class static data; `isConst` fields have no storage.
5. Statics: use `mono_list_fields.staticAddress`, or add the field offset to
   `mono_get_static_field_address(classHandle="<classHandle>")`. `domainHandle` comes from `mono_get_status.domains`;
   when you omit it, the first domain is used. A generic class has no static address (`not_found`), and on IL2CPP the
   address can be missing. A static singleton such as `Instance` is the most stable root:
   `[<static field address>]+<field offset>`.
6. Methods: `mono_list_methods(classHandle="<classHandle>", nameContains="Damage")` pages `handle`, `name`,
   `signature`, `returnType`, `parameterNames`, `flags` and `isStatic`. `mono_find_method(classHandle="<classHandle>",
   methodName="TakeDamage")` returns only the first method with that name, so resolve overloads from the list.
7. Native code: `mono_compile_method(methodHandle="<methodHandle>")` needs `Mcp:EnableTargetCodeExecution`. It makes
   the game JIT-compile the method, then `nativeAddress` feeds `code_disassemble(address="<nativeAddress>")` or
   [find_writer](../Workflows/find-writer.md). JIT addresses change every run: resolve them again each session.
   Methods of generic classes do not compile this way (`host_refused`). On IL2CPP it returns the existing code pointer.
8. Instances: `mono_start_instance_search(classHandle="<classHandle>", maximumResults=100)` returns a `jobId` at once.
   Call `mono_poll_instance_search(jobId="<jobId>", afterSequence=0, limit=100)` until `job.state` is no longer
   `running` (on `failed`, read `job.error`), then `runtime_stop_job(jobId="<jobId>")`. The job, a read-only scan for
   the class vtable pointer, can block CE for seconds and never runs managed code; `maximumResults` caps what is kept,
   not the scan. Hits are candidates (dead objects, stray values): check their fields. A class without a vtable
   (generic, abstract or not yet initialised) fails the job. `job.progressTotal` above `job.total` means more hits.

Numbers: `offset` and `offsetInObject` are decimal integers, while CE address expressions, `record_create` offsets and
`structure_create` element offsets are hexadecimal. `util_calculate(expression="<decimal offset>")` gives the `hex`
form.

## Mono: from an address to its object

For a scan hit or register value inside an object of unknown class, `mono_get_object(address="<scan hit>")` returns
the object's start `address`, `offsetInObject` (the given address's distance from it), `classHandle`,
`className`, `namespace`, `imageHandle`, `totalFields` and `fields`, inherited ones first and statics included, each
with its `offset`, `address` and current `value`. The instance field whose `offset` equals `offsetInObject` holds the
scanned value; `classHandle` feeds steps 4 to 8 above.

- It walks down from the address, at most `maxBacktrack` bytes (4096 by default, at most 65536), reading target memory
  only. A header counts only when its class (through the vtable on Mono, directly on IL2CPP) is in the collector's
  class list for that class's image. The collector is never asked about the address: a wrong guess can crash the game.
- It is read-only and ungated, but `host_scan`: listing a large image's classes can block CE for seconds.
- `unsupported`: a generic instance or array object, or an IL2CPP image whose collector lists no classes (CE's
  memory-scan guess is not used). Read it with `memory_read`, or start from an object that references it.
- `not_found`: no listed header within `maxBacktrack`; check the address is in a live object, or raise `maxBacktrack`.
- `invalid_state` after a pipe timeout or error: retry once, since the next Mono call reconnects. `not_attached` after
  an abort: the recovery above.
- `value` has the format of `dotnet_get_object` (below) and is omitted for structs, enums, generic instances,
  constants, statics without an address and unreadable memory.

## Calling a Mono method

`mono_invoke_method` runs game code once. It needs `Mcp:EnableTargetCodeExecution` and can change game state or crash
the game. Call it once per explicit user request. Never call it in a loop, and never retry an uncertain result.

`mono_invoke_method(methodHandle="<methodHandle>", instanceAddress="<object>", arguments=[{type=2, value="100"}])`

- Omit `instanceAddress` for a static method (`isStatic` from `mono_list_methods`); an address CE cannot resolve gives
  `invalid_argument`.
- `arguments` holds up to 64 entries in parameter order. Each `value` is text, and each `type` is a CE type code
  (not a CorElementType code):

| type | Parameter | value |
|---|---|---|
| 0 | byte or bool | -128 to 255 (decimal or `0x`), or true, false, yes, no |
| 1 | int16 | -32768 to 65535 |
| 2 | int32 | int32 minimum to uint32 maximum |
| 3 | int64 | int64 minimum to uint64 maximum |
| 4, 5 | float, double | a finite number (4 must fit a float) |
| 6 | string | the text |
| 12 | object or pointer | an address or CE address expression; `0` for null |

- Any other type code, an out-of-range value, or more arguments than the method has parameters gives
  `invalid_argument` before anything runs. Missing trailing arguments are sent as null.
- `returnValue` is text: numbers and addresses in decimal, strings as text, value types as sorted name=value pairs.
  `exception` holds a managed exception the method threw; the call still ran.
- `host_refused` with `hostEffect` `unknown` means CE could not complete the call and the method may have run. Inspect
  the state with read tools before doing anything else.
- The call runs on a collector thread in the game, not on its main thread: Unity APIs that expect the main thread may
  fail or destabilise the game. A method that runs longer than about 5 s raises CE's timeout dialog.

## .NET: the out-of-process collector

For `coreclr.dll` and `clr.dll` targets, CE uses `DotNetDataCollector64.exe` (or `32`), a separate process that reads
the game through the CLR debugging interfaces. Nothing is injected, the `dotnet_*` tools need no switch,
and they are all read-only. They do not check for a paused game or a stopped debugger.

1. `dotnet_get_status()` returns `available`, `attached` and `domainCount`. If `attached` is false once the CLR is
   loaded, `symbol_reload(scope="dotnet")` makes CE reconnect its collector (CE source, not verified on 7.7): check
   again, then list domains and modules anew. Never inject anything to force it. On .NET 7+ suspect `dbgshim.dll`:
   CE 7.7 ships an old copy (4.700.20), and a mismatch gave "No .NET info found" in CE issue #2448.
2. `dotnet_list_domains()`, then `dotnet_list_modules(domainHandle="<handle>")`. A module's `name` is usually its full
   path.
3. `dotnet_list_types(moduleHandle="<moduleHandle>", nameContains="Player")` returns `token`, the full `name`, `flags`
   and `extends`. The filter matches the full name, namespace included, literally and ignoring ASCII case. The
   collector still enumerates the whole module (`host_scan`); `total` counts the matches and `nextOffset` pages them.
4. `dotnet_get_type(moduleHandle="<moduleHandle>", typeToken="<token>", maximumFields=2048)` returns `name`,
   `baseType`, the array layout when there is one, and the fields (512 by default). Each field has `offset`
   (meaningless for a static), `isStatic`, `fieldType`, `elementType`, `attributes` and, for a static,
   `staticAddress`. CE's collector already includes inherited instance fields, but only this type's own statics: pass
   `dotnet_get_type.baseTypeModuleHandle` and `dotnet_get_type.baseTypeToken` back for the base type's statics.
5. `dotnet_list_methods(moduleHandle="<moduleHandle>", typeToken="<token>", nameContains="Damage")` returns `token`,
   `name`, `attributes` (0x10 is static), `implementationFlags`, `ilCode`, `nativeCode` and `secondaryNativeCode`.
   `nativeCode` is omitted until the method is compiled: have the user trigger that feature, then list again.
6. `dotnet_get_method_parameters(moduleHandle="<moduleHandle>", methodToken="<method token>")` returns the
   `parameters` (`index`, `name`, `elementType`, `elementTypeName`) and the `signature` text when reported.
   Here `elementType` is the collector's metadata-constant/default-value code, **not the declared parameter type**.
   `1` / `Void` means no constant; `0` can be unavailable metadata. The list may be incomplete or include return
   metadata; `index` is the returned position, not necessarily the declared ordinal. Use `signature` for declared types.
7. `dotnet_get_object(address="<object address>")` returns the object's start `address`, its `typeName` and the fields
   (512 by default) with their current `value`: decimal numbers, true or false, hexadecimal references and pointers,
   nothing for value-type fields. CE documents that the query assumes a valid object, so pass an object start, such
   as an instance search hit or a reference field; an interior address is not verified on 7.7. `not_found` means no
   object was recognised there. For a Unity (Mono) object, use `mono_get_object`.
8. Instances: `dotnet_start_instance_search(moduleHandle="<moduleHandle>", typeToken="<token>", maximumResults=100)`,
   then `dotnet_poll_instance_search(jobId="<jobId>", afterSequence=0, limit=100)` until `job.state` is no longer
   `running`, then `runtime_stop_job(jobId="<jobId>")`. The collector walks the whole heap for that type in one
   dispatch; `job.progressTotal` above `job.total` means `maximumResults` cut the list.
9. Structures: `structure_fill_from_dotnet(name="Player", address="<object address>", rename=false)` adds the object's
   fields to a CE structure, creating it when missing ([structures](structures.md)). It refuses with `busy` while the
   game is paused or stopped in the debugger.
10. Code: `symbol_reload(scope="dotnet")` adds managed names for newly compiled methods, then
    `code_disassemble(address="<nativeCode>")`.

`elementType` is an ECMA-335 CorElementType code, in decimal: 2 Boolean, 3 Char, 4 SByte, 5 Byte, 6 Int16, 7 UInt16,
8 Int32, 9 UInt32, 10 Int64, 11 UInt64, 12 Single, 13 Double, 14 String, 15 Pointer, 17 ValueType, 18 Class, 20 Array,
21 GenericInstance, 24 IntPtr, 25 UIntPtr, 28 Object, 29 SZArray.

Handles and tokens are opaque decimal strings scoped to the collector session. Pass them back verbatim, and never reuse
them after the game or the collector restarts.

## Loading your own managed code

`exec_compile_csharp(source="<C#>", outputPath="<approved-root assembly path>")` compiles and exports without invocation.
It requires TCE, CE compiler prerequisites, at most 131,072 source characters and 32 held `referencePaths`; optional `coreAssembly` is also checked and held.
It returns path/length/SHA-256, caps output at 16 MiB, and requires `overwrite=true` to replace a file.
Compiler errors carry `{text,truncated}` capped at 16 KiB; prerequisites and syntax errors may share the same diagnostic outcome.
CE owns its temporary file, which another CE instance closing may remove; missing/expired export is explicit.
Use the exported copy for separately authorized injection, inspect partial/unknown effects before retrying, and do not assume unload or native compiler qualification.

### Explicit injection

`exec_inject_dotnet` loads a managed assembly into a .NET Framework or .NET Core target and calls its
`public static int Method(string)`. Use it only on explicit request:

`exec_inject_dotnet(assemblyPath="<absolute path>", className="MyMod.Loader", methodName="Run", parameter="")`

- It needs `Mcp:EnableTargetCodeExecution` and runs code in the game. Assume the assembly stays loaded until the game
  exits.
- CE copies all four values into Auto Assembler strings: control characters, quotes and braces are refused, the path
  may have at most 255 UTF-8 bytes and the other values at most 127.
- CE waits on its main thread, with no time limit, until the method returns. Only the MCP call timeout ends the
  request, and CE stays blocked after it.
- It refuses as `unsupported` on a game with an active Mono collector (CE would load the assembly through Mono without
  calling the method) and on a game with no .NET runtime. For Mono, use `mono_invoke_method` on existing methods.
- `result` is the method's signed int return value as decimal text, such as "-1" (CE reads it unsigned; MCP restores
  the sign). A failure after admission has `hostEffect` `unknown`: inspect the game before any retry.

## Rules for both runtimes

- Never enumerate the whole heap (CE warns that it freezes CE): MCP offers only bounded, type-filtered searches.
- The .NET garbage collector compacts the heap, so objects move. Unity's Boehm collector (`mono-2.0-bdwgc.dll`) does
  not move them, but they die and are replaced, for example on scene changes; a Mono app on `mono-2.0-sgen.dll` uses
  SGen, which moves young objects. Re-resolve objects from statics or instance searches instead of storing addresses.
- Prefer the runtime's field offsets and static roots over pointer scans: they survive restarts, while JIT and heap
  addresses do not. Object headers and array layouts are in [structures](structures.md).
- Before switching targets or ending ([cleanup_session](../Workflows/cleanup-session.md)), stop running searches with
  `runtime_stop_job`, detach Mono, and tell the user what `mono_detach.remainingEffects` says stays.

## Sources

- Cheat Engine 7.7, local install: `autorun/monoscript.lua`, `autorun/dlls/src/Mono/MonoDataCollector/PipeServer.cpp`,
  `autorun/DotNetInject.lua`, `autorun/dotnetinfo.lua`, `autorun/SpeedhackV3.lua`, `win64/dbgshim.dll`, `celua.txt`.
- https://wiki.cheatengine.org/index.php?title=Mono:Lua
- https://github.com/cheat-engine/cheat-engine (`DotNetDataCollector/PipeServer.cpp`: inherited fields, heap walks,
  dbgshim lookup; `symbolhandler.pas`: .NET reinitialisation)
- https://github.com/cheat-engine/cheat-engine/issues/2448 (dbgshim version and "No .NET info found")
- https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals
- https://learn.microsoft.com/dotnet/core/deploying/native-aot/
- https://ecma-international.org/publications-and-standards/standards/ecma-335/ (II.23.1.16, element types)
- https://github.com/mono/mono/blob/main/mono/metadata/object-internals.h
- https://www.mono-project.com/docs/advanced/garbage-collector/sgen/
- https://docs.unity3d.com/Manual/performance-incremental-garbage-collection.html (non-compacting collector)
