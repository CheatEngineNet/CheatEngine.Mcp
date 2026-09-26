# Tool catalog

Tools use CheatEngine.Client high-level APIs where available. `Lua*Tool` containers cover additional documented CE APIs through fixed protected Client Lua operations. Capabilities depend on the loaded CE build and selected target. Use the running MCP schema for exact parameter types, bounds, and defaults.

Arbitrary `execute_lua` and Client Auto Assembler patches are opt-in. Other dedicated mutation tools remain powerful; those flags are not a sandbox. Errors after an operation starts may leave changes in the host. Inspect `hostEffect` and recovery information before retrying.

The Client owns independent named scan/allocation/symbol/patch leases. The reserved `main` scanner is borrowed from the visible CE tab; its results survive plugin disable. Omit `scannerName` to use main, or pass another name for an independent scan. Use `release_target_resources` before switching targets. CE-owned structures, breakpoints, comments, address-list changes, debugger state, and injected code can outlive the plugin; remove/stop them explicitly.

The gateway exposes **142 tools**: `list_instances` plus **141 CE tools**. Every CE tool requires the additional string `instanceId` from discovery. The tables below list tool names and purposes; use live schemas for all remaining parameters. Resource IDs and names are scoped to that instance and activation.

## Gateway

| Tool | Purpose |
| --- | --- |
| `list_instances` | Discover responsive local plugins by immutable instance ID, display name, CE process ID, and plugin version. No arguments. |

## AddressListTool

| Tool | Purpose |
| --- | --- |
| `add_memory_record` | Add a typed top-level memory record to the Cheat Engine address list. |
| `delete_memory_record` | Delete one memory record by Client-issued record ID. |
| `get_address_list` | Copy the current top-level Cheat Engine address list. |
| `set_memory_record_active` | Activate or deactivate one memory record by Client-issued record ID. |
| `update_memory_record` | Update one memory record by Client-issued record ID. |

## AdvancedMemoryTool

| Tool | Purpose |
| --- | --- |
| `allocate_memory` | Allocate target memory owned by a named Client lease. |
| `free_memory` | Release a named target-memory allocation lease. |

## AssemblyTool

| Tool | Purpose |
| --- | --- |
| `disassemble` | Disassemble one instruction or obtain its exact length at a target address. |
| `disassemble_range` | Disassemble a bounded sequence of target instructions using the Client's typed assembly API. |
| `resolve_address` | Resolve a target-process symbol expression, such as game.exe+10. |

## AutoAssemblyTool

| Tool | Purpose |
| --- | --- |
| `assemble` | Assemble one target instruction into bytes through the Client instruction capability. |
| `auto_assemble` | Apply an Auto Assembler patch and retain its Client lease, or release a previously returned patch ID. |
| `auto_assemble_check` | Check an Auto Assembler [ENABLE] section without applying a patch. |

## CheatTableTool

| Tool | Purpose |
| --- | --- |
| `load_cheat_table` | Load a trusted absolute Cheat Engine table path. The Client enforces configured table roots. |
| `save_cheat_table` | Save the current table to a trusted absolute Cheat Engine table path. |

## ConversionTool

| Tool | Purpose |
| --- | --- |
| `convert_string` | Convert text with a managed MD5 digest or explicit UTF-8 and ANSI byte round trips. |

## LuaCodeTool

| Tool | Purpose |
| --- | --- |
| `add_code_reference` | Add a persistent reference to Cheat Engine's code-dissection data. |
| `analyze_code_range` | Run Cheat Engine's code dissector on one bounded target-memory range. |
| `clear_code_analysis` | Clear Cheat Engine's current code-dissection data and references. |
| `delete_code_reference` | Remove one source-to-destination code reference. |
| `disassemble_bytes` | Disassemble a bounded hexadecimal byte sequence without reading target memory. |
| `get_code_references` | Read copied references to an address from Cheat Engine's current code dissector data. |
| `get_comment` | Read the persistent user-defined Memory View comment at a target address. |
| `get_function_range` | Return Cheat Engine's estimated function boundaries for a target address. |
| `get_previous_opcodes` | Return a bounded predecessor-instruction walk before one target address. |
| `get_referenced_functions` | List bounded function addresses referenced by the current code dissector data. |
| `get_referenced_strings` | Read a bounded copied list of strings referenced by current code dissector data. |
| `is_jump_destination` | Check whether an address is a jump destination within a bounded code range. |
| `set_comment` | Set a persistent user-defined Memory View comment at a target address. |

