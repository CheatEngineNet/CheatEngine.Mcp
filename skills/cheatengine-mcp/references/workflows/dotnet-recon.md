# Explore a .NET game

## Goal

Find type `{typeName}` in a .NET (CoreCLR or .NET Framework) game, read its fields and static data, and locate live
instances and native code, without injecting anything into the game.

## Steps

1. `module_list(nameContains="clr")`: `coreclr.dll` or `clr.dll` must be loaded. Mono modules
   mean [Unity recon](../workflows/unity-mono-recon.md) instead.
2. `dotnet_get_status()`: the collector is CE's separate helper process; these reads do not inject into the game.
3. `dotnet_list_domains()`, then `dotnet_list_modules(domainHandle=..., limit=200)`: pick the game assembly module.
4. `dotnet_list_types(moduleHandle=..., limit=500, offset=0)`: page with `nextOffset` and match `{typeName}` yourself;
   there is no name filter.
5. `dotnet_get_type(moduleHandle=..., typeToken=...)`: fields with offsets, types and `staticAddress`.
6. `dotnet_list_methods(moduleHandle=..., typeToken=...)`: `nativeCode` is the JIT address (0 until the method has run).
7. Instances: `dotnet_start_instance_search(moduleHandle=..., typeToken=..., maximumResults=100)` returns a `jobId`;
   then `dotnet_poll_instance_search(jobId=..., afterSequence=0, limit=100)` until done, then
   `runtime_stop_job(jobId=...)`.
8. `dotnet_get_object(address=...)` on a candidate: class name and fields.
9. With consent, `structure_fill_from_dotnet(name="{typeName}", address=..., createIfMissing=true)`, then
   `structure_read(name="{typeName}", addresses=[...])`.
10. For code: `symbol_reload(scope="dotnet")` for managed names, then
    `code_disassemble(address=<nativeCode>)`, [find what writes](../workflows/find-writer.md)
    or [AOB injection](../workflows/aob-injection.md).

## Decisions

- `nativeCode` is 0: ask the user to use the feature so the method gets compiled, then list the methods again.
- Many instances: tell them apart by field values (`dotnet_get_object`) before choosing one.
- Values held by a static singleton: use its `staticAddress` plus offsets rather than heap addresses.

## Pitfalls

- Handles and tokens are opaque collector values, not addresses; reuse them only in this session.
- The garbage collector moves objects; heap addresses and pointer chains through managed objects are fragile.
- `structure_fill_from_dotnet` refuses while the game is paused or stopped in the debugger.
- Instance searches are bounded jobs; stop them before switching target. Whole-heap enumeration is not offered.

## Report

Type, module, field layout with offsets and static addresses, instances found, native method addresses, the structure
created (CE-owned; `structure_delete` removes it), and searches stopped or still running (`runtime_stop_job`).
See [Mono and .NET](../mono-and-dotnet.md).
