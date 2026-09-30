# Getting started

Start here: this page is the entry point of the knowledge base. It covers what the server is, the first calls of a
session, the settings that switch features, which document to read for which task, what consent means and how to
leave Cheat Engine (CE) clean. The rules for every session and the index of guided workflows are in
[workflows](workflows.md).

## What the server is

- **A plugin in each CE instance.** CE 7.7 x64 loads `CheatEngine.Mcp.dll` (Edit > Settings > Plugins). Once
  enabled, it runs a local MCP backend inside that CE process, protected by a per-activation access token, registers
  it as an instance and shows **MCP: Enabled** in CE's main menu bar. It attaches to no process by itself.
- **A gateway for the AI client.** The client starts `CheatEngine.Mcp.Gateway.exe` as a local stdio MCP server. It
  finds the enabled instances of the same Windows user and routes each call by `instanceId`, never to another instance.
  It serves the documents and prompts itself, even with no CE running, and never launches CE or selects a target.
  Setup and failures: [connection-troubleshooting](connection-troubleshooting.md).
- Through the gateway every tool except `instance_list` takes a required `instanceId`. Examples in these pages omit it:
  `process_list(nameContains="tutorial")` is sent as `process_list(instanceId="<instanceId>", nameContains="tutorial")`.
  A direct connection to one backend has neither `instance_list` nor `instanceId`.

## First calls

1. `instance_list()`: choose by `name` and `processId` (CE's own process id, not the game's); ask when names repeat.
   `discoveryIncomplete` true means the 10-second discovery budget ran out: call again before concluding that an
   instance is missing. An `instanceId` lasts one plugin activation: after a CE restart or a plugin re-enable, list
   again.
2. `runtime_get_info()`: `hostVersion` (CE), `pluginVersion`, the Client `capabilities` (`name`, `isAvailable`,
   `reason`) and `gates`, one boolean per `Mcp:Enable…` switch (`autoAssembler`, `unsafeLua`, `targetCodeExecution`,
   `kernelAccess`). Tell the user which switches are off before planning work that needs them.
3. `runtime_get_overview()`: `process` says whether a target is selected (`isOpen`), and `runtime.gates` repeats the
   switches. A `resourceCount` or `jobCount` above zero means earlier work may still be live: read
   `runtime_list_resources()` and `runtime_list_jobs()` first, and release nothing unasked.
4. Ask which program to use, whether it is single-player or offline, and whether it has anti-cheat. Stop at online,
   competitive or protected games.
5. `process_list(nameContains="tutorial")`: `processes` gives `processId` and `processName`; `truncated` true means
   narrow the filter. When a launcher and the game both match, let the user choose.
6. With consent, `process_attach(process="<processId>")`: a process id (preferred) or an exact executable name.
   - Attaching by id to the process already selected changes nothing.
   - Any other attach is refused with `busy` while MCP holds state that a switch would strand: a patch, allocation,
     named scanner, registered symbol, breakpoint, running job, a speed other than 1, MCP's pause or a Mono
     attachment, including orphans of an earlier activation and entries whose release failed. CE's main scan running
     also blocks it. Snapshots, pointer maps and pointer scans do not (see Cleanup below).
   - When CE's table option Uses Mono is set (by a loaded table or, by default, by an earlier Mono attach) and CE's
     IgnoreUsesMono setting is off, CE injects its Mono data collector on attach. The call then needs
     `Mcp:EnableTargetCodeExecution`, and `process_attach.monoAutoAttach` true says the injection may happen: tell the
     user.
7. `process_get_current()`: confirm `processName`, `processId` and `pointerSize` (4 means CE treats the target as
   32-bit).

The [attach_and_orient](../Workflows/attach-and-orient.md) prompt runs these steps, then classifies the runtime
(native, Mono, IL2CPP or .NET) from the loaded modules.

## Settings that switch features

