# Glossary

Short definitions of Cheat Engine (CE), memory and CheatEngine.Mcp terms, each with the tool that uses it and the page
that explains it. Read it when a term in a result, an error or another page is unclear. Changes to a target are only
for single-player or offline software the user may modify, with consent: see [safety](safety.md).

## Cheat Engine and memory terms

- **Address expression**: CE's text form of an address: `7FF6A1B2C3D0`, `game.exe+1A2B30`, `kernel32.CreateFileW`, a
  registered symbol, or brackets that read the pointer stored at an address, as in `[[game.exe+10]+8]+4C8`. Every
  number is hex. Test one with `symbol_resolve(expressions=["game.exe+1A2B30"])`. See
  [address expressions](address-expressions.md).
- **Address list, record**: the list in CE's main window. A record is a value at an address or pointer chain, an AA
  script or a group (`record_list`); a value record can offer a dropdown of named values (`record_set_dropdown`).
  Record ids are CE's own and expire when a table loads: list the records again. See [cheat tables](cheat-tables.md).
- **AOB, signature**: an array of bytes in hex, with `??` for any byte, searched with `aob_find`. A signature is an AOB
  that matches once in its module, so it finds the same code after a restart. `aob_generate_signature` builds and
  verifies one, and its `generator` says how. `cheat_engine`, CE's own generator for a module of at most 64 MiB,
  wildcards likely-changing bytes around the instruction but not inside it, so review its displacements. `managed`,
  for a larger module, adds whole instructions and wildcards their displacements and large constants. See
  [AOB signatures](aob-signatures.md).
- **Auto Assembler (AA)**: CE's patch script: assembly plus commands such as `alloc` and `registersymbol`, in an
  `[ENABLE]` section and a `[DISABLE]` section that undoes it. `asm_check` has CE check ENABLE, then DISABLE if ENABLE
  passed. CE runs parts of a script while checking it, so `asm_check` refuses as `unsupported` a script whose content
  needs another switch than `Mcp:EnableAutoAssembler`. `asm_apply` applies a script (that gate, plus any its content
  needs). See [Auto Assembler](auto-assembler.md).
- **Byte order, big-endian**: x86 and x64 store numbers little-endian, low byte first; several emulated consoles
  (GameCube, Wii, PS3) store them big-endian. `memory_read(address="...", valueType="int32", byteOrder="big_endian")`
  decodes such a number, and `memory_write` and the sample and batch memory tools take the same `byteOrder`. Scans and
  `aob_find_value` do not: search for the byte-swapped value. See [emulators](emulators.md).
- **Cheat table (.CT)**: an XML file with the address list, AA scripts, structures and an optional Lua script.
  `table_load` loads one (.CT, .XML or .CETRAINER) from a root in `CheatEngineClient:AllowedTableRoots`, which is empty
  by default and then refuses every load and save. It inspects the file first and enforces the gates that its Lua,
  forms, AA scripts and Uses Mono option need. A load never reactivates records saved as active: CE writes that state
  but never reads it back. The table's Lua, which CE may ask the user to run, can still activate records. Record ids
  from before the load are stale. See [cheat tables](cheat-tables.md).
- **Code injection, code cave**: an instruction replaced by a `jmp` to a cave, memory CE allocates, that runs new code
  and the replaced bytes, then jumps back. A 5-byte `jmp` reaches +/-2 GB, so the cave is allocated near the
  injection point. `asm_generate_injection` writes such a script without applying it. See
  [x64 injection](x64-injection.md).
- **Data collector**: CE's helper that reads a managed runtime. The Mono collector is a DLL (`MonoDataCollector64.dll`
  or the 32-bit build) that CE injects into a Mono or IL2CPP game through `mono_attach` (gate
  `Mcp:EnableTargetCodeExecution`), the Uses Mono option or a Unity speedhack. The `mono_` tools query it over a pipe,
  and the DLL stays after `mono_detach`. The .NET collector (`DotNetDataCollector64.exe`) is a separate process that
  reads a .NET game without injecting anything, for the `dotnet_` tools. A `mono_` or `dotnet_` read without its
  collector returns `not_attached`. See [Mono and .NET](mono-and-dotnet.md).
