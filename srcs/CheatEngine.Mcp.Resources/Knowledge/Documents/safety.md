# Safety and responsible use

Cheat Engine (CE) can read and change any process the user can open, run code inside it, and load a kernel driver and
a hypervisor. Read this page before the first change of a session: scope, consent, backups, what the gates do and do
not stop, what MCP tracks, how to restore, and the kernel rules.

## Scope

- **In scope:** software the user owns or may modify, used offline: their own programs, single-player games with no
  online component, and CE's practice targets `Tutorial-x86_64.exe` and `gtutorial-x86_64.exe`
  ([ce-tutorial](ce-tutorial.md), [ce_tutorial_walkthrough](../Workflows/ce-tutorial-walkthrough.md)).
- **Online parts count as online:** global leaderboards, synced achievements, trading or a cloud economy make a game
  online unless the user confirms a fully offline mode.
- **Out of scope, always:** multiplayer or co-op with other people, ranked play, leaderboards, shared economies,
  anything that gives an advantage over other players, anti-cheat-protected games, DRM, licence or purchase checks,
  and other people's data. Answer "not with this tool" and do not explain how a protection works.
- **CE's terms:** cheatengine.org says CE is for private and educational use by adults (18 or older) and asks users to
  check the target's EULA or terms of service before attaching. That is a website notice, not an in-app prompt; CE's
  `license.txt` allows personal use and testing. Remind the user for commercial software; the decision and the
  responsibility are theirs.
- **Consequences to name** for a borderline target: a terms-of-service breach, suspension or a permanent ban
  (sometimes tied to the hardware), lost purchases, and legal exposure (publishers have won civil cases against cheat
  makers, and some countries, South Korea among them, criminalise game cheats).
- Attach only to the process the user named; ask when the name is ambiguous. Never inspect unrelated processes such as
  browsers, password managers or chat clients, and never repeat a secret that a read surfaces.

## Anti-cheat and bans

- Before the first attach to a game, ask whether it has online features or anti-cheat. If it does, stop, unless the
  user confirms an offline build without anti-cheat.
- Never attach to a protected game, not even "just to look": it can get the account banned permanently.
- Do not discuss how anti-cheat or DRM detects tools, or how to avoid it. The CE table community at FearLess also
  forbids multiplayer cheat requests.

## Consent for each change

Consent covers one change, not the session. Before each write (memory, register or physical), protection change,
pause, Auto Assembler script, patch or script-record activation, `lua_execute`, code execution or injection
(`exec_call_remote`, `mono_invoke_method` and the like), `mono_attach`, `debugger_attach`, `speedhack_set_speed`,
`process_create`, `table_load`, file write or kernel call: say what will change, what could break and how to undo it,
then wait for a yes. Ask again when the target or the risk level changes, for example from user mode to the kernel.

- Look before you change: resolve, read and scan first, and write only addresses you identified.
- `may_prompt` tools (`_meta` `cheatengine/dispatchClass`) can open a CE dialog that waits for a person: `asm_apply`,
  `asm_apply_code_patch`, `asm_release_patch`, `debugger_attach`, `kernel_initialize_dbvm`, `lua_execute`, `mono_attach`,
  `record_set_active`, `record_delete`, `record_clear`, `runtime_release_resources`, `speedhack_set_speed`,
  `table_load`, `table_save`. Warn the user that they may have to answer it in CE, including during script cleanup.
- `blocking_native` tools can hold CE's main thread for seconds. `exec_inject_dotnet` waits without a time limit until
  the injected method returns; only the MCP call timeout bounds the request, and CE stays blocked after it.
- `exec_call_remote` and `exec_call_method` run the function on a new thread in the target, not on the game's own
  thread, so engine code that expects its thread or its locks can deadlock or crash. A timeout leaves that thread
  running with `hostEffect` `unknown`: inspect the target, never call again blindly.
- Read-only annotations are hints. `asm_check` stays read-only by refusing, as `unsupported`, any script that CE would
  partly run while checking it (`{$lua}`, `{$c}`, `luacall`, `loadlibrary`, `globalalloc`, a `$` Lua token and the
  like); judge such a script by reading it ([review_aa_script](../Workflows/review-aa-script.md)). Any tool that
  resolves an address can still run Lua (see "Gates are switches, not a sandbox").