Personal settings go in `%APPDATA%\CheatEngine.Mcp\appsettings.json`, or in the folder named by `MCP_DATA_DIRECTORY`.
The plugin reads them once, when it is enabled: disable and re-enable it to apply a change. No tool changes a
setting; the user does. Every key, default and refusal: [configuration](configuration.md).

- `Mcp:EnableUnsafeLua` (on): `lua_execute`, tables that carry Lua, and Lua in scripts passed to `asm_apply`.
- `Mcp:EnableAutoAssembler` (on): `asm_check`, `asm_apply`, `asm_apply_code_patch`, and creating, scripting,
  activating or deleting Auto Assembler records.
- `Mcp:EnableTargetCodeExecution` (on): `exec_*` (remote and local calls, DLL and .NET injection, C compilation),
  `mono_attach`, `mono_invoke_method`, `mono_compile_method`, `speedhack_set_speed`, the VEH debugger, and an attach,
  launch, file open or table load that makes CE inject its Mono collector.
- `Mcp:EnableKernelAccess` (on): `kernel_*` (the DBK driver and DBVM: physical memory, address translation, DBVM
  watches), the kernel debugger, `symbol_enable_sources(kernel=true)` and `exec_compile_c(kernelMode=true)`.
- `Mcp:Files:AllowedRoots` (empty): folders that `memory_dump_to_file` and `process_save_file` may write to; empty
  refuses every write.
- `CheatEngineClient:AllowedTableRoots` (empty): folders for `table_load`, `table_save` and `table_list_files`; empty
  refuses every table load and save.

Rules for the switches:

- `runtime_get_info.gates` reports them. A switched-off tool stays listed and answers `capability_disabled`
  (`not_started`) before anything runs; its `hint` names the setting. Report it to the user; never route around it.
- Content decides some gates: `asm_apply` and `table_load` read the script or table and require every switch it needs
  (target code execution for `loadlibrary`, say, or the Lua, Auto Assembler and target-code switches together for a
  table that cannot be inspected, such as a `.CETRAINER`). `asm_check` needs only the Auto Assembler switch: it
  refuses, as `unsupported`, a script that would need another switch, uses `globalalloc` or evaluates Lua through `$`;
  review such a script by reading it.
- `debugger_attach(interface="windows")` needs no switch. `debugger_attach(interface="default")` follows CE's debugger
  setting: VEH needs target code execution, the kernel debugger needs kernel access, and CE's DBVM debugger is refused
  as `unsupported` whatever the switches. Details: [debugger](debugger.md).
- These are exposure switches, not a sandbox. With `Mcp:EnableUnsafeLua` on, caller Lua can do what the other switches
  withhold, and an Auto Assembler record's script is checked against the Auto Assembler switch only, whatever it
  contains.
- A prompt that needs a switch names it at the end of its description.

## Documents

Each document is served as `cheatengine://docs/<slug>`. Pick the one whose cue matches the task and read it before
calling the tools it covers.

### Rules, terms and failures

| Document | Read it when |
|---|---|
| [workflows](workflows.md) | before target work: session rules (ids, paging, scanners, jobs, target switches, errors) and the prompt index |
| [safety](safety.md) | before the first change: allowed targets, consent, backups, what the switches stop, what MCP tracks and undoes |
| [glossary](glossary.md) | a term or result field is unfamiliar, such as AOB, code cave, pointer chain, host effect or job |
| [tool-map](tool-map.md) | you need the tool for a task, with the switch it needs and its dispatch class |
| [errors-and-recovery](errors-and-recovery.md) | a call failed: its kind, host effect and next step, and releasing or acknowledging resources |

### Values, scans and memory

| Document | Read it when |
|---|---|
| [address-expressions](address-expressions.md) | writing an address, offset or number argument: symbols, `[base]+offset`, hex and signed forms |
| [value-types](value-types.md) | choosing a scan, memory or record type, or decoding how a game stores numbers, text and pointers |
| [value-scans](value-scans.md) | planning a value scan: main or named scanner, comparisons, floats, narrowing, snapshots, verification |
| [troubleshooting-scans](troubleshooting-scans.md) | a scan finds nothing or too much, or the value reverts, will not freeze or moves |
| [memory-model](memory-model.md) | a read fails, an address needs classifying (module, static, heap, stack) or a protection must change |