- **DBK, DBVM**: CE's kernel driver and its hypervisor (gate `Mcp:EnableKernelAccess`). `kernel_get_status` reports
  whether they already run and loads nothing. On a default Windows 11 PC the vulnerable driver blocklist and memory
  integrity usually keep them from loading; never ask the user to weaken those protections. See [kernel](kernel.md).
- **Display copy**: a shown value that the game recomputes from another; writing or freezing it changes nothing
  lasting, so find the source value. See [troubleshooting scans](troubleshooting-scans.md).
- **Dissect**: "dissect code" indexes the references, strings and functions of a range: a `code_start_dissect` job,
  then `code_find_references` or `code_find_strings` ([code analysis](code-analysis.md)). "Dissect data" lays a named
  structure over an object: `structure_autoguess` drafts one and `structure_read` reads it
  ([structures](structures.md)).
- **Fast scan, alignment**: CE's fast scan, on by default, checks only addresses aligned to the value's size (4 for
  4-byte, 8-byte, float and double, 2 for 2-byte), so it misses a packed or misaligned field. `main` follows CE's
  setting; a named scanner checks every address unless `scan_first.alignment` is set. See
  [value scans](value-scans.md).
- **File as process**: CE's mode that opens a file from disk as the target, through
  `process_open_file(filename="...")`, which changes the target like `process_attach`. The memory tools then read and
  write the file's bytes at `process_open_file.startAddress` plus the file offset, and `process_save_file` writes the
  edited image to a new file inside `Mcp:Files:AllowedRoots`. See [configuration](configuration.md).
- **Find out what writes or accesses**: CE's debugger commands that list the instructions that write to or access an
  address, and "find out what addresses this instruction accesses". Here they are capture jobs:
  `debugger_start_capture(address="...", trigger="write")` (or `"access"`), and
  `debugger_start_capture(address="...", trigger="execute", groupByEffectiveAddress=true)`, whose items give each
  accessed `effectiveAddress`. See [find a writer](../Workflows/find-writer.md) and [debugger](debugger.md).
- **First scan, next scan**: `scan_first` finds the addresses that hold a value, or keeps every address with
  `scan_first(comparison="unknown")`; `scan_next` keeps those that meet a new condition, such as
  `scan_next(comparison="decreased")`. See [value scans](value-scans.md).
- **Freeze**: an active value record (`record_set_active(ids=[...], active=true)`). CE writes the value back on a
  timer, every 100 ms by default (Settings, Freeze interval), and the game runs in between. See
  [freeze a value](../Workflows/freeze-value.md).
- **Hardware breakpoint**: a watch in one of the CPU's four debug registers, on execute, write or access (read or
  write) of 1, 2, 4 or 8 bytes (`debugger_set_breakpoint(method="hardware")`, `debugger_start_capture`). A data hit
  traps after the instruction: a captured hit's `ip` is the next instruction and `instructionAddress` the likely
  writer, a guess when `isHeuristic` is true. See [find a writer](../Workflows/find-writer.md).
- **Mapped memory (MEM_MAPPED)**: file views and shared sections, where emulators such as Dolphin keep guest RAM. CE's
  scans skip it unless its scan settings include it, and `main` follows those settings. `scan_first.includeMapped` on
  a named scanner, `aob_find.includeMapped` and `aob_find_value.includeMapped` scan it for one call, then end every
  scan-region override, including one that a table or `lua_execute` set. See [emulators](emulators.md).
- **Mono, IL2CPP**: Unity's two scripting back ends. Mono (`mono-2.0-bdwgc.dll` or `mono.dll`) JIT-compiles the
  game's .NET code at run time; IL2CPP converts it ahead of time into native code in `GameAssembly.dll`, with the names
  only in `global-metadata.dat`. `mono_attach` injects CE's data collector into either (gate
  `Mcp:EnableTargetCodeExecution`); on IL2CPP it reports `il2Cpp` true and offers less. See
  [Mono and .NET](mono-and-dotnet.md), [IL2CPP](unity-il2cpp.md).