## Backups before the first change

- **Game saves:** make an in-game save, then copy the save folder (often under `Saved Games`, `Documents\My Games` or
  `%AppData%`) elsewhere. Cloud sync can spread a corrupted save, so keep the copy until the game reloads cleanly.
- **The user's table:** `table_save(path="<path under an allowed table root>")` first. `table_load(merge=false)`
  replaces the whole address list without running any `[DISABLE]` section and also drops the table's structures,
  comments, forms and Lua files; a `.CETRAINER` always replaces. `record_clear` deletes every record, and active
  scripts may run their `[DISABLE]` sections.
- **Memory:** `memory_write` returns the range's first 64 bytes as `previous`, so read a longer range first;
  `memory_set_protection` returns the range's `previous` access. `memory_write_batch`, `memory_copy`,
  `memory_load_from_file` and `structure_write_element` return no old bytes: `memory_read` the range first.
- **State you will change:** read `speedhack_get_state`, `symbol_get_module_preference`, `process_get_current` (pointer
  size), `code_get_comments` and `record_get` for the records you will edit; before replacing a dropdown, call
  `record_get(ids=[...], includeDropdown=true)`.
- **Before any kernel call:** the user saves all work in every application; a crash loses it everywhere.
- Prefer reversible tools: on code, `asm_apply_code_patch` (it checks and restores the exact original bytes) or an
  `asm_apply` script with a working `[DISABLE]` section, not `memory_write`. Patch whole instructions, and test
  injected code on the smallest scope first: it runs on every thread that reaches it.

## Gates are switches, not a sandbox

