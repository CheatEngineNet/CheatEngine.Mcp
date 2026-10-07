# Tool map

Every served tool by domain, with its gate and purpose. Pick a tool here, then read its schema in `tools/list`, the
authority for parameters, bounds, defaults and results. Task guides: [workflows](workflows.md); first calls:
[getting started](getting-started.md).

## Reading the map

- Through the gateway every tool except `instance_list` requires an `instanceId` that `instance_list` returns (no
  default; examples omit it); a direct backend connection has neither.
- A failure sets `isError` and carries `error` with `kind`, `message`, `hostEffect`, `retryable` and maybe `hint`.
  Repeat a mutation only after `hostEffect` `not_started` or `not_applied`; otherwise inspect first
  ([errors and recovery](errors-and-recovery.md)).
- A `*_start_*` tool returns a `jobId` for its domain's `*_poll_*` tool, which pages from `afterSequence` 0, then
  `nextAfterSequence`; pointer jobs are read with `pointer_list_maps` and `pointer_list_scans`, `debugger_run_to` with
  `runtime_list_jobs`. `runtime_stop_job` stops any job, and every job ends at its TTL (at most 300 s).
- Allocations, patches, named scanners, registered symbols, MCP breakpoints, a speed other than 1, MCP's pause and Mono
  attachment, and jobs are resources: list them with `runtime_list_resources` and release them with
  `runtime_release_resources` before switching processes ([cleanup](../Workflows/cleanup-session.md)). Snapshots,
  pointer maps and pointer scans are MCP memory, not resources.