### Pointers, structures and code

| Document | Read it when |
|---|---|
| [pointers](pointers.md) | an address changes after a restart: pointer chains, pointer maps, path scans and rescans |
| [structures](structures.md) | naming the fields around a base, identifying an object or comparing instances |
| [code-analysis](code-analysis.md) | reading disassembly, functions, references and strings to learn what a value means or where to patch |
| [debugger](debugger.md) | before a breakpoint, capture or trace: interfaces, switches, slots and leaving the target running |

### Patches, records and scripts

| Document | Read it when |
|---|---|
| [x64-injection](x64-injection.md) | writing code to inject: registers, flags, SSE floats, jump sizes, RIP-relative operands |
| [auto-assembler](auto-assembler.md) | writing, checking or applying an Auto Assembler script or a code patch |
| [aob-signatures](aob-signatures.md) | a patch or record must survive game updates: making, proving or repairing a byte signature |
| [cheat-tables](cheat-tables.md) | creating, freezing, grouping or activating records, or loading and saving a table |
| [cheat-recipes](cheat-recipes.md) | the user names a goal (infinite health, money, teleport, no cooldown): where it lives, which route to take |
| [speedhack](speedhack.md) | before changing game speed: the hooks, their lasting effects and the restore |
| [lua](lua.md) | no dedicated tool covers a CE feature and `lua_execute` is the last resort |
| [lua-api](lua-api.md) | writing a `lua_execute` chunk: CE 7.7 Lua functions, known traps and the tool to prefer |

### Engines and runtimes

| Document | Read it when |
|---|---|
| [game-engines](game-engines.md) | right after attaching: identify the engine or runtime and predict how it stores values |
| [mono-and-dotnet](mono-and-dotnet.md) | the game loads `mono-2.0-bdwgc.dll` or `mono.dll` (Unity Mono), or `coreclr.dll` or `clr.dll` (.NET) |
| [unity-il2cpp](unity-il2cpp.md) | the game loads `GameAssembly.dll` (Unity IL2CPP) |
| [unreal-engine](unreal-engine.md) | the game is built on Unreal Engine 4 or 5 |
| [emulators](emulators.md) | the target is an emulator: guest RAM, mapped memory, big-endian values |

### Setup, limits and practice

| Document | Read it when |
|---|---|
| [connection-troubleshooting](connection-troubleshooting.md) | setting up, or `instance_list` is empty, an instance is unavailable, or calls time out or are busy |
| [configuration](configuration.md) | a setting matters: the switches, execution limits, file and table roots, logs |
| [kernel](kernel.md) | the user asks for DBK or DBVM features (physical memory, page watches); usually unavailable, with hazards |
| [ce-tutorial](ce-tutorial.md) | learning, or smoke-testing a new setup, on CE's tutorial and gtutorial |

## Guided workflows and live resources

- Guided workflows are MCP prompts; each body also reads as `cheatengine://docs/workflows/<name-with-dashes>`, and
  [workflows](workflows.md) indexes them all. Start with [attach_and_orient](../Workflows/attach-and-orient.md) and
  [engine_triage](../Workflows/engine-triage.md). Use [plan_cheat](../Workflows/plan-cheat.md) with
  [cheat-recipes](cheat-recipes.md) when the user names a goal (infinite health, teleport) but no technique,
  [ce_tutorial_walkthrough](../Workflows/ce-tutorial-walkthrough.md) to learn or smoke-test on CE's tutorial,
  [explain_error](../Workflows/explain-error.md) after a failure, and [session_report](../Workflows/session-report.md)
  then [cleanup_session](../Workflows/cleanup-session.md) at the end.