## LuaDbvmTool

| Tool | Purpose |
| --- | --- |
| `dbk_control_registers` | Read copied CR0, CR3, and CR4 values through DBK. |
| `dbk_physical_address` | Resolve a target virtual address to its physical address through DBK. |
| `dbk_status` | Report whether DBK is already initialized; this query does not initialize it. |
| `dbvm_cr4` | Read DBVM's real CR4 value. |
| `dbvm_initialize` | Initialize DBVM. Offloading the operating system is advanced and can destabilize the host. |
| `dbvm_read_physical` | Read a bounded physical-memory range through DBVM. |
| `dbvm_status` | Report whether DBVM is already initialized; this query does not initialize it. |
| `dbvm_watch` | Capture a DBVM watch for up to five seconds and disable it before returning up to 1024 copied events. |
| `dbvm_watch_log` | Retrieve a bounded copied DBVM watch log. |
| `dbvm_watch_stop` | Disable a DBVM watch by identifier. |
| `dbvm_write_physical` | Write a bounded hexadecimal byte sequence to physical memory through DBVM. |

## LuaDebuggerCaptureTool

| Tool | Purpose |
| --- | --- |
| `debugger_poll_capture` | Read buffered breakpoint hits in FIFO order without waiting; clear removes only the returned hits. |
| `debugger_start_capture` | Find instructions accessing or writing an address using a pollable breakpoint capture. Hits contain trap IP, thread ID and registers. Automatically expires; poll results within 30 seconds of expiry. |
| `debugger_stop_capture` | Remove the owned capture breakpoint, timer, and results. Cleanup failures retain the ID for retry. |

## LuaDebuggerTool

| Tool | Purpose |
| --- | --- |
| `debugger_add_breakpoint` | Create an execute, access, or write breakpoint. It persists until removed or debugger detachment. |
| `debugger_break_thread` | Request that Cheat Engine break a target thread; stopping can be asynchronous. |
| `debugger_breakpoints` | Return a bounded copied list of breakpoint addresses. |
| `debugger_context` | Read copied current registers; the debugger must be broken for meaningful values. |
| `debugger_continue` | Continue, step into, or step over from the current broken context. |
| `debugger_detach` | Unpause and detach the debugger while preserving the current target selection. |
| `debugger_ignore_thread` | Add or remove a target thread from Cheat Engine's breakpoint-ignore list. |
| `debugger_remove_breakpoint` | Remove the breakpoint containing the supplied address. |
| `debugger_set_register` | Set one general-purpose register in the currently broken debugger context, then write the context back before continuation. |
| `debugger_start` | Attach the selected debugger interface. A repeated request is idempotent; changing interfaces detaches once before attaching. |
| `debugger_status` | Read copied debugger state and active interface. |

## LuaDebuggerTraceTool

| Tool | Purpose |
| --- | --- |
| `debugger_poll_step_trace` | Copy collected trace contexts without waiting or consuming them. |
| `debugger_start_step_trace` | Break at an address and single-step one thread for a bounded number of contexts, then leave it stopped. Requires no existing breakpoints or global breakpoint hook. Results expire 30 seconds after completion or timeout. |
| `debugger_stop_step_trace` | Remove the owned trace breakpoint, hook, timer and results. Does not resume a stopped thread; cleanup failures retain the ID for retry. |

## LuaExecutionTool

| Tool | Purpose |
| --- | --- |
| `execute_lua` | Execute trusted Lua when the server explicitly enables unsafe Lua execution. Prefer typed MCP tools whenever possible. |

## LuaInjectionTool

| Tool | Purpose |
| --- | --- |
| `execute_local_code` | Execute a one-parameter stdcall function inside Cheat Engine and return its copied result. |
| `execute_remote_code` | Execute a one-parameter stdcall function in the selected target and return its copied result. |
| `generate_api_hook_script` | Generate a Cheat Engine Auto Assembler API-hook script without applying it. |
| `inject_dotnet_library` | Inject a managed assembly and invoke its static entry point in the selected target. |
| `inject_library` | Inject a native DLL or library into the selected target. Cheat Engine must report true for success. |

