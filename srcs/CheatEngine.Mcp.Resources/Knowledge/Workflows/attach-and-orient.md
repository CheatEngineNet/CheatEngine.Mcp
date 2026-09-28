# Attach to a target and get oriented

## Goal

Attach this Cheat Engine instance to the process the user authorized (`{process}`: a PID or an exact executable name;
when it is not given, choose it with the user), classify its runtime and report what the session can do for `{goal}`.

## Steps

1. Ask whether the game is single-player or offline and free of anti-cheat. Stop at online, competitive or protected
   games ([safety](../Documents/safety.md)).
2. `runtime_get_info()`: `hostVersion`, `pluginVersion` and `runtime_get_info.gates`, the four Mcp:Enable… switches
   (all on by default). Name each one that is off and what it rules out: TargetCodeExecution the Mono attach, the
   speedhack, code calls and an attach while Uses Mono is set; AutoAssembler patches and injections; UnsafeLua own
   Lua; KernelAccess kernel tools. A change needs a plugin disable and re-enable.
3. `runtime_get_overview()`: `runtime_get_overview.process` is the selected target. A non-zero `resourceCount` or
   `jobCount` means earlier work remains: read `runtime_list_resources()` and `runtime_list_jobs()`; release nothing
   unasked.
4. If the right game is already selected, go to step 7.
5. `process_list(nameContains="<name>")`, with the name from `{process}` or from the user. It returns ids and names
   only; when several match (a launcher and its `-Shipping.exe`, browser renderers), ask the user.
6. With consent, `process_attach(process="<PID>")`. Prefer the PID; a name must match exactly one process. A loaded
   table or an earlier Mono attach can set the table option Uses Mono; Cheat Engine can then inject its Mono
   collector on attach: say so first, and tell the user when `process_attach.monoAutoAttach` is true.
7. `process_get_current()`: confirm `processName` and `processId` with the user; `pointerSize` 4 means Cheat Engine
   treats the target as 32-bit.
8. `module_list(format="detailed", limit=200)`: look for the runtime DLLs below; page with `offset` while
   `module_list.nextOffset` is present.
9. `module_get(module="<processName>")`: keep `is64Bit`, `pe.machine`, `pe.managed` and `pe.timeDateStamp`, to tell
   later whether the game was updated.

## Decisions

- `process_attach` fails with `busy`: MCP resources (an MCP pause included) or a running main scan block the switch,
  as the message says. With consent, run [cleanup](cleanup-session.md) or `runtime_release_resources()`, which
  releases every tracked one, or stop the scan with `scan_stop(scannerName="main")`; then attach again.
- `process_attach` fails with `capability_disabled`: Uses Mono is set (or unreadable) and
  Mcp:EnableTargetCodeExecution is off. Nothing was attached; report the `hint`.
- `mono-2.0-bdwgc.dll` or `mono.dll`: Unity Mono. `mono_get_status.attached` true before any Mono attach of yours
  means the collector is already in the game (Uses Mono, the user or a script): tell the user. Next
  [Unity Mono recon](unity-mono-recon.md), which asks its own consent; with TargetCodeExecution off it works only
  while the collector is attached ([Mono and .NET](../Documents/mono-and-dotnet.md)).
- `GameAssembly.dll`: Unity IL2CPP, [Unity IL2CPP recon](unity-il2cpp-recon.md); the Mono tools work there only in
  a partial mode.
- `coreclr.dll` or `clr.dll`, or `pe.managed` true: .NET, [.NET recon](dotnet-recon.md), which injects nothing.
- Anything else, or an unknown engine: [identify the engine](engine-triage.md) before choosing value types.
- Target not running: ask the user to start it; launch it with `process_create(path="<absolute exe path>")` only
  with consent.
- `pointerSize` disagrees with `is64Bit`: report it; `process_set_pointer_size(pointerSize=<4 or 8>)` only if the user
  asks.

## Pitfalls

- `instance_list.processId` is Cheat Engine's own PID, not the game's.
- Two Cheat Engine instances attached to one game share its memory, and their changes collide.
- After an attach, `symbol_find.symbolsLoaded` false means Cheat Engine is still loading symbols or listing IL2CPP
  methods: a missing name may appear later.
- An attach error whose `hostEffect` is `started` or `unknown`: read `process_get_current()` before trying again.

## Report

Instance name and id (gateway), target name, PID, `pointerSize` and `pe.machine`, the runtime, the switches that are
off, earlier resources or jobs found, and the next workflow for `{goal}`: [identify the engine](engine-triage.md) or
[plan a cheat](plan-cheat.md). An attach leaves nothing to undo; a Mono collector that Cheat Engine injected stays
loaded until the game exits.
