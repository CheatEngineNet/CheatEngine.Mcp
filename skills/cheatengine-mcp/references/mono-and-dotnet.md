# Unity (Mono) and .NET targets

Managed runtimes know their own classes, fields and methods. Asking the runtime for a field offset or a static address
is faster and more robust than scanning. The two families work very differently: Mono tools inject into the target, .NET
tools do not.

## Detect the runtime first

Call `module_list` and look at the names:

| Modules                                                                       | Runtime                                                | Tools                                                                                   |
|-------------------------------------------------------------------------------|--------------------------------------------------------|-----------------------------------------------------------------------------------------|
| `mono-2.0-bdwgc.dll`, `mono.dll`, `mono-*.dll`                                | Mono (Unity's Mono backend, other Mono apps)           | `mono_*`                                                                                |
| `GameAssembly.dll` (with `UnityPlayer.dll`)                                   | Unity IL2CPP: C# compiled ahead of time to native code | `mono_*` only in CE's partial IL2CPP mode, not qualified by MCP; otherwise native tools |
| `coreclr.dll` (.NET Core / .NET 5+), `clr.dll` (.NET Framework), `clrjit.dll` | .NET                                                   | `dotnet_*`                                                                              |
| `UnityPlayer.dll` alone                                                       | Unity, backend not yet known                           | check again after the game finishes loading                                             |

- IL2CPP has no JIT and no IL. CE 7.7's `monoscript.lua` treats `GameAssembly.dll` as a Mono target and switches to a
  reduced IL2CPP mode (`il2cpp:true` in `mono_attach`/`mono_get_status`). It can often list classes, fields and methods,
  and `mono_attach` still injects the collector with the host effects below, but IL, JIT and some static-field features
  do not apply. MCP has not qualified this mode: treat its answers as partial, and when a `mono_*` call fails, fall back
  to native analysis ([code-analysis](code-analysis.md), [structures](structures.md)).
- A game can load several runtimes (a .NET launcher, a Mono game). Pick by the module that holds the game logic.

## Mono: what `mono_attach` does to the host and target

`mono_attach` needs `EnableTargetCodeExecution` and the user's consent. It runs CE's own `monoscript.lua`
(`LaunchMonoDataCollector`), which:

- injects CE's `MonoDataCollector64.dll` (or the 32-bit one) into the target and talks to it over a pipe;
- patches the target's `mono_error_ok` in `mono-2.0-bdwgc.dll` to always return 1;
- installs CE-wide hooks (symbol and address lookup, structure naming and dissect overrides) and replaces CE's global
  `createStructureFromName`;
- can block CE for 10 s or more (pipe connect and symbol enumeration timeouts), and on failure or version mismatch can
  show a CE modal dialog that a human must dismiss;
- lets CE set the table option **Uses Mono** (CE setting "Enable 'Uses Mono' option on successful mono attach", on by
  default). With it set, CE re-attaches Mono by itself when a process is opened or a table is loaded, unless the CE
  setting "Ignore the 'Uses Mono' option inside a table" is on. MCP's `process_attach`, `process_create`,
  `process_open_file` and `table_load` check this, require `EnableTargetCodeExecution` and report the host effect
  `mono_auto_attach`.

The result reports `il2cpp`, `collectorVersion`, `monoscriptPath`, `monoscriptSha256` and `hostEffects[]`. The installed
`monoscript.lua` hash differs from the one the project reviewed, so quote the reported hash if the user asks what ran.

The first `speedhack_set_speed` on a Unity target can start the same collector without `mono_attach`
(see [speedhack](speedhack.md)).

`mono_detach` releases only the attachment this plugin activation created with `mono_attach`.
If this activation owns no attachment, it returns `not_found` and leaves a collector attached by another Cheat Engine session unchanged.
It closes the owned connection's pipes, unregisters its hooks and restores `createStructureFromName`.
It does not unload the collector DLL or undo the `mono_error_ok` patch, so restart the target for a clean state.
The table option stays in the table, so tell the user.

## Mono guards

Every `mono_*` call runs on CE's main thread and first checks that the target is not paused (`process_set_paused`), the
debugger is not stopped on a breakpoint, the Mono module is present and the pipe is connected. A paused or broken target
would hang the pipe and trigger CE's "about to timeout" modal. On a refusal (`busy`, `invalid_state`, `not_attached`),
fix the state (`debugger_continue`, unpause, `mono_attach`) and call again; read-only calls are safe to repeat.

A Mono `timeout` has `hostEffect:"unknown"` and marks Mono unhealthy: check that the game still responds, then
`mono_detach` and re-attach at most once. A hung target can still raise CE's modal; ask the user to dismiss it.

## Mono workflow: class -> field -> static address -> code

1. `mono_attach` once; `mono_get_status` later to confirm `attached` and `il2cpp`.
2. `mono_list_assemblies` -> the `image` of the game code (for Unity usually `Assembly-CSharp`).
3. `mono_find_class(namespace?, className)`, or page `mono_list_classes(image)`.
4. `mono_list_fields(class, includeParents)` -> `name`, `offset` (from the object start), `typeName`, `isStatic`,
   `isConst`, `staticAddress?`.
5. `mono_get_static_field_address(class)` -> the class's static data block; a static field lives at block + its offset.
   A static `Instance` or manager field is the most stable pointer root: `[static block + offset]` + instance field
   offset.
6. `mono_find_method(namespace?, className?, methodName, parameters?)` or `mono_list_methods(class)`; then
   `mono_compile_method(method)` (`EnableTargetCodeExecution`, it makes the target JIT the method) -> `nativeAddress`
   for `code_disassemble`, a capture or an injection. JIT addresses change every run: re-derive them each session, never
   hard-code them.
7. Instances: `mono_start_instance_search(class)` returns a `jobId` ->
   `mono_poll_instance_search(jobId, afterSequence, limit)` -> `runtime_stop_job(jobId)`. Results are candidates from a
   bounded memory scan and can include dead objects: verify their fields.
8. `mono_invoke_method(method, instance?, arguments)` executes game code in the target (`EnableTargetCodeExecution`). It
   can change game state or crash the target; call it once per explicit user request, never in a loop.

## .NET: the DotNetDataCollector

For `coreclr.dll`/`clr.dll` targets CE uses `DotNetDataCollector64.exe`, a separate helper process: nothing is injected,
so the read tools need no gate.

1. `dotnet_get_status` -> `available`, `attached`. If not attached, the `dotnet_*` tools cannot help; do not inject
   anything to force it.
2. `dotnet_list_domains` -> `dotnet_list_modules(domainHandle)` -> `dotnet_list_types(moduleHandle)`.
3. `dotnet_get_type(moduleHandle, typeToken)` -> fields with `offset`, `fieldType` and, for statics, `staticAddress`.
4. `dotnet_list_methods(moduleHandle, typeToken)` -> `nativeCode` (JIT address, `0` until the method has been compiled)
   and `ilCode`.
5. `dotnet_get_object(address)` -> class name and fields of an object at an address: use it on capture registers or scan
   results.
6. `dotnet_start_instance_search(moduleHandle, typeToken, maximumResults)` returns a `jobId` ->
   `dotnet_poll_instance_search(jobId, afterSequence, limit)` -> `runtime_stop_job(jobId)`.
7. `structure_fill_from_dotnet(name, address)` builds a CE structure from an object's layout
   ([structures](structures.md)).

Handles and tokens are opaque decimal strings scoped to the collector session: pass them back verbatim and never reuse
them after a restart.

## Rules for both

- Never enumerate every object on the heap: CE documents that whole-heap enumeration takes a very long time and freezes
  CE. MCP exposes only bounded, type-filtered instance searches as jobs.
- The .NET garbage collector compacts the heap, so object addresses move: re-resolve objects from statics or instance
  searches instead of storing raw addresses. Unity's Boehm-based collector is non-compacting, so objects stay put, but
  they still die and get replaced (scene changes).
- Prefer field offsets and static roots from the runtime over pointer scans: they survive restarts, while JIT and heap
  addresses do not.
- `exec_inject_dotnet` (load your own managed assembly) is a separate code-execution tool; use it only on explicit
  request.
- Before switching targets: stop instance-search jobs and `mono_detach`.

## Sources

- https://wiki.cheatengine.org/index.php?title=Mono:Lua
- https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals
- https://docs.unity3d.com/560/Documentation/Manual/BestPracticeUnderstandingPerformanceInUnity4-1.html
- https://docs.unity3d.com/Manual/scripting-backends.html