Four settings expose the risky tools, all `true` by default; `runtime_get_info.gates` reports them for this plugin
activation. A need whose setting is off returns `capability_disabled` with `hostEffect` `not_started` before anything
changes, and the `hint` names the setting. Each tool publishes its fixed needs in `_meta` `cheatengine/requires`; the
other needs depend on an argument or on content and are checked when the call runs. The full rules are in
[configuration](configuration.md#capability-gates); in short:

- **`Mcp:EnableUnsafeLua`:** caller Lua: `lua_execute`, Lua in Auto Assembler (AA) scripts such as `{$lua}` or
  `luacall`, and a table with a Lua script or forms.
- **`Mcp:EnableAutoAssembler`:** `asm_check`, `asm_apply`, `asm_apply_code_patch`, AA records (create one or pass a
  script, set its script, activate or deactivate it, also through a parent, delete or clear it while active) and a
  table with AA scripts.
- **`Mcp:EnableTargetCodeExecution`:** code run in the target or in CE: `exec_call_local`, `exec_call_method`,
  `exec_call_remote`, `exec_compile_c`, `exec_inject_dotnet`, `exec_inject_library`, `mono_attach`,
  `mono_compile_method`, `mono_invoke_method`, `speedhack_set_speed`, `debugger_attach(interface="veh")`, AA `{$c}`,
  `loadlibrary` or `createthread`, and a process open or table load that the table's UsesMono option turns into a Mono
  injection (`process_attach.monoAutoAttach` and `table_load.monoAutoAttach` report it).
- **`Mcp:EnableKernelAccess`:** DBK and DBVM: all kernel tools, `kernel_get_status` included,
  `debugger_attach(interface="kernel")`, `exec_compile_c(kernelMode=true)` on top of target code execution,
  `symbol_enable_sources(kernel=true)` and AA `kalloc`.
- AA constructs that reach further, such as `{$luacode}`, `include`, `loadbinary` or an unknown or Lua-registered
  command like `USEMONO`, need both unsafe Lua and target code execution.
- `debugger_attach(interface="default")`, also used when `interface` is omitted, always reads the debugger method
  selected in CE's settings first. VEH then needs target code execution, and the kernel debugger needs kernel access
  (`capability_disabled`); the DBVM debugger, a setting MCP cannot identify, or a ceserver connection is refused as
  `unsupported` whatever the switches. `debugger_attach(interface="windows")` needs no switch.
- A table MCP cannot inspect as plain XML (a `.CETRAINER`, a protected, binary, obfuscated or malformed table, or one
  over 64 MiB) needs unsafe Lua, Auto Assembler and target code execution together.

What the switches do not do:

- **Enabled does not mean safe.** Every enabled tool acts with CE's full rights, and `Mcp:EnableUnsafeLua` includes all
  the others, because `lua_execute` can call any CE API.
- **Record scripts are not classified.** `record_create`, `record_set_script` and `record_set_active` check only the
  Auto Assembler switch, so a stored script with `{$lua}` or `createthread` runs on activation even when the other
  switches are off. Read `record_get.script` and activate only scripts you have read; `asm_check` refuses a script
  with such constructs as `unsupported`, which flags it but proves nothing else.
- **Address expressions can run Lua.** CE's symbol handler runs a `$` token such as `$player` as Lua, and CE 7.7's
  `autorun/luasymbols.lua` reads any name it cannot resolve as a Lua expression. This happens in every tool that takes
  an address, read-only ones included, whatever `Mcp:EnableUnsafeLua` says; MCP does not screen expressions (only
  `asm_check` refuses a `$` Lua token). Keep Lua out of addresses and run Lua only through `lua_execute`, with consent
  ([address expressions](address-expressions.md)).
- **Much happens outside MCP:** hotkeys, the user's clicks, autorun Lua, table scripts, and CE settings that route
  memory access through the kernel driver (see "CE settings that reach the kernel").
- **`capability_disabled` is the operator's decision.** Report the setting the `hint` names. Never route around it with
  `lua_execute`, a table, a record script, an AA `{$lua}` block, an address expression or another instance. Only the
  user changes a setting, and it takes effect after the plugin is disabled and re-enabled.
- **Host files and network:** file writes need `Mcp:Files:AllowedRoots` and tables need
  `CheatEngineClient:AllowedTableRoots`, both empty by default; never suggest a root that covers a drive, a system
  folder or unrelated data. `symbol_enable_sources(windows=true)` can download PDBs from Microsoft's public symbol
  server; say so first.

## What MCP tracks and what it does not

`runtime_list_resources` (live: `cheatengine://instance/resources`) lists what this plugin activation holds, newest
first (patches, allocations, named scanners, registered symbols, breakpoints, a speed other than 1, a pause MCP made,
the Mono attachment, every job), then Lua state that an earlier activation left (`orphaned`) and failed cleanups
awaiting an acknowledgement. While any of them holds state, `process_attach`, `process_create` and `process_open_file`
refuse with `busy` (re-selecting the same process is allowed); the resources this activation still tracks also block
`debugger_detach`. Snapshots, pointer maps and pointer scans are MCP memory, not resources: they never block and end
with the activation. Nothing else is tracked, and a release does not undo everything:

| Change | How to undo it |
|---|---|
| `process_set_paused(paused=true)` | `process_set_paused(paused=false)`. MCP tracks its own pause as a resource (`resourceId`); a pause the user made is not tracked. |
| `memory_write`, `memory_set_protection` | Write back or set back the returned `previous`: 64 bytes at most for a write; read, write and execute only for a protection, so copy-on-write and guard flags are not restored. |
| `memory_write_batch`, `memory_copy`, `memory_load_from_file`, `structure_write_element`, `kernel_write_physical` | Restore the bytes you read first. |
| Records, freezes, `table_load`, `record_clear` | Delete what you created, or reload the user's saved table. |
| `record_set_dropdown` | Set back the items and options that `record_get(ids=[...], includeDropdown=true)` returned. |
| Structures, comments, dissect data | `structure_delete`; `code_set_comment` with the earlier text; `code_clear_dissect` clears all dissect data, the user's too. |
| `symbol_set_module_preference` | `symbol_set_module_preference(modules=[...], replace=true)` with the list you read; only a list of 1 to 64 names can be put back. |
| `symbol_enable_sources`, `symbol_add_module` | None; the symbols stay for this CE session. |
| `scan_first.includeMapped`, `aob_find.includeMapped`, `aob_find_value.includeMapped` | None: the call ends every CE scan-region override, one that a table or `lua_execute` set included. |
| `process_set_pointer_size` | Set the size `process_get_current` reported before. |
| `exec_inject_library`, `exec_inject_dotnet`, `exec_compile_c` | None; a target restart removes the code. `exec_compile_c(targetSelf=true)` code stays in CE until it restarts, and `exec_compile_c(kernelMode=true)` code stays in kernel memory until a reboot. |
| `exec_call_local`, `exec_call_remote`, `exec_call_method`, `mono_invoke_method`, `lua_execute` | Undo by hand whatever the code changed. |
| Speedhack hooks | They stay until the target exits, even at speed 1 ([speedhack](speedhack.md)). |
| `mono_attach` | `mono_detach`, which closes only the attachment MCP made; the collector DLL, its patch and the UsesMono option remain. |
| `debugger_attach` | `debugger_detach`; after VEH, restart the target before another attach. |
| DBK loaded, DBVM running | A reboot (see "Kernel, DBK and DBVM"). |

`symbol_set_module_preference` affects every later lookup, tables included. While the table's UsesMono option stays
set, CE injects the Mono collector again when a process opens or a table loads.

## Restoring state

- Undo what the session created, newest first, with [cleanup_session](../Workflows/cleanup-session.md): stop jobs;
  delete MCP's breakpoints, then continue a stopped debugger; deactivate records; release patches; resume MCP's pause,
  then set speed 1; with consent, delete the records and structures you made; delete scanners, pointer scans, pointer
  maps and snapshots; after the patches, unregister symbols and free allocations; `mono_detach()`;
  `runtime_release_resources()`; `debugger_detach()` last (it refuses while any tracked resource remains, and
  unpauses the target). Confirm with `runtime_list_resources()` and `runtime_get_overview()`.
- Stop at the first release that does not complete and report it. Only at the end of cleanup, once the user confirms
  that an entry is undone by hand, acknowledge it with `runtime_release_resources(acknowledgeIds=["<id>"])`: the same
  call then releases every other tracked resource, so never use it mid-session to forget one leftover
  ([errors-and-recovery](errors-and-recovery.md#releasing-owned-resources)).
- Disabling the plugin is not cleanup: CE-owned state (the address list with its freezes and scripts, structures, the
  main scan, a pause the user made, a Mono collector) stays, and MCP's leftover breakpoints, speed or pause become
  orphans for the next activation.
- Preserve what the session did not create: the user's records, structures, main scan results, symbols and table file.
- Report every change and its `hostEffect`. `partial_effect` is a failure: say exactly what applied. Never repeat a
  mutation after a `timeout`, a lost connection, or a `hostEffect` of `started`, `unknown` or `cleanup_unconfirmed`;
  read the state first ([errors-and-recovery](errors-and-recovery.md)).
- Name the side effects the user cannot see: an injected DLL, speedhack hooks, an attached debugger, an open CE dialog.

## Kernel, DBK and DBVM

The `kernel_*` tools use CE's DBK kernel driver and DBVM hypervisor. A mistake can freeze or blue-screen the whole
machine and lose unsaved work in every program. CE's own dialog warns of a high chance that running DBVM crashes the
system, and the CE wiki puts the blue-screen risk of starting DBVM at about one in five per CPU core, probably counting
hyper-threads. Details: [kernel](kernel.md).

- **Before:** call `kernel_get_status` (it loads nothing). Use kernel tools only on a machine the user can afford to
  crash, with all work saved, and get consent for each call.
- **Unavailable is normal.** Since the Windows 11 2022 update, Microsoft's vulnerable driver blocklist is on by default
  and denies CE's driver (`dbk32.sys`, `dbk64.sys`). Memory integrity, on by default on clean Windows 11 installs on
  compatible hardware, runs Windows under Microsoft's hypervisor, which leaves no room for DBVM. An `unsupported` or
  `invalid_state` result is the expected, safe outcome, not a problem to solve.
- **Hard rule: Windows protections stay on.** Never advise turning off, or explain how to turn off, memory integrity
  (HVCI), the vulnerable driver blocklist, Secure Boot, driver signature enforcement or test-signing mode, Hyper-V or
  virtualization-based security, or the Spectre and Meltdown mitigations, even when a CE dialog suggests it. Never
  suggest CE's **Make possible** button (Settings > Debugger Options): it turns the Spectre v2 and Meltdown mitigations
  off system-wide after a reboot. If the user already weakened a protection, name the risk and give the restore path:
  CE's **Restore Protection** button then a reboot, or Windows Security > Device security > Core isolation details >
  Memory integrity on, then a restart.
- **DBVM:** `kernel_initialize_dbvm(offloadOperatingSystem=false)` loads nothing; it only confirms that DBVM already
  runs and fails with `invalid_state` otherwise. `kernel_initialize_dbvm(offloadOperatingSystem=true, reason="...")`
  first loads DBK without a prompt, then shows CE's crash warning followed by the reason, which a person must answer.
  Never retry an unknown outcome; call `kernel_get_status` once the host responds. Kernel access never lets MCP drive
  the DBVM debugger.
- **Physical memory:** `kernel_write_physical` can overwrite Windows, a driver or another process: translate with
  `kernel_translate_address` just before, stay inside one page, and read the range first. `kernel_start_watch` sees
  every access to the page, from any process or the kernel.
- **Recovery:** power-cycle after a freeze if needed. A reboot removes DBVM (CE cannot unload it) and DBK (a
  demand-start driver). `Kernelmoduleunloader.exe` in the CE folder unloads DBK without a reboot, but CE warns that an
  enabled process watcher stops it from working. If CE crashes at startup, `ceregreset.exe` resets every CE setting.

## CE settings that reach the kernel

MCP never changes CE settings, and these bypass `Mcp:EnableKernelAccess`. CE saves them and applies them at every
start:

- Settings > Extra, "Use the following CE Kernel routines instead of the original windows version": **Query memory
  region routines**, **Read/Write Process Memory**, **Open Process**; and **Enable use of the Process Watcher**. While
  one is set, CE loads DBK at every start. The three routines also send CE's memory access through DBK whatever the
  gates say, including plain `memory_read`, scans, `memory_list_regions` and the live `cheatengine://instance/regions`
  resource.
- Settings > Debugger Options, Debugger method: **Kernelmode debugger** or **DBVM-level debugger**. A debugger
  attached from CE's own UI then uses DBK or DBVM. `debugger_attach(interface="default")` refuses the DBVM-level
  debugger as `unsupported` whatever the switches, and the Kernelmode debugger while `Mcp:EnableKernelAccess` is off.

When the user wants no kernel involvement, ask them to clear these options, choose the Windows debugger, and restart
CE (a DBK already loaded stays until a reboot or the unloader), and prefer `debugger_attach(interface="windows")`.

## Instances and secrets

- Two CE instances attached to the same target share its memory: a freeze in one fights the writes of the other.
  Prefer one instance per target, and never move a failed call to another instance, even one with the same name.
- Each backend listens only on 127.0.0.1, behind a per-activation bearer token that the gateway reads from the instance
  registry. Never read, print or copy registry records or tokens. Never expose a backend on another interface or weaken
  its authentication to fix a connection; follow [connection-troubleshooting](connection-troubleshooting.md).

## Sources

- CE terms: https://www.cheatengine.org/ and `license.txt` in the CE 7.7 install; FearLess:
  https://fearlessrevolution.com/viewtopic.php?t=5718
- DBVM risks: https://wiki.cheatengine.org/index.php?title=DBVM
- CE source (https://github.com/cheat-engine/cheat-engine): `formsettingsunit.pas` and `MainUnit2.pas` (Extra options,
  debugger method, Make possible, Restore Protection, settings applied at start), `NewKernelHandler.pas` and
  `LuaHandler.pas` (DBVM loading), `autoassembler.pas` (what runs during a check), `symbolhandler.pas` (`$` tokens run
  as Lua), `OpenSave.pas` (what a table load clears); install files `autorun/monoscript.lua` (UsesMono, Mono AA
  commands), `autorun/luasymbols.lua` (unknown names read as Lua), `Kernelmoduleunloader.exe`, `ceregreset.exe`.
- Microsoft driver block rules:
  https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules
- Memory integrity default enablement:
  https://learn.microsoft.com/windows-hardware/design/device-experiences/oem-hvci-enablement and
  https://learn.microsoft.com/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity
- Spectre and Meltdown settings: Microsoft KB4072698.
- Bans and legal cases (secondary): https://en.wikipedia.org/wiki/Cheating_in_online_games
