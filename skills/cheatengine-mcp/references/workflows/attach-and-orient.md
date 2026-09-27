# Attach to a target and get oriented

## Goal

Attach this Cheat Engine instance to the process the user authorized (`{process}`, a name or PID), identify its runtime, and report what the session can do for `{goal}`.

## Steps

1. `runtime_get_info()`: note the CE and plugin versions and `gates` (unsafeLua, autoAssembler, targetCodeExecution, kernelAccess). A disabled gate makes its tools fail with `capability_disabled`.
2. `runtime_get_overview()`: is a target attached, are resources or jobs already owned, is the main scan busy? Understand existing state before starting new work.
3. `process_get_current()`: if `{process}` is already attached, go to step 7.
4. Ask the user whether the game is single-player or offline and whether it uses anti-cheat. Stop on online multiplayer or protected games; see [safety](../safety.md).
5. `process_list(nameContains="{process}", limit=20)` (or look up the PID). If several processes match (launcher, helpers), show name, PID and window title and let the user choose.
6. `process_attach(processId=<chosen PID>)`. Prefer `processId` over `processName`.
7. `process_get_current()`: confirm name, PID, `bitness`, `pointerSize` and `pointerSizeMismatch`.
8. `module_list(limit=50)`: classify the runtime (see Decisions) and note the main module.
9. `module_get(module="<main module>")`: keep the PE timestamp; it later tells whether the game was updated.

## Decisions

- `process_attach` refused because owned resources remain: show `runtime_list_resources()`, ask, then run the [cleanup](../workflows/cleanup-session.md) workflow or `runtime_release_resources()` before attaching. Re-attaching the same PID is allowed.
- `mono-2.0-bdwgc.dll` or `mono.dll`: Unity Mono; continue with [Unity recon](../workflows/unity-mono-recon.md), which needs explicit consent.
- `GameAssembly.dll`: IL2CPP. The Mono tools work only in CE's partial IL2CPP mode, which MCP has not qualified; [Unity recon](../workflows/unity-mono-recon.md) may help with the same consent. When a `mono_*` call fails, use disassembly, AOB signatures, RTTI (`memory_get_address_info(includeRtti=true)`) and structures.
- `coreclr.dll` or `clr.dll`: .NET; continue with [.NET recon](../workflows/dotnet-recon.md).
- Otherwise native: value scans, debugger and Auto Assembler apply.
- Target not running: ask the user to start it. `process_create` launches a program and needs the targetCodeExecution gate; prefer the user launching it.
- `process_attach` reports a Mono auto-attach host effect (the loaded table uses Mono): tell the user the collector was injected.
- `pointerSizeMismatch` is true: report it; call `process_set_pointer_size` only if the user asks.

## Pitfalls

- The `processId` from `instance_list` is Cheat Engine's own PID, not the game's.
- Never switch to another instance; if this one disappears, rediscover it and ask the user.
- Two Cheat Engine instances attached to the same game share its memory.
- Symbols may still be loading: `runtime_get_overview()` reports `symbolsLoaded`, and `symbol_reload(scope="new_modules")` never waits.
- An attach error whose `hostEffect` is not `not_started` or `not_applied`: check `process_get_current()` before trying again.

## Report

Instance id and name, target name, PID and bitness, runtime classification, gate states, owned resources from `runtime_list_resources()` (normally none after an attach), and the workflow you suggest next for `{goal}`, for example [find a known value](../workflows/find-known-value.md). See [workflows](../workflows.md).
