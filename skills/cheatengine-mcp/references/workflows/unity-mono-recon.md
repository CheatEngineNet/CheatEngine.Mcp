# Explore a Unity (Mono) game

## Goal

Use Cheat Engine's Mono data collector to find class `{className}` and field `{fieldName}` in a Unity Mono game, then
turn them into addresses and code locations. Attaching changes the game process and needs explicit consent.

## Steps

1. `module_list(nameContains="mono")`: `mono-2.0-bdwgc.dll` or `mono.dll` should be loaded. If
   `module_list(nameContains="GameAssembly")` finds `GameAssembly.dll` instead, the game is IL2CPP: CE's Mono tools then
   run in a partial IL2CPP mode that MCP has not qualified, with the same host effects. Continue only with that caveat,
   and when a `mono_*` call fails, fall back to disassembly, AOB signatures and
   `memory_get_address_info(includeRtti=true)`.
2. `runtime_get_info()`: `gates.targetCodeExecution` must be true, otherwise `mono_attach` fails with
   `capability_disabled`.
3. `mono_get_status()`: when already attached, go to step 7.
4. State the host effects and get explicit consent. Attaching injects the MonoDataCollector DLL into the game; patches a
   Mono runtime error-check function in the game's code; installs CE hooks and replaces CE's global structure-from-name
   handler; marks the table as using Mono, so later process attaches or table loads can inject the collector again; can
   block CE for 10 s or more, and on failure CE may show a dialog someone must dismiss; can be detected by anti-cheat.
   Assume these effects last until the game restarts. Ask the user to save first.
5. `process_get_current()` must show `paused=false` (else `process_set_paused(paused=false)`), and
   `debugger_get_status()` must show `broken=false` (else `debugger_continue()`).
6. `mono_attach()`: report `attached`, `il2cpp`, `collectorVersion`, `monoscriptSha256` and `hostEffects`.
7. `mono_find_class(namespace="<ns>", className="{className}")`; retain its class handle. If unknown,
   `mono_list_assemblies()`, then `mono_list_classes(imageHandle=<Assembly-CSharp image handle>, limit=200)`.
8. `mono_list_fields(classHandle=..., includeParents=true)`: offset, type and `isStatic` of `{fieldName}`.
9. Static field: `mono_get_static_field_address(classHandle=...)` plus the field offset, then `memory_read`.
10. Instance field: `mono_start_instance_search(classHandle=...)` returns a `jobId`;
    `mono_poll_instance_search(jobId=..., afterSequence=0, limit=100)` until done, `runtime_stop_job(jobId=...)`; then
    `memory_read(address=<instance>+<offset>, valueType=...)`.
11. Code: `mono_find_method(classHandle=..., methodName="<name>")`, then `mono_compile_method(methodHandle=...)`
    (targetCodeExecution) for a native address; continue with
    `code_disassemble`, [find what writes](../workflows/find-writer.md)
    or [AOB injection](../workflows/aob-injection.md).
12. Ask whether to stay attached; otherwise `mono_detach()`.

## Decisions

- `mono_invoke_method` runs game code with arguments; call it only after a separate explicit consent for that call.
- A Mono call returns `busy`: the game is paused or stopped in the debugger; resume it first.
- `timeout` or `hostEffect` unknown: the collector may be unhealthy; report it, check that the game still responds, then
  `mono_detach()` and re-attach at most once, never blindly.

## Pitfalls

- Field offsets are relative to the object start and already include the object header.
- JIT addresses change every run; resolve methods again each session instead of storing raw addresses.
- Unity's Mono garbage collector does not move objects, but objects die and get replaced (scene changes); prefer static
  fields plus offsets.

## Report

Collector version and script hash, host effects reported, class and field (offset, static address), instances found,
native method addresses, searches stopped, and whether Mono is still attached (`mono_detach()` detaches; the injected
DLL and patch remain until the game restarts). See [Mono and .NET](../mono-and-dotnet.md) and [safety](../safety.md).
