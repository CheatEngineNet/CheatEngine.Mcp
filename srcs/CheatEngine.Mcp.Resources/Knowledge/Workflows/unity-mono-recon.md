# Explore a Unity (Mono) game

## Goal

Find class `{className}` and field `{fieldName}` in a Unity Mono game with CE's Mono data collector, as live addresses
and code; without a class name, start from a scanned value. Attaching injects into the game: consent first.

## Steps

1. Ask whether the game is offline and free of anti-cheat; stop otherwise. `module_list(nameContains="mono")` must show
   `mono-2.0-bdwgc.dll` or `mono.dll`; none, but `GameAssembly.dll`: [Unity IL2CPP](unity-il2cpp-recon.md).
2. `mono_get_status()`: `attached` true means a collector already runs: use it from step 5 and leave it attached.
3. Get explicit consent (suggest saving first): the DLL stays until the game exits, `mono_error_ok` stays patched,
   CE hooks run until detach, CE may set Uses Mono (then each process open or table load injects again) and show a
   dialog a human must dismiss.
4. `mono_attach()`: keep `hostEffects`. `invalid_state`: resume (`process_set_paused(paused=false)` or
   `debugger_continue()`), retry; `busy`: use the running collector; `host_refused`: `mono_get_status()` first;
   `capability_disabled`: stop.
5. Class: `mono_find_class(namespace="<namespace>", className="{className}")` (`""` is the global namespace); keep
   `handle`. Namespace unknown: `mono_list_classes(imageHandle="<Assembly-CSharp imageHandle>",
   nameContains="{className}")`, the image from `mono_list_assemblies(limit=200)`. No class name: scan the value
   ([find a known value](find-known-value.md)), then `mono_get_object(address="<hit>")`: `classHandle` is the handle,
   `address` the object, and the field whose `offset` equals `offsetInObject` holds the value (none: wrong object).
6. `mono_list_fields(classHandle="<handle>", includeParents=true, nameContains="{fieldName}")`: `offset` (decimal;
   `util_calculate(expression="<offset>")` gives `hex`), `typeName`, `isStatic` and `staticAddress`.
7. Static: `memory_read(address="<staticAddress>", valueType="int32")`, or
   `mono_get_static_field_address(classHandle="<handle>")` `address` plus the hex offset. Type by `typeName`: Single
   `float`, Boolean `uint8`, a class or string `pointer`.
8. Instance: `mono_start_instance_search(classHandle="<handle>", maximumResults=100)` gives a `jobId`;
   `mono_poll_instance_search(jobId="<jobId>", afterSequence=0)` until `job.state` is not `running`, then
   `runtime_stop_job(jobId="<jobId>")`. `mono_get_object(address="<instance>", maxBacktrack=0)` gives each hit's
   field values to compare.
9. Code: `mono_list_methods(classHandle="<handle>", nameContains="<method>")`; with consent,
   `mono_compile_method(methodHandle="<methodHandle>")` JIT-compiles it in the game: `nativeAddress` feeds
   `code_disassemble(address="<nativeAddress>")` or [find what writes](find-writer.md).
10. Ask whether to stay attached; otherwise `mono_detach()`.

## Decisions

- Prefer a static singleton (`Instance`) as root: `[<staticAddress>]+<hex offset>`.
- `mono_get_object` `unsupported`: a generic or array object; `not_found`: no live object there, or raise
  `maxBacktrack`.
- A generic class has no static address (`not_found`); a class without a vtable fails the search (`job.error`).
  `job.progressTotal` above `job.total`: more hits than `maximumResults`.
- JIT code has no module (`aob_generate_signature` refuses it): a script scans it with `aobscanregion` over
  `<Class>:<Method>`, which CE resolves by JIT-compiling it, so only while attached.
- `invalid_state` after a pipe timeout: follow its hint. Retry once (the next call reconnects); if reconnecting
  failed, `mono_detach()`, then with consent `mono_attach()` once, or on `not_found` or `alreadyEnded` the user
  re-activates Mono in CE's Mono menu. CE's timeout dialog up: the user answers it.

## Pitfalls

- Instance offsets include the `10` header (64-bit), a struct's too: subtract it for an embedded struct. Static
  offsets count from the class static data; `isConst` fields have no storage.
- `mono_find_class` wants plain names: a nested (`+`) or generic name makes CE run `Type.GetType` in the game.
- JIT addresses change every run and objects die on scene changes: resolve again each session.
- Search hits can be dead objects. Searches, class lists and `mono_get_object` can block CE for seconds.

## Report

A table: field, `typeName`, hex offset, static address or instance, value; method addresses, `hostEffects`, searches
stopped, whether Mono is attached. The DLL, the patch and Uses Mono (the user can untick it) outlive `mono_detach()`.
See [Mono and .NET](../Documents/mono-and-dotnet.md) and [safety](../Documents/safety.md).