- **Module, base, RVA**: a module is the exe or a DLL mapped into the process (`module_list`). Its base can change
  between runs or reboots (ASLR); an RVA is an offset from the base, as in `game.exe+1A2B30`. See
  [memory model](memory-model.md).
- **NOP**: the one-byte no-operation instruction `90`. NOP-ing overwrites every byte of an instruction with NOPs so it
  no longer runs (`asm_apply_code_patch`, same length in and out). See [NOP patch](../Workflows/nop-patch.md).
- **PDB**: a program database, the symbol file a compiler writes for an exe or DLL, with its function, global and type
  names. `module_get` names a module's PDB in `pe.pdb`, and
  `symbol_add_module(path="C:/Games/Game/game.pdb", baseAddress="game.exe", enumStructures=true)` loads one;
  `symbol_find` then searches its names and `structure_get_pdb_layout` reads its types. See
  [code analysis](code-analysis.md).
- **Pointer, chain, offset, level**: a pointer holds an address. A chain is a base plus offsets, each added to the
  value read at the step before; the level is the number of reads. The tools take and return offsets as signed hex
  strings such as "4C8" or "-8", in dereference order (`pointer_read_chain`, records); CE's pointer dialog and .CT
  files list them reversed. See [pointers](pointers.md).
- **Pointer map, pointer scan**: a map is a stored list of the target's pointer values (`pointer_create_map`); a scan
  searches it for chains from static roots to an address (`pointer_find_paths`), and a rescan keeps the chains that
  still reach it after a restart (`pointer_rescan_paths`). See [pointer scan](../Workflows/pointer-scan.md).
- **RIP-relative**: an x64 operand such as `[rip+disp32]`, relative to the next instruction. In a module's code it
  reaches a fixed module+offset address, which is how code finds globals. See [code analysis](code-analysis.md).
- **RTTI, vtable**: an MSVC C++ object with virtual methods starts with a vtable pointer; the slot just before the
  vtable leads to RTTI that names its class (`memory_get_address_info(addresses=[...], includeRtti=true)`). See
  [structures](structures.md).
- **Speedhack**: CE's hooks that scale the target's time (`speedhack_set_speed`, gate
  `Mcp:EnableTargetCodeExecution`): on a Unity game the engine's time scale, through the Mono collector; otherwise the
  Windows clocks (GetTickCount, timeGetTime, QueryPerformanceCounter). Speed 1 restores normal time, but the hooks stay
  until the target exits. See [speedhack](speedhack.md).
- **Static, dynamic address**: a static address lies in a module image (green in CE's scan results, written
  `module+offset`), so the same expression works after a restart; a dynamic one is heap or stack and moves. See
  [memory model](memory-model.md).
- **Symbol**: a name for an address: a module export, a debug symbol, or one registered by an AA script or by
  `symbol_register`. See [address expressions](address-expressions.md).
- **THREADSTACKn**: CE's symbol for a slot near the top of thread n's stack (Toolhelp order, 0 usually the main
  thread), as in `THREADSTACK0-2F8`. CE's own pointer scanner reports such roots, which hold only while the thread
  order and call chain repeat; this server's pointer tools never report them. See [memory model](memory-model.md).
- **Uses Mono**: a cheat-table option, saved with the table and set by default after a Mono attach, that makes CE
  inject its Mono collector by itself when it opens a process or loads a table that sets it. `process_attach`,
  `process_create`, `process_open_file` and `table_load` therefore need `Mcp:EnableTargetCodeExecution` when it could
  fire, and report it as `monoAutoAttach`. See [Mono and .NET](mono-and-dotnet.md).
- **Value type**: how bytes are read: 1 to 8 byte integers, float, double, text or raw bytes. Scan tools, memory tools
  and records name the types differently. See [value types](value-types.md).