- Live resources are read-only JSON views, each the result of one read-only tool. Through the gateway,
  `cheatengine://instances` matches `instance_list`. Once the gateway has verified an instance (through
  `instance_list`, a read of `cheatengine://instances` or a call routed to it), its resource list also carries that
  instance's fixed views, such as `cheatengine://instances/{instanceId}/runtime` (`runtime_get_overview`), titled with
  CE's name and process id.
- Views with a parameter, such as `cheatengine://instances/{instanceId}/modules/{module}`, stay templates. Clients
  that support completion offer `{instanceId}` values for recently verified instances, then `{module}`,
  `{structure}`, `{scannerName}` and `{scanName}`. A direct backend serves the same views as
  `cheatengine://instance/...`. The full list is in [workflows](workflows.md).

## Responsible use and consent

- Work only on software the user owns or may modify: their own programs, single-player or offline games, CE's
  tutorials. Attach only to the process the user named.
- Never online, multiplayer or competitive play, never a game with anti-cheat (it can detect CE and ban the account),
  and never help bypass anti-cheat, DRM or licence checks. Stop when unsure.
- Consent in practice: before a memory write or protection change, a patch, an Auto Assembler or Lua script,
  activating or freezing a record, a debugger attach, a speedhack, a Mono attach, code run in the target, a kernel
  feature, a table load, a process launch or a file write, tell the user what changes, where, the risk (a crash, lost
  progress) and how to undo it, then wait for an explicit yes. A yes covers the change described, not the session.
  Suggest saving game progress first.
- A failure sets `isError` and returns `error.kind`, `error.hostEffect`, `error.retryable` and `error.hint`. When
  `retryable` is true, repeat the identical call once, later. `not_started` or `not_applied` means nothing changed:
  fix the cause, then call again. After `completed`, verify with a read. Never repeat a mutation after a `timeout` or
  when `hostEffect` is `started`, `unknown` or `cleanup_unconfirmed`: inspect the state first. See [safety](safety.md)
  and [errors-and-recovery](errors-and-recovery.md).

## Cleanup

- `runtime_list_resources()` lists what MCP holds in CE or the target (`id`, `kind`, `state`, `orphaned`,
  `requiresManualRecovery`): patches, allocations, named scanners, registered symbols, breakpoints, running jobs, a
  speed other than 1, MCP's pause and a Mono attachment.
- `runtime_release_resources()` releases all of them, newest first: it stops jobs, releases Auto Assembler patches,
  frees allocations, deletes named scanners, removes MCP breakpoints and symbols, restores speed 1, resumes MCP's pause
  and detaches MCP's Mono attachment. Call it only when the user agrees to release everything: before a target switch,
  which needs every holder released, or as the last step of [cleanup_session](../Workflows/cleanup-session.md), which
  first undoes things one by one in a safe order.
- It stops at the first release that does not complete, keeps the older resources and fails with `partial_effect`:
  follow the hint, never repeat blindly. Entries of an earlier plugin activation (`orphaned`) need
  `runtime_release_resources(includeOrphans=true)`. `runtime_release_resources(acknowledgeIds=["<id>"])` forgets an
  entry the user recovered by hand and then releases everything else too, so use it only at the end of cleanup, with
  consent, never to drop one leftover mid-session.
- It does not undo `memory_write` or `memory_set_protection` changes, records and freezes, structures, main scan
  results, a loaded table, a pause the user made or a debugger attachment: cleanup_session handles those. Snapshots,
  pointer maps and pointer scans are MCP memory, not resources: delete them with `memory_delete_snapshot`,
  `pointer_delete_map` and `pointer_delete_scan`. CE's speedhack hooks and the Mono collector DLL stay in the game
  until it exits, and a Mono attach normally leaves the Uses Mono table option set.

## Sources

- CE 7.7 `autorun/monoscript.lua` (installed with CE): the Uses Mono table option, the IgnoreUsesMono setting and the
  attach that sets Uses Mono.
- https://www.cheatengine.org/
- https://modelcontextprotocol.io/specification/2025-11-25/server/prompts
- https://modelcontextprotocol.io/specification/2025-11-25/server/resources