- **Gate** (all on by default, shown by `runtime_get_info`, not a sandbox;
  [configuration](configuration.md#capability-gates)): **UL** `Mcp:EnableUnsafeLua`, **AA** `Mcp:EnableAutoAssembler`,
  **TCE** `Mcp:EnableTargetCodeExecution`, **KA** `Mcp:EnableKernelAccess`. A plain gate is in `_meta`
  `cheatengine/requires` and covers every call; **→** marks one the tool checks when the arguments, loaded content or
  Cheat Engine (CE) state need it. An off gate refuses with `capability_disabled` before anything runs.
- A purpose ending in `host_scan` (a scan blocking CE while it runs), `blocking_native` (a native call that can block
  it for seconds) or `may_prompt` (may show a CE dialog) gives `_meta` `cheatengine/dispatchClass`; other CE tools are
  `short`.
- Use the tools only on single-player or offline software the user may modify, never on online, competitive or
  anti-cheat-protected games; get consent before any write, patch, script, debugger attach, speedhack, Mono attach,
  code execution or kernel feature ([safety](safety.md)).

## gateway

| Tool | Gate | Purpose |
|---|---|---|
| `instance_list` | | List the verified local CE instances and their `instanceId`; call it first. |

## runtime

| Tool | Gate | Purpose |
|---|---|---|
| `runtime_get_info` | | Host and plugin versions, platform, Client epoch, capability evidence and the four gates. |
| `runtime_get_overview` | | Orientation: host information, gates, selected process, retained resource and job counts. |
| `runtime_list_resources` | | List resources this or an earlier activation holds, with kind, state and orphan flag. |
| `runtime_release_resources` | | Release every resource newest first, optionally orphans; acknowledge manually recovered ones. |
| `runtime_list_jobs` | | List running and retained jobs with state, progress, buffer counts and expiry. |
| `runtime_stop_job` | | Stop any job and discard its buffered items. |

## process

Guide: [attach and orient](../Workflows/attach-and-orient.md). **→TCE**: with the table option Uses Mono set (and
IgnoreUsesMono off), opening a process makes CE inject its Mono data collector; `process_attach.monoAutoAttach` says so.

| Tool | Gate | Purpose |
|---|---|---|
| `process_list` | | List up to 4096 local processes, filtered by name. |
| `process_attach` | →TCE | Select a process by id or exact name; refused while retained state or a scan blocks it. |
| `process_get_current` | | The selected process: open state, id, name, pointer size, selection epoch. |
| `process_create` | →TCE | Launch a validated, pinned executable through CE and select it. |
| `process_open_file` | →TCE | Open a validated, pinned host file as the target (file-as-process). |
| `process_save_file` | | Save the opened file target to a new path under `Mcp:Files:AllowedRoots`, atomically. |
| `process_set_paused` | | Pause or resume the target; MCP's own pause is a tracked resource. |
| `process_list_threads` | | List up to 4096 target thread ids. |
| `process_set_pointer_size` | | Set the target pointer size to 4 or 8 bytes and verify it. |

## memory

Guide: [memory model](memory-model.md), [value types](value-types.md), [emulators](emulators.md),
[compare snapshots](../Workflows/compare-snapshots.md). `memory_read.byteOrder` (also on the writes, batches and
samples) handles big-endian numbers.

| Tool | Gate | Purpose |
|---|---|---|
| `memory_read` | | Read a typed value, up to 1024 consecutive values, a string or raw bytes. |
| `memory_read_batch` | | Read up to 1024 addresses with their own types; per-item errors stay in band. |
| `memory_read_samples` | | Sample up to 64 addresses for at most 10 s and keep each value's changes. |
| `memory_write` | | Write a typed value, string or bytes; returns up to 64 previous bytes and reads back by default. |
| `memory_write_batch` | | Write up to 1024 typed items in order; not atomic, a failure reports what was written. |
| `memory_get_address_info` | | Describe up to 256 addresses: symbol, module, section, region, pointer value, RTTI class. |
| `memory_list_regions` | | Page regions filtered by range, module, state, backing and protection. |
| `memory_set_protection` | | Change the protection of up to 16 MiB in one region; returns the previous one. |
| `memory_allocate` | | Allocate named read/write or executable memory owned by this activation. |
| `memory_free` | | Free a named allocation. |
| `memory_copy` | | Copy up to 1 MiB inside the target; overlap is safe. |
| `memory_compare` | | Compare two ranges of up to 16 MiB and list the differing runs. |
| `memory_hash` | | Hash up to 64 MiB with MD5, SHA-1 or SHA-256. |
| `memory_create_snapshot` | | Copy up to 16 MiB into a named snapshot in MCP memory. |
| `memory_compare_snapshot` | | Compare a snapshot with live memory or another, per slot: changed, unchanged, up or down. |
| `memory_list_snapshots` | | List the snapshots and the bytes they hold. |
| `memory_delete_snapshot` | | Delete a snapshot. |
| `memory_dump_to_file` | | Write up to 256 MiB to a file under `Mcp:Files:AllowedRoots` (empty by default: refused). |
| `memory_load_from_file` | | Write a pinned local file of up to 256 MiB into target memory. |

## scan

Guide: [value scans](value-scans.md), [troubleshooting scans](troubleshooting-scans.md). `main` is CE's visible
scanner: it returns at once and uses CE's own scan settings. Any other name is an independent scanner that scans
synchronously, can include mapped memory and is a resource until deleted.

| Tool | Gate | Purpose |
|---|---|---|
| `scan_first` | | Start a first value scan. `host_scan` |
| `scan_next` | | Narrow the results against the previous scan. `host_scan` |
| `scan_get_status` | | Read a scanner's state and result count, and for `main` CE's scan settings. |
| `scan_list_results` | | Page results: addresses and CE's display values. |
| `scan_list_scanners` | | List `main` and up to 32 named scanners. |
| `scan_reset` | | Clear the results for a new first scan; a named scanner stays. |
| `scan_delete` | | Release and delete a named scanner. |
| `scan_stop` | | Stop `main`'s scan and untick Repeat, keeping partial results; a named scanner is deleted. |

## aob

Guide: [AOB signatures](aob-signatures.md).

| Tool | Gate | Purpose |
|---|---|---|
| `aob_find` | | Find up to 8 byte patterns with ?? wildcards by module, range and protection, optionally in mapped memory. `host_scan` |
| `aob_find_value` | | Find the little-endian bytes of one number, pointer or string, scoped likewise. `host_scan` |
| `aob_generate_signature` | | Generate and verify a unique signature for a module address; MCP's own generator above 64 MiB. `host_scan` |

## pointer

Guide: [pointers](pointers.md). Offsets are hex strings in dereference order, the reverse of CE's address list.

| Tool | Gate | Purpose |
|---|---|---|
| `pointer_read_chain` | | Follow a pointer chain hop by hop and read the final value. |
| `pointer_read_chains` | | Validate up to 128 supplied chains with independent resolution, comparison, and value outcomes. |
| `pointer_get_access_info` | | Analyze supplied instruction/register facts without live memory or symbol lookup. |
| `pointer_find_references` | | Find the addresses pointing at or just below a target, live or in a map. `host_scan` |
| `pointer_create_map` | | Start a job that captures a pointer map; poll `pointer_list_maps`. |
| `pointer_list_maps` | | List pointer maps and their capture state. |
| `pointer_delete_map` | | Delete a pointer map, stopping its capture. |
| `pointer_find_paths` | | Start a job that finds paths from static roots to a target; poll `pointer_list_scans`. |
| `pointer_list_scans` | | List pointer-path scans and their search state. |
| `pointer_list_paths` | | Page a scan's paths, each with an address expression. |
| `pointer_rescan_paths` | | Keep only the paths that still reach a target, as after a restart. |
| `pointer_delete_scan` | | Delete a pointer-path scan, stopping its search. |
| `pointer_save_map` | | Export a usable map as a native CE version-1 `.scandata` file. |
| `pointer_load_map` | | Import a native CE version-1 `.scandata` map; capture completeness is unknown. |
| `pointer_save_scan` | | Save result paths in MCP pointer-scan JSON version 2. |
| `pointer_load_scan` | | Load MCP pointer-scan JSON version 2 or legacy version 1; rescan unresolved paths. |

## module

Guide: [repair after update](../Workflows/repair-after-update.md).

| Tool | Gate | Purpose |
|---|---|---|
| `module_list` | | Page the loaded modules, filtered by name. |
| `module_get` | | One module: base, size, path, sections and PE fields such as the build time stamp. |
| `module_list_exports` | | Page a module's exports. |
| `module_list_imports` | | Page a module's imports: DLL, IAT slot, name, and where the slot points now. |
| `module_find_patches` | | Compare a module's code in memory with its file and list differing ranges. |

## symbol

Guide: [address expressions](address-expressions.md).

| Tool | Gate | Purpose |
|---|---|---|
| `symbol_resolve` | | Resolve up to 256 expressions to addresses and names. |
| `symbol_find` | | Find symbols whose names contain a text, optionally in one module. `host_scan` |
| `symbol_register` | | Register a named symbol owned by this activation. |
| `symbol_unregister` | | Unregister a symbol this activation registered. |
| `symbol_list_registered` | | Page registered symbols, marking the ones MCP owns. |
| `symbol_get_module_preference` | | Read which module wins when several define a name. |
| `symbol_set_module_preference` | | Change the module precedence of later lookups. |
| `symbol_reload` | | Start a symbol reload and return at once. |
| `symbol_add_module` | | Load a file's symbols, such as a game PDB, at a base address. `blocking_native` |
| `symbol_enable_sources` | →KA | Enable Windows PDB symbols (may download) or kernel symbols (needs KA). `blocking_native` |

## speedhack

Guide: [speedhack](speedhack.md).

| Tool | Gate | Purpose |
|---|---|---|
| `speedhack_get_state` | | Read the configured speed and whether the hook symbol exists; neither proves working hooks. |
| `speedhack_set_speed` | TCE | Set the speed, 0.01 to 1000; a speed other than 1 hooks while that symbol is absent, 1 never. `may_prompt` |

## util

| Tool | Gate | Purpose |
|---|---|---|
| `util_convert_value` | | Convert a value between value types without reading the target; never decodes big-endian input. |
| `util_calculate` | | Evaluate an integer expression with 64-bit wraparound. |

## code

Guide: [code analysis](code-analysis.md). Operands print as absolute hex, without module names.

| Tool | Gate | Purpose |
|---|---|---|
| `code_disassemble` | | Disassemble 1 to 1024 instructions, optionally with estimated predecessors. |
| `code_decode` | | One instruction: CE's text columns, exact bytes and length. |
| `code_disassemble_bytes` | | Disassemble hexadecimal bytes without reading the target. |
| `code_get_function` | | Estimate the function bounds around an address. |
| `code_get_function_graph` | | Build a bounded control-flow graph: blocks, successors, call sites. |
| `code_start_dissect` | | Start a code-dissector job over up to 1 MiB. `blocking_native` |
| `code_start_search` | | Start a job searching up to 1 MiB of decoded instruction text for a substring. |
| `code_poll_job` | | Poll a dissect or search job. |
| `code_find_references` | | Page the dissector's calls, jumps and loads that reference an address; it records no stores. |
| `code_find_strings` | | Page the strings the dissector found. |
| `code_list_functions` | | Page the functions the dissector found. |
| `code_clear_dissect` | | Clear the dissector's data; refused while a dissect job runs. |
| `code_get_comments` | | Read Memory View comments at up to 256 addresses. |
| `code_set_comment` | | Set the Memory View comment at one address. |

## asm

Guide: [auto assembler](auto-assembler.md), [x64 injection](x64-injection.md). **→UL/TCE/KA**: `{$lua}` and luacall
need UL; `{$luacode}` UL and TCE; loadlibrary, createthread(andwait), `{$c}` and `{$ccode}` TCE; kalloc KA; include,
loadbinary and any command or directive CE 7.7 does not build in need UL and TCE. `asm_check` refuses such a script,
globalalloc or a `$` before non-hex text (`unsupported`): CE runs parts of a checked script.

| Tool | Gate | Purpose |
|---|---|---|
| `asm_assemble` | | Assemble up to 128 instructions without writing them. |
| `asm_check` | AA | Check a script's ENABLE and DISABLE sections without applying it. |
| `asm_apply` | AA →UL/TCE/KA | Apply a script and keep its patch for release. `may_prompt` |
| `asm_apply_code_patch` | AA | Replace 1 to 64 verified bytes as a reversible patch. `may_prompt` |
| `asm_release_patch` | | Release a patch by running its DISABLE section. |
| `asm_list_patches` | | List the patches this activation holds. |
| `asm_generate_injection` | | Generate an AOB injection scaffold from a verified signature. |
| `asm_generate_api_hook` | | Ask CE for an API hook script with ENABLE and DISABLE sections, without applying it. |

## record

Guide: [cheat tables](cheat-tables.md). **→AA**: creating an Auto Assembler record or passing a script, activating or
deactivating one (or a group passing it on), deleting or clearing an active one, and every `record_set_script`.
Offsets are written as for pointers, and reads return them.

| Tool | Gate | Purpose |
|---|---|---|
| `record_list` | | Page the whole address list, nested records included, or one parent's children. |
| `record_get` | | Read 1 to 256 records by id, optionally with their dropdown lists. |
| `record_find` | | Find records at any depth by description, address, type or state. |
| `record_get_selected` | | Read the selected record. |
| `record_select` | | Select a record, visibly to the user. |
| `record_create` | →AA | Create 1 to 256 value, group or script records; rolled back on failure. |
| `record_update` | | Update 1 to 256 records: description, address, type, length, Unicode, offsets, value. |
| `record_set_active` | →AA | Activate (freeze) or deactivate 1 to 256 records; a refused one reports CE's reason. |
| `record_delete` | →AA | Delete 1 to 256 records. |
| `record_move` | | Move one record under a parent or to the root. |
| `record_group` | | Group 1 to 256 records under a new type-14 record, not CE's group header. |
| `record_set_script` | →AA | Replace an inactive script record's text without activating it. |
| `record_set_dropdown` | | Replace or clear one record's dropdown list and its three dropdown options. |
| `record_clear` | →AA | Delete every record and return the count. |

## table

Guide: [cheat tables](cheat-tables.md). **→UL/AA/TCE/KA**: Lua scripts and forms need UL, AA scripts AA plus what
their commands need; Uses Mono in the loaded or current table needs TCE while a process is open; a .CETRAINER,
protected, binary or unparseable table needs UL, AA and TCE.

| Tool | Gate | Purpose |
|---|---|---|
| `table_list_files` | | List table files under `CheatEngineClient:AllowedTableRoots`. |
| `table_load` | →UL/AA/TCE/KA | Inspect a table, enforce its gates, then load or merge it. `may_prompt` |
| `table_save` | | Save the current table under `CheatEngineClient:AllowedTableRoots`. `may_prompt` |

## structure

Guide: [structures](structures.md).

| Tool | Gate | Purpose |
|---|---|---|
| `structure_list` | | Page Structure Dissect definitions. |
| `structure_get` | | Read a structure and a page of its elements. |
| `structure_create` | | Create a structure from elements, a copy or a PDB type. |
| `structure_set_name` | | Rename a structure in place; pointers to it stay. |
| `structure_delete` | | Delete a structure. |
| `structure_add_elements` | | Add elements, all or nothing. |
| `structure_update_elements` | | Rename, retype or move elements. |
| `structure_remove_elements` | | Remove elements by index. |
| `structure_autoguess` | | Let CE guess element types from memory at address plus offset. |
| `structure_fill_from_dotnet` | | Add a .NET object's fields from the data collector. `blocking_native` |
| `structure_get_pdb_layout` | | Read a type's fields from loaded PDB symbols. |
| `structure_read` | | Read element values at up to 16 addresses side by side. |
| `structure_write_element` | | Write one element's value and read it back. |
| `structure_compare` | | Find the fields that tell two groups of instances apart. |
| `structure_generate_c_header` | | Write C declarations for a structure and its children. |

## debugger

Guide: [debugger](debugger.md), [find a writer](../Workflows/find-writer.md). `debugger_attach` needs TCE for veh, KA
for kernel, nothing for windows; default reads CE's debugger setting and is gated alike. DBVM, an unidentified setting
or a ceserver connection is refused (`unsupported`).

| Tool | Gate | Purpose |
|---|---|---|
| `debugger_attach` | →TCE/KA | Attach the debugger; check the interface actually used. `may_prompt` |
| `debugger_detach` | | Continue, unpause, detach; refused while any MCP resource holds target state. |
| `debugger_get_status` | | Read the debugger state. |
| `debugger_break_thread` | | Ask a thread to break. |
| `debugger_set_breakpoint` | | Set a tracked execute, access or write breakpoint. |
| `debugger_delete_breakpoint` | | Delete a breakpoint this activation set. |
| `debugger_list_breakpoints` | | List up to 1024 breakpoints, marking MCP's. |
| `debugger_continue` | | Continue the stopped context. |
| `debugger_step` | | Step into or over one instruction. |
| `debugger_get_context` | | Read the stopped thread's registers, optionally FPU and XMM bytes. |
| `debugger_set_register` | | Write a register or EFLAGS and read it back. |
| `debugger_set_thread_ignored` | | Add a thread to the no-break list or remove it. |
| `debugger_get_stack_trace` | | Scan up to 128 stack slots for return addresses. `host_scan` |
| `debugger_start_capture` | | Start a job recording what accesses, writes or executes an address, or what one instruction accesses. |
| `debugger_poll_capture` | | Poll a capture job. |
| `debugger_start_trace` | | Start a job stepping the hitting thread up to 256 steps; one at a time. |
| `debugger_poll_trace` | | Poll a trace job. |
| `debugger_run_to` | | Continue to an address through a one-shot breakpoint job. |

## exec

Guide: [calling a game function once](x64-injection.md#calling-a-game-function-once).

| Tool | Gate | Purpose |
|---|---|---|
| `exec_inject_library` | TCE | Inject a native DLL; MCP cannot undo it. `blocking_native` |
| `exec_inject_dotnet` | TCE | Load a .NET assembly and call its static int Method(string). `blocking_native` |
| `exec_call_remote` | TCE | Call a target function on a new thread; waits at most 10 s. `blocking_native` |
| `exec_call_method` | TCE | Call a target instance method on a new thread; waits at most 10 s. `blocking_native` |
| `exec_call_local` | TCE | Call a function in CE's own process; no timeout. `blocking_native` |
| `exec_compile_c` | TCE →KA | Compile C source into new memory or at an address you own. `blocking_native` |
| `exec_compile_csharp` | TCE | Compile C# through CE and export to a required approved-root assembly path; does not inject. `blocking_native` |

- `exec_call_remote` and `exec_call_method` take up to 16 typed arguments, each value a JSON string:
  `exec_call_remote(functionAddress="<address>", arguments=[{type="integer", value="10"}])`. A buffer is
  `memory_allocate` memory kept until the call has ended. Both refuse a paused or debugger-stopped target.
- `exec_call_method.classRegister` (default 1, ECX/RCX) carries `this`; on x64, 1 is the member call with arguments
  from RDX/XMM1.
- `exec_inject_dotnet` refuses quotes, braces, control characters, long strings and an active Mono collector. CE waits
  without limit: only the MCP call timeout (45 s in the gateway) ends the request, and CE stays blocked.
- `exec_compile_c(kernelMode=true)` allocates kernel memory, needs KA and is refused with an address.

## dotnet

Guide: [Mono and .NET](mono-and-dotnet.md). They use CE's out-of-process .NET data collector and never inject.

| Tool | Gate | Purpose |
|---|---|---|
| `dotnet_get_status` | | Report whether the .NET collector is available and attached. |
| `dotnet_list_domains` | | Page application domains. |
| `dotnet_list_modules` | | Page a domain's modules. |
| `dotnet_list_types` | | Page a module's types, filtered by name. `host_scan` |
| `dotnet_get_type` | | Read a type's layout, base type and fields. |
| `dotnet_list_methods` | | Page a type's methods, filtered by name. |
| `dotnet_get_method_parameters` | | Read a method's parameter names, type codes and signature. |
| `dotnet_get_object` | | Inspect the object at an address with its field values. |
| `dotnet_start_instance_search` | | Start a job that finds instances of a type. `host_scan` |
| `dotnet_poll_instance_search` | | Poll an instance search. |

## mono

Guide: [Mono and .NET](mono-and-dotnet.md), [Unity IL2CPP](unity-il2cpp.md).

| Tool | Gate | Purpose |
|---|---|---|
| `mono_attach` | TCE | Inject CE's Mono data collector; by default it sets Uses Mono. `may_prompt` |
| `mono_detach` | | Close only the collector attachment MCP made; the DLL, its patch and Uses Mono stay. |
| `mono_get_status` | | Report whether a collector is attached, IL2CPP mode and domains. |
| `mono_list_assemblies` | | Page assemblies and images. |
| `mono_list_classes` | | Page an image's classes, filtered by name. `host_scan` |
| `mono_find_class` | | Find a class by namespace and name. `host_scan` |
| `mono_list_fields` | | List a class's fields and offsets, filtered by name. |
| `mono_list_methods` | | Page a class's methods, filtered by name. |
| `mono_find_method` | | Find a method by name in a class. |
| `mono_get_static_field_address` | | Read a class's static field-data address. |
| `mono_compile_method` | TCE | JIT-compile a method and return its address. `blocking_native` |
| `mono_invoke_method` | TCE | Invoke a method once in the target. `blocking_native` |
| `mono_get_object` | | Find the object containing an address: its start, class and field values. `host_scan` |
| `mono_start_instance_search` | | Start a job that finds candidate instances by vtable. `host_scan` |
| `mono_poll_instance_search` | | Poll an instance search. |

## kernel

Guide: [kernel](kernel.md). Kernel features can freeze or crash the host; use them only with explicit consent.

| Tool | Gate | Purpose |
|---|---|---|
| `kernel_get_status` | KA | Read the DBK and DBVM state and control registers; loads nothing. |
| `kernel_initialize_dbvm` | KA | Confirm DBVM runs; `kernel_initialize_dbvm.offloadOperatingSystem` loads DBK, then DBVM once a human agrees. `may_prompt` |
| `kernel_translate_address` | KA | Translate a virtual address to its physical address. |
| `kernel_read_physical` | KA | Read 1 to 4096 bytes of physical memory. |
| `kernel_write_physical` | KA | Write 1 to 4096 bytes of physical memory, bypassing protection. |
| `kernel_start_watch` | KA | Start a DBVM watch job inside one 4 KiB physical page. `blocking_native` |
| `kernel_poll_watch` | KA | Collect new watch events and return a page. `blocking_native` |

## lua

Guide: [Lua](lua.md), [Lua API](lua-api.md).

| Tool | Gate | Purpose |
|---|---|---|
| `lua_execute` | UL | Run caller-authored Lua on CE's main thread with its full API. `may_prompt` |
| `lua_find_api` | | Search the installed celua.txt Lua API reference. |

## Sources

- The served tool schemas (`tools/list`) and their `_meta` keys.
- CE source (`autoassembler.pas`, `LuaHandler.pas`): https://github.com/cheat-engine/cheat-engine
- CE 7.7 `autorun/monoscript.lua` (Uses Mono, collector attachment).