- **VEH debugger**: CE's debugger interface that injects a helper DLL and catches exceptions with a vectored exception
  handler (`debugger_attach(interface="veh")`, gate `Mcp:EnableTargetCodeExecution`). A target that used it must
  restart before another attach. See [debugger](debugger.md).
- **WoW64**: how 64-bit Windows runs a 32-bit process: x86 code, 4-byte pointers and user addresses below 4 GB (2 GB
  unless large-address-aware); `module_get` reports `is64Bit` false. See [memory model](memory-model.md).

## Server terms

- **Activation**: one enable-to-disable period of the plugin in one CE. The `instanceId`, the job, resource and patch
  ids, and the named scanners, snapshots, symbols and pointer maps it created end with it; record ids are CE's own.
  Lua state it recorded in CE (breakpoints, a speed other than 1, a pause, a Lua job that had not ended) outlives it
  as an orphan. Unrelated to activating a record. See [errors and recovery](errors-and-recovery.md).
- **Client, Client lease**: the Client is the CheatEngine.Client library through which the plugin drives CE;
  `runtime_get_info` lists its capabilities. A Client lease is an owned resource held through it: a patch, allocation,
  scan or registered symbol. A plugin disable releases the leases. One whose release failed for good is no longer
  tracked and is reported with `requiresManualRecovery`. See [errors and recovery](errors-and-recovery.md).
- **Dispatch class**: the tool's `_meta` `cheatengine/dispatchClass`, how long it may hold CE's main thread: `short`,
  `host_scan` (grows with the target), `blocking_native` (may block CE for seconds) or `may_prompt` (may show a CE
  dialog that waits for the user). See [workflows](workflows.md).
- **Gate**: one of the settings `Mcp:EnableUnsafeLua`, `Mcp:EnableAutoAssembler`, `Mcp:EnableTargetCodeExecution` and
  `Mcp:EnableKernelAccess`, all on by default; `runtime_get_info.gates` reports them. A tool's `_meta`
  `cheatengine/requires` names its fixed gate; content decides others (AA records, tables, the debugger interface,
  Uses Mono). A call that needs a gate that is off returns `capability_disabled` with `not_started`; `asm_check`
  refuses such content as `unsupported` instead. A change applies after the plugin is disabled and enabled again.
  Switches, not a sandbox. See [configuration](configuration.md).
- **Gateway, backend**: each enabled plugin runs a backend on `127.0.0.1` inside its CE; the gateway is the stdio MCP
  server the AI client starts, which lists the instances and routes each call by `instanceId`. See
  [connection troubleshooting](connection-troubleshooting.md).
- **Host effect**: a failure's `hostEffect`: `not_started`, `not_applied`, `started`, `completed`,
  `cleanup_unconfirmed` or `unknown`. Only the first two mean nothing changed. After `completed` the change happened:
  verify, do not repeat. After the last three, inspect the state before any retry. `retryable` true means the
  identical call may be repeated once, later. See [errors and recovery](errors-and-recovery.md).
- **Instance**: one CE whose plugin is enabled and verified. `instance_list` gives its `instanceId`, which every other
  tool needs and which changes on each enable or CE restart. See [getting started](getting-started.md).
- **Job**: a background operation. A start tool returns a `jobId`; poll it as in
  `debugger_poll_capture(jobId="...", afterSequence=0)` and stop it with `runtime_stop_job`, which discards its items.
  It expires at its TTL, 120 s by default. A running job is an owned resource. See [workflows](workflows.md).
- **Live resource**: a read-only resource whose JSON is one tool's structured result, such as
  `cheatengine://instance/process` on a backend and `cheatengine://instances/{instanceId}/process` through the
  gateway. The gateway's resource list shows the fixed ones of each instance it verified in this session; those that
  take a parameter, such as a module or an address, stay templates. See [workflows](workflows.md).
- **Main, named scanner**: `main` drives CE's visible scanner with CE's own scan settings; poll it with
  `scan_get_status` and stop it with `scan_stop`. Any other `scannerName` is an independent scanner, an owned resource
  that blocks CE while it scans and lasts until `scan_delete`. A running main scan and every named scanner block a
  process switch. See [value scans](value-scans.md).