## LuaMemoryTool

| Tool | Purpose |
| --- | --- |
| `compare_memory` | Compare two bounded memory ranges through Cheat Engine Lua. |
| `copy_memory` | Copy a bounded memory range using Cheat Engine's target or local copy route. |
| `dump_memory` | Write a bounded target-memory range to an explicit file through a temporary Cheat Engine memory stream. |
| `full_access_memory` | Make a bounded target range writable and executable through Cheat Engine Lua. |
| `get_memory_protection` | Read the target memory protection flags reported by Cheat Engine Lua. |
| `hash_memory` | Compute Cheat Engine's MD5 hash for a bounded target memory range. |
| `set_memory_protection` | Set target range read, write, and execute protection through Cheat Engine Lua. |

## LuaProcessTool

| Tool | Purpose |
| --- | --- |
| `create_process` | Launch and open a process through Cheat Engine. This starts an external executable. |
| `get_opened_file_size` | Get the size of the currently opened file-as-process target. |
| `get_pointer_size` | Get Cheat Engine's configured target pointer size. |
| `get_process_state` | Get the currently opened target process ID, pause state, and age. |
| `get_speedhack_speed` | Get Cheat Engine's last configured speedhack speed. |
| `get_thread_list` | List target thread IDs reported by Cheat Engine, capped at 4096 entries. |
| `open_file_as_process` | Open a file through Cheat Engine's process-like memory interface. |
| `pause_process` | Pause the currently opened Cheat Engine target process. |
| `resume_process` | Resume the currently opened Cheat Engine target process. |
| `save_opened_file` | Save the open file-as-process target, optionally to an explicit filename. |
| `set_pointer_size` | Set Cheat Engine's configured target pointer size to 4 or 8 bytes. |
| `set_speedhack_speed` | Enable Cheat Engine speedhack and set a finite positive target speed. |

## LuaStructureTool

| Tool | Purpose |
| --- | --- |
| `add_structure_element` | Append one Structure Dissect element with validated primitive fields. |
| `autoguess_structure` | Ask Cheat Engine to infer Structure Dissect fields from a bounded target range. |
| `create_structure` | Create a persistent global Structure Dissect definition. Delete it explicitly with delete_structure. |
| `delete_structure` | Remove a persistent global Structure Dissect definition by name. |
| `fill_structure_from_dotnet` | Fill an existing Structure Dissect definition using the layout of a target .NET object. |
| `get_structure` | Get one global or internal Structure Dissect definition by its case-sensitive name. |
| `get_structure_element_value` | Read one Structure Dissect element value from a target base address. |
| `list_structures` | List up to 1024 global Cheat Engine Structure Dissect definitions. |
| `remove_structure_element` | Remove one Structure Dissect element by zero-based index. |
| `set_structure_element_value` | Write a value interpreted by a Structure Dissect element into the selected target. |
| `update_structure_element` | Change selected fields of one Structure Dissect element by zero-based index. |

## LuaSymbolsTool

| Tool | Purpose |
| --- | --- |
| `get_module_preference` | Get Cheat Engine's symbol lookup module-precedence list, capped at 1024 entries. |
| `get_structure_elements` | Get PDB-backed structure elements by name, capped at 4096 entries. |
| `get_symbols_loading_state` | Get whether Cheat Engine reports all symbols loaded. |
| `load_new_symbols` | Ask Cheat Engine to scan for loaded modules and add their symbols. |
| `lookup_rtti_class_name` | Resolve a likely RTTI class name for a target address, returning found=false when unavailable. |
| `reinitialize_dotnet_symbols` | Reinitialize the .NET symbol list for an optional module. |
| `reinitialize_symbols` | Reinitialize Cheat Engine's symbol handler. |
| `set_module_preference` | Put one extensionless module name first in Cheat Engine symbol lookup precedence. |
| `wait_for_symbols` | Wait for one Cheat Engine symbol-loading stage. |

## LuaTableTool

| Tool | Purpose |
| --- | --- |
| `find_memory_records_by_description` | Find up to 1024 current address-list records matching an exact description. |
| `get_memory_record_details` | Read pointer offsets and script text for one current Cheat Engine memory-record ID. |
| `get_selected_memory_record` | Read the currently selected Cheat Engine address-list record with pointer-offset details. |
| `select_memory_record` | Select one current Cheat Engine address-list record by ID. |
| `set_memory_record_offsets` | Replace a memory record's pointer offsets. Offsets are Cheat Engine internal order, index zero nearest the final value. |
| `set_memory_record_script` | Set a bounded Auto Assembler script on a memory record. The record persists it in the cheat table. |

