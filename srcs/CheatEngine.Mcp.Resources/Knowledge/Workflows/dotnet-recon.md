# Explore a .NET game

## Goal

Find type `{typeName}` in a .NET (CoreCLR or .NET Framework) game: its layout, statics, live objects and native code,
through Cheat Engine's out-of-process .NET collector, which injects nothing.

## Steps

1. Ask whether the game is offline and free of anti-cheat; stop otherwise. `module_list(nameContains="clr")` must show
   `coreclr.dll` or `clr.dll`; otherwise a Mono module means [Unity Mono](unity-mono-recon.md), `GameAssembly.dll`
   [Unity IL2CPP](unity-il2cpp-recon.md), and neither means native code (NativeAOT too).
2. `dotnet_get_status()`: `available` and `attached` must be true. `attached` false with the CLR loaded:
   `symbol_reload(scope="dotnet")` should make CE reconnect its collector (CE source, not verified on 7.7), then check
   again. Still false: the collector cannot read this game; use [find a known value](find-known-value.md) and never
   inject anything to force it.
3. `dotnet_list_domains()`, then `dotnet_list_modules(domainHandle="<handle>", limit=200)`: pick the game assembly
   (`name` is usually a full path).
4. `dotnet_list_types(moduleHandle="<moduleHandle>", nameContains="{typeName}")`: it matches the full `name`, namespace
   included, ignoring ASCII case; `nextOffset` pages the matches. Not given: omit the filter, show candidates and ask.
5. `dotnet_get_type(moduleHandle="<moduleHandle>", typeToken="<token>")`: `name`, `baseType` and the fields, inherited
   instance fields included, with `offset`, `isStatic`, `fieldType`, `elementType` and, for statics, `staticAddress`.
   The base type's own statics:
   `dotnet_get_type(moduleHandle="<baseTypeModuleHandle>", typeToken="<baseTypeToken>")`.
6. Statics: `memory_read(address="<staticAddress>", valueType="int32")`, typed by `elementType`.
7. Instances: `dotnet_start_instance_search(moduleHandle="<moduleHandle>", typeToken="<token>", maximumResults=100)`
   returns a `jobId`; `dotnet_poll_instance_search(jobId="<jobId>", afterSequence=0, limit=100)` until `job.state`
   is not `running` (`job.error` on failure), then `runtime_stop_job(jobId="<jobId>")`.
8. `dotnet_get_object(address="<instance>")`: `typeName` and each field's current `value`, to compare candidates.
9. With consent (it creates a CE structure or adds fields to one of that name):
   `structure_fill_from_dotnet(name="{typeName}", address="<instance>", createIfMissing=true)`, then
   `structure_read(name="{typeName}", addresses=["<instance>"])`.
10. Code: `dotnet_list_methods(moduleHandle="<moduleHandle>", typeToken="<token>", nameContains="<method>")` gives
    each method's `token` and `nativeCode`; `dotnet_get_method_parameters(moduleHandle="<moduleHandle>",
    methodToken="<method token>")` its collector entries and optional declared-type `signature`. Its `elementType`
    describes a metadata constant, not a declared parameter type. `symbol_reload(scope="dotnet")` adds managed names, then
    `code_disassemble(address="<nativeCode>")` or [find what writes](find-writer.md).

## Decisions

- Field `elementType` to `valueType`: 2 Boolean `uint8`, 8 Int32 `int32`, 10 Int64 `int64`, 12 Single `float`, 13 Double
  `double`; 14 String and 18 Class hold a `pointer`.
- No `nativeCode`: not JIT-compiled yet; ask the user to use that feature, then list again.
- JIT code has no module, so `aob_generate_signature` refuses it: re-find the method each session.
- A static singleton is the best anchor: `[<staticAddress>]+<hex offset>`.
- `job.progressTotal` above `job.total`: more instances existed than `maximumResults`.
- Only a scanned value: ask for the type name; `dotnet_get_object` expects an object start (an interior address is
  unverified on 7.7).

## Pitfalls

- The garbage collector moves objects: re-resolve heap addresses from statics or a new search right before use.
- Handles and tokens are opaque decimal strings, not addresses; pass them verbatim. Handles belong to one collector
  session: after a restart or a `symbol_reload(scope="dotnet")`, list domains and modules again.
- `offset` is decimal and meaningless for a static; address expressions are hex (`util_calculate(expression="<offset>")`
  gives `hex`).
- Value-type fields have no `value` in `dotnet_get_object`: read them at the object plus the offset.
- The search walks the heap in one dispatch and can block CE; stop it before switching targets.
- `structure_fill_from_dotnet` refuses with `busy` while the game is paused or stopped in the debugger.

## Report

Type and module; a table of fields: name, hex offset, static or instance, static address, value; instances found,
native method addresses, the structure created (`structure_delete(name="{typeName}")` removes it) and searches
stopped. Nothing was injected. See [Mono and .NET](../Documents/mono-and-dotnet.md).