- **Orphan**: Lua state that an earlier activation left in CE: an MCP breakpoint, a speed other than 1, a pause or a
  Lua job that had not ended. `runtime_list_resources` shows it with `orphaned` true. It blocks `process_attach`,
  `process_create` and `process_open_file` (`busy`), but not `debugger_detach`. Release it only with the user's
  consent, through `runtime_release_resources(includeOrphans=true)`, or acknowledge it at the end of cleanup once the
  user undid it by hand. See [errors and recovery](errors-and-recovery.md).
- **Owned resource**: what this activation holds in CE or the target (`runtime_list_resources`): a patch, allocation,
  named scanner, registered symbol, breakpoint, speed other than 1, pause, Mono attachment or running job. Its
  `category` is `client_lease`, `lua_state` or `job`. Owned resources block a process switch and `debugger_detach`
  (`busy`) until their own release tool or `runtime_release_resources` undoes them. Snapshots, pointer maps and
  pointer scans are not owned resources. See [cleanup](../Workflows/cleanup-session.md).
- **Patch**: an `asm_apply` or `asm_apply_code_patch` change kept under a `patchId`; `asm_release_patch` runs its
  DISABLE section to restore the original bytes. See [Auto Assembler](auto-assembler.md).
- **Snapshot**: a named copy of up to 16 MiB of target memory held in this activation's own memory
  (`memory_create_snapshot`). It is not an owned resource: it survives a process switch and ends with the activation.
  `memory_compare_snapshot` compares it with another snapshot, or with live memory while the same target stays
  selected. See [compare snapshots](../Workflows/compare-snapshots.md).
- **Target change, selection epoch**: CE selecting another process, through `process_attach`, `process_create`,
  `process_open_file` or CE's own window. Each selection changes `process_get_current.selectionEpoch`. A call that
  spans several dispatches and sees it change fails with `target_changed`; addresses, captures and live comparisons
  of the old target no longer apply. MCP's own switch is refused (`busy`) while owned resources, orphans or a running
  main scan remain. `runtime_get_info.epoch` is another counter, which changes when CE's host runtime is replaced.
  See [errors and recovery](errors-and-recovery.md).
- **Unsafe Lua**: the caller's own Lua (`lua_execute`), which runs with CE's full rights on its main thread (gate
  `Mcp:EnableUnsafeLua`). See [Lua](lua.md).

## Sources

- CE wiki: [pointers](https://wiki.cheatengine.org/index.php?title=Tutorials:Pointers),
  [AOBs](https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs),
  [Auto Assembler](https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Auto_Assembler),
  [DBVM](https://wiki.cheatengine.org/index.php?title=DBVM).
- CE 7.7 sources ([github.com/cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine)): `MainUnit.lfm`
  and `formsettingsunit.lfm` (freeze timer, 100 ms), `frmautoinjectunit.pas` (`GetUniqueAOB` masking), `MainUnit.pas`
  (static results in green), `DBK32functions.pas` (driver blocklist message), `MemoryRecordUnit.pas` (`LastState` is
  saved, never read back), `symbolhandler.pas` (`THREADSTACK`); the installed `autorun/SpeedhackV3.lua` (Unity time
  scale or Windows clocks, never unhooked) and `autorun/monoscript.lua` (Uses Mono, collector launch).
- Microsoft Learn:
  [vectored exception handling](https://learn.microsoft.com/windows/win32/debug/vectored-exception-handling),
  [recommended driver block rules](https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules),
  [memory integrity](https://learn.microsoft.com/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity),
  [symbol files](https://learn.microsoft.com/windows/win32/debug/symbol-files),
  [running 32-bit applications](https://learn.microsoft.com/windows/win32/winprog64/running-32-bit-applications).
- Intel 64 and IA-32 Architectures Software Developer's Manual, Vol. 3, debug registers (data breakpoints are traps).