## MemoryTool

| Tool | Purpose |
| --- | --- |
| `read_memory` | Read typed target memory through CheatEngine.Client. |
| `write_memory` | Write typed target memory through CheatEngine.Client. |

## MemoryViewTool

| Tool | Purpose |
| --- | --- |
| `enum_memory_regions` | Copy a bounded target memory-region map. |
| `get_memory_region` | Get copied metadata for the region containing an address. |

## PointerScanTool

| Tool | Purpose |
| --- | --- |
| `delete_pointer_map` | Delete an MCP pointer snapshot. Existing copied scan paths remain usable. |
| `generate_pointer_map` | Capture a bounded pointer snapshot with Client memory APIs. Maps live until deleted or plugin disable; they are not CE .scandata files. Inspect incomplete before relying on absence. |
| `get_pointer_scan_results` | Page stored pointer chains. Offsets are in dereference order; CE's address-list offset order is reversed. |
| `list_pointer_maps` | List bounded MCP-owned snapshots and their capture completeness. |
| `pointer_scan` | Find pointer chains in a captured map using nonnegative offsets. Results and traversal are bounded; module-relative roots support later rebasing. |
| `rescan_pointer_scan` | Filter existing paths against a new map or live Client pointer-chain reads. Module roots rebase; absolute roots remain absolute. Unresolved paths are retained and counted; interrupted scans keep original results. |
| `reset_pointer_scan` | Release an MCP-owned pointer result list. |

## PointerTool

| Tool | Purpose |
| --- | --- |
| `read_pointer_chain` | Resolve a bounded pointer chain using the target pointer width. |

## ProcessTool

| Tool | Purpose |
| --- | --- |
| `get_current_process` | Get the process currently selected in Cheat Engine. |
| `get_plugin_version` | Get the loaded plugin version and assembly path, plus the extracted runtime location for deployment diagnostics. |
| `get_process_list` | List a bounded set of local processes. |
| `open_process` | Attach Cheat Engine to a process ID or exact process name. |

## RuntimeTool

| Tool | Purpose |
| --- | --- |
| `get_runtime_info` | Read the active Client epoch, host version and capability evidence before using optional features. |

## ScanTool

| Tool | Purpose |
| --- | --- |
| `aob_scan` | Run a bounded AOB scan using CheatEngine.Client. |
| `aob_scan_unique` | Find at most one AOB match; truncated results are not proof of uniqueness. |
| `get_memory_scan_results` | Read a bounded page from main's visible CE found list (including manual scans), or an independent Client session. |
| `get_memory_scan_status` | Poll main's UI state or inspect a named independent session. |
| `list_memory_scanners` | List main (the visible CE scan tab) and up to 32 independent Client sessions. |
| `memory_scan` | Start main's visible UI scan by default, or an independent Client scan by name. Reset explicitly before another first scan. |
| `next_memory_scan` | Narrow main's visible scan or an independent named scan with the same comparison API. |
| `reset_memory_scan` | Clear main through CE's New Scan action, or release one independent named session. Refuses a running main scan. |

## SymbolRegistryTool

| Tool | Purpose |
| --- | --- |
| `enum_registered_symbols` | List symbols currently owned by this MCP server. |
| `register_symbol` | Register a Client-owned target symbol. The server releases it on disable. |
| `unregister_symbol` | Release a symbol that this MCP server registered in the current activation. |

## SymbolTool

| Tool | Purpose |
| --- | --- |
| `enum_module_sections` | Copy a bounded section list for one target module through CheatEngine.Client. |
| `enum_modules` | Copy a bounded module list from the selected target. |
| `get_name_from_address` | Resolve the best Cheat Engine name for an address. |
| `get_symbol_info` | Get copied metadata for one Cheat Engine symbol expression. |

## TargetResourceTool

| Tool | Purpose |
| --- | --- |
| `release_target_resources` | Release owned scans, symbols, allocations and patches in reverse creation order before switching processes. Stops at the first incomplete release. |
