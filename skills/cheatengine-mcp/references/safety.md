# Safety and responsible use

Cheat Engine (CE) can read and change any process the user can open, run code inside it, load a kernel driver and a hypervisor, and alter CE itself. These rules bound what you do with that power. They apply to every tool, prompt and resource, whatever the gates allow.

## Authorization and scope

- Work only on software the user owns or is entitled to modify: their own programs, single-player or offline games, the CE tutorials, disposable test targets.
- CE's publisher limits it to private and educational use and asks users to confirm, before attaching, that they are not breaking the target's EULA or terms of service. Remind the user of this for commercial software; the decision and the responsibility are theirs.
- Refuse: cheating in multiplayer or online modes, anything that gives an advantage over other players, tampering with online economies, ranked play or leaderboards, bypassing anti-cheat, DRM, license or purchase checks, and harvesting other people's data. The large CE table community at FearLess forbids multiplayer cheat requests for the same reason.
- Attach only to the process the user named. Never pick the closest match; ask when the target is ambiguous.
- Never inspect unrelated processes such as browsers, password managers or chat clients. Process memory holds credentials and personal data; if a read surfaces a secret, do not repeat it.

## Anti-cheat and bans

- Games protected by anti-cheat (VAC, Easy Anti-Cheat, BattlEye, kernel-level systems) can detect CE, its driver, its debugger, speedhack hooks and injected code. Community reports describe bans that are permanent.
- Before attaching, ask whether the game has online features or anti-cheat. If it does, stop unless the user confirms an offline mode without anti-cheat or a build they control.
- Advise the user to close CE before launching a protected game, and never to attach to a protected process "just to look".
- Detection surfaces to mention when relevant: debugger interfaces and software breakpoints ([debugger](debugger.md)), the speedhack's compiled helper code and patched time functions ([speedhack](speedhack.md)), the Mono data collector that `mono_attach` and the Unity speedhack path inject ([mono-and-dotnet](mono-and-dotnet.md)), DBK and DBVM ([kernel](kernel.md)).

## Before any change

- Save progress first: an in-game save, the user's table (`table_save` under an allowed root), and in the CE tutorial the current step password. Patches, injections and debugger mistakes can crash the target and lose unsaved work.
- Record the state you will restore: `memory_write` returns `previous`; read `speedhack_get_state`, `process_get_current` (`paused`) and `record_get` before changing them.
- Explain the effect and get consent before destructive or host-affecting calls: memory and register writes, Auto Assembler scripts and patches, activating script records, code execution (`exec_*`, `mono_invoke_method`), injection, `process_create`, `table_load` (tables can run Lua), kernel tools and file writes.
- Prefer reversible tools: `asm_apply_code_patch` or scripts with a working `[DISABLE]` section over raw `memory_write` on code. Patch whole instructions only.
- Injected code runs on every thread that reaches it; test on the smallest scope first.

## Gates are switches, not a sandbox

| Setting | Governs |
| --- | --- |
| `Mcp:EnableUnsafeLua` | `lua_execute`; Lua inside Auto Assembler (`{$lua}`, `{$luacode}`, `luacall`); tables that carry Lua |
| `Mcp:EnableAutoAssembler` | `asm_check`, `asm_apply`, `asm_apply_code_patch`, script records |
| `Mcp:EnableTargetCodeExecution` | `exec_*`, `process_create`, `speedhack_set_speed`, `mono_attach`, `mono_compile_method`, `mono_invoke_method`, the VEH debugger, C code, `loadlibrary` or `createthread` in Auto Assembler, and any attach or table load that would start CE's Mono collector |
| `Mcp:EnableKernelAccess` | `kernel_*`, the kernel debugger interface |

- All four default to `true`. Enabled does not mean safe: fixed-script tools can do anything CE can.
- `capability_disabled` means the operator switched a capability off. Report the setting named in `hint`. Never route around it with `lua_execute`, a table, an Auto Assembler `{$lua}` block or another instance. Changing a setting is the user's decision and needs a plugin disable and re-enable.
- File writes (dumps, saved files) need `Mcp:Files:AllowedRoots`, empty by default so writes are refused; tables need `CheatEngineClient:AllowedTableRoots`. The instance registry and the MCP data directory are always refused. Never suggest roots that cover system or unrelated folders.

## Honest reporting

- Report every mutation's outcome and `hostEffect`. `partial_effect` is a failure: list exactly what applied. Never call an incomplete release or cleanup done.
- Never retry a mutation blindly. After `timeout`, a lost connection, or `hostEffect` `started`, `unknown` or `cleanup_unconfirmed`, read the state with read-only tools first; see [errors-and-recovery](errors-and-recovery.md).
- Name side effects the user cannot see: a DLL injected by `mono_attach`, speedhack hooks compiled into the target (they stay until it exits, even at speed 1), an attached debugger, a CE dialog left open.

## Restoration duties

- Undo what the session created, newest first, with [cleanup_session](workflows/cleanup-session.md): stop jobs, delete breakpoints, release patches, deactivate records, restore speed and pause, delete session structures and scanners, call `runtime_release_resources`, detach the debugger.
- CE-owned state survives a plugin disable: the address list and its freezes, structures, the main scan tab, breakpoints, speedhack, pause, the Mono collector. Disabling the plugin is not cleanup.
- Preserve what the session did not create: existing records, structures, the main scan results, registered symbols, the user's table file.

## Several instances

- Two CE instances attached to the same target share its memory: a freeze in one fights writes from the other, and patches can overlap. Prefer one instance per target, or coordinate explicitly.
- Never move a failed call to another instance, even one with the same display name.

## Secrets and transport

- Each backend listens only on 127.0.0.1 behind a per-activation bearer token that the gateway reads from the instance registry. Never read, print, copy or summarize registry records or tokens, and never place them in results, logs or reports.
- Never expose a backend on another interface or weaken its authentication to fix connectivity; use [connection-troubleshooting](connection-troubleshooting.md).

## Kernel access

- `kernel_*` tools use CE's DBK driver and DBVM hypervisor. A mistake can freeze or blue-screen the whole machine and lose unsaved work in every program, not only the game. Use them only on a machine the user can afford to crash, with all work saved.
- A blue-screen hazard is reported with CE's "Query memory region routines" setting combined with DBVM; read [kernel](kernel.md) before any kernel call. MCP never changes CE settings.

## Sources

- https://www.cheatengine.org/
- https://fearlessrevolution.com/viewtopic.php?t=5718
- https://steamcommunity.com/discussions/forum/9/601912617062143726/
- https://steamcommunity.com/discussions/forum/9/558749191094081728/
- https://wiki.cheatengine.org/index.php?title=Help_File:Debugger_options
- https://github.com/cheat-engine/cheat-engine/issues/3334
