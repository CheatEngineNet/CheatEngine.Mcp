# Tool catalog

The v2 contract (2.0.0) freezes **170 tool names**: `instance_list` on the gateway and **169 Cheat Engine tools** in 21
domains. The names below are final; each domain's tools land as its implementation is completed. Until then the
gateway still lists the legacy tools at the end of this page, and the running MCP schema stays the authority for what an
instance serves and for every parameter, bound and default.

Every Cheat Engine tool takes the string `instanceId` returned by `instance_list`; there is no default instance. A v2
tool returns one JSON object (`structuredContent`, repeated as text). A failure sets `isError` and carries
`error.kind`, `hostEffect` and `hint`; never repeat a mutation whose `hostEffect` is `started` or `unknown`. Tools named
`*_start_*` return a `jobId`: poll it with the matching `*_poll_*` tool and `afterSequence`, stop it with
`runtime_stop_job`. Release owned resources with `runtime_release_resources` before switching processes.

**Gate** names the setting a tool needs, all enabled by default and none of them a sandbox: **UL**
`Mcp:EnableUnsafeLua`, **AA** `Mcp:EnableAutoAssembler`, **TCE** `Mcp:EnableTargetCodeExecution`, **KA**
`Mcp:EnableKernelAccess`. A disabled gate refuses the call with `capability_disabled` before anything runs. **→**
marks a gate that applies only to some arguments or content, such as a debugger interface, an Auto Assembler record or
a cheat table that would load Lua or attach Mono.

## gateway (1)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `instance_list` |  | Discover the verified local instances; call it first. |

## runtime (6)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `runtime_get_info` |  | Plugin, Cheat Engine and Client versions, enabled settings, dispatch statistics and limits. |
| `runtime_get_overview` |  | Orientation: attached process, debugger, scanners, owned resources, speedhack, symbol loading and running jobs. |
| `runtime_list_resources` |  | Page the resources this activation owns, including orphaned MCP state. |
| `runtime_release_resources` |  | Release owned resources newest first; stops at the first incomplete release. |
| `runtime_list_jobs` |  | Page running and retained jobs with their state and expiry. |
| `runtime_stop_job` |  | Stop any job and drop its buffered results; repeating the stop is harmless. |

## process (9)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `process_list` |  | Page local processes, optionally with window titles. |
| `process_attach` | →TCE | Attach Cheat Engine to a process by ID or exact name. |
| `process_get_current` |  | Describe the attached process: identity, architecture, pointer size and pause state. |
| `process_create` | TCE | Launch an executable and attach to it. |
| `process_open_file` | →TCE | Open a file through Cheat Engine's file-as-process interface. |
| `process_save_file` |  | Save the opened file-as-process target under the allowed write roots. |
| `process_set_paused` |  | Pause or resume the attached process. |
| `process_list_threads` |  | Page the target's thread IDs. |
| `process_set_pointer_size` |  | Set Cheat Engine's target pointer size to 4 or 8 bytes. |

## memory (13)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `memory_read` |  | Read a typed value, an array, bytes or a string at one address. |
| `memory_read_batch` |  | Read up to 1024 typed addresses in one call; per-item errors stay in band. |
| `memory_write` |  | Write a typed value, bytes or a string, with read-back verification. |
| `memory_write_batch` |  | Write up to 1024 typed values; a failure reports the completed prefix. |
| `memory_get_address_info` |  | Describe up to 256 addresses: symbol, module, section, region and optional RTTI class. |
| `memory_list_regions` |  | Page the target's memory regions with address, module and protection filters. |
| `memory_set_protection` |  | Change the read, write and execute protection of a range. |
| `memory_allocate` |  | Allocate named target memory owned by this activation. |
| `memory_free` |  | Free a named allocation. |
| `memory_copy` |  | Copy a bounded range inside the target. |
| `memory_compare` |  | Compare two target ranges and list the differences. |
| `memory_hash` |  | Hash a target range with MD5, SHA-1 or SHA-256. |
| `memory_dump_to_file` |  | Dump a target range to a file under the allowed write roots. |

## scan (8)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `scan_first` |  | Start a first value scan on the visible main scanner or a named independent one. |
| `scan_next` |  | Narrow a scanner's results with a next scan. |
| `scan_get_status` |  | Read a scanner's state, progress and result count. |
| `scan_list_results` |  | Page a scanner's results. |
| `scan_list_scanners` |  | List the main scanner and the independent scanners. |
| `scan_reset` |  | Reset a scanner for a new first scan. |
| `scan_delete` |  | Release a named independent scanner. |
| `scan_stop` |  | Stop a running main scan. |

## aob (2)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `aob_find` |  | Find up to 8 byte patterns with wildcards, filtered by module, range, protection and alignment. |
| `aob_generate_signature` |  | Generate and verify a unique AOB signature for an address. |

## pointer (10)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `pointer_read_chain` |  | Follow a pointer chain hop by hop and read the final value. |
| `pointer_find_references` |  | Find addresses that point at or just below a target. |
| `pointer_create_map` |  | Start a job that captures a pointer map. |
| `pointer_list_maps` |  | List pointer maps with their capture progress. |
| `pointer_delete_map` |  | Delete a pointer map and cancel its capture. |
| `pointer_find_paths` |  | Start a job that searches a pointer map for paths to a target. |
| `pointer_rescan_paths` |  | Filter stored paths against a new map or live memory. |
| `pointer_list_paths` |  | Page stored pointer paths; offsets are in dereference order. |
| `pointer_list_scans` |  | List stored pointer-path scans. |
| `pointer_delete_scan` |  | Delete a stored pointer-path scan. |

## module (4)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `module_list` |  | Page the target's modules. |
| `module_get` |  | Read one module's sections and PE header fields. |
| `module_list_exports` |  | Page a module's exports. |
| `module_find_patches` |  | Compare a module's code in memory with its file on disk. |

## symbol (8)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `symbol_resolve` |  | Resolve up to 256 expressions to addresses and addresses to names. |
| `symbol_register` |  | Register a named symbol owned by this activation. |
| `symbol_unregister` |  | Unregister a symbol this activation registered. |
| `symbol_list_registered` |  | Page registered symbols, marking the ones MCP owns. |
| `symbol_get_module_preference` |  | Read the module precedence of symbol lookup. |
| `symbol_set_module_preference` |  | Set the module precedence of symbol lookup. |
| `symbol_reload` |  | Reload symbols for new modules, all modules or .NET; never waits. |
| `symbol_add_module` |  | Load symbols for a module from a file. |

## speedhack (2)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `speedhack_get_state` |  | Read the speedhack speed and whether its hooks are installed. |
| `speedhack_set_speed` | TCE | Set the speedhack speed; the first use injects the speedhack. |

## util (2)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `util_convert_value` |  | Convert a value between value types, byte orders and encodings. |
| `util_calculate` |  | Evaluate an integer expression with 64-bit wraparound. |

## code (13)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `code_disassemble` |  | Disassemble instructions at an address, optionally with predecessors. |
| `code_decode` |  | Decode instruction fields: prefix, mnemonic, operands and control-flow flags. |
| `code_disassemble_bytes` |  | Disassemble raw bytes without reading target memory. |
| `code_get_function` |  | Estimate the function bounds around an address. |
| `code_start_dissect` |  | Start a code dissection job over a module or range. |
| `code_start_search` |  | Start a code search job by text, references or RIP-relative operands. |
| `code_poll_job` |  | Poll a dissection or search job's progress and hits. |
| `code_find_references` |  | Page the code references to an address from the dissection data. |
| `code_find_strings` |  | Page the referenced strings from the dissection data. |
| `code_list_functions` |  | Page the functions found by the dissector. |
| `code_clear_dissect` |  | Clear Cheat Engine's dissection data. |
| `code_get_comments` |  | Read Memory View comments and headers for up to 256 addresses. |
| `code_set_comment` |  | Set or clear a Memory View comment or header. |

## asm (8)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `asm_assemble` |  | Assemble instructions to bytes without writing them. |
| `asm_check` | AA | Check an Auto Assembler script without applying it. |
| `asm_apply` | AA | Apply an Auto Assembler script and keep its patch for release. |
| `asm_apply_code_patch` | AA | Apply a reversible byte, instruction or NOP patch. |
| `asm_release_patch` |  | Release a patch, running its disable section once. |
| `asm_list_patches` |  | List the patches this activation owns. |
| `asm_generate_injection` |  | Generate a code, AOB or full injection template. |
| `asm_generate_api_hook` |  | Generate an API hook template with enable and disable sections. |

## record (12)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `record_list` |  | Page address-list records with their hierarchy. |
| `record_get` |  | Read up to 64 records in detail. |
| `record_find` |  | Find records by description, address, value type or state. |
| `record_get_selected` |  | Read the selected record or records. |
| `record_select` |  | Select a record in the address list. |
| `record_create` | →AA | Create up to 256 value, script or group records; rolled back on failure. |
| `record_update` |  | Update up to 256 records; offsets are in dereference order. |
| `record_set_active` | →AA | Activate (freeze) or deactivate records. |
| `record_delete` |  | Delete records and their children. |
| `record_move` |  | Move records under another parent or to the root. |
| `record_group` |  | Group records under a new header. |
| `record_set_script` | →AA | Replace an Auto Assembler record's script. |

## table (3)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `table_load` | →UL/AA/TCE | Load a cheat table from the allowed roots after inspecting it. |
| `table_save` |  | Save the address list as a cheat table. |
| `table_list_files` |  | Page cheat table files under the allowed roots. |

## structure (13)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `structure_list` |  | Page Structure Dissect definitions. |
| `structure_get` |  | Read a structure's elements. |
| `structure_create` |  | Create a structure from elements, a clone or a PDB type. |
| `structure_delete` |  | Delete a structure. |
| `structure_add_elements` |  | Add elements to a structure. |
| `structure_update_elements` |  | Update structure elements. |
| `structure_remove_elements` |  | Remove structure elements. |
| `structure_autoguess` |  | Let Cheat Engine guess a structure's fields from memory. |
| `structure_fill_from_dotnet` |  | Fill a structure from a .NET object's layout. |
| `structure_get_pdb_layout` |  | Read a PDB type's field layout. |
| `structure_read` |  | Read structure values at up to 16 addresses. |
| `structure_write_element` |  | Write one structure element's value. |
| `structure_compare` |  | Compare structure instances to find the fields that tell them apart. |

## debugger (18)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `debugger_attach` | →KA/TCE | Attach a debugger interface. |
| `debugger_detach` |  | Detach the debugger. |
| `debugger_get_status` |  | Read the debugger state. |
| `debugger_break_thread` |  | Ask a thread to break. |
| `debugger_set_breakpoint` |  | Set an execute, access or write breakpoint. |
| `debugger_delete_breakpoint` |  | Delete a breakpoint. |
| `debugger_list_breakpoints` |  | Page the breakpoints. |
| `debugger_continue` |  | Continue from a break. |
| `debugger_step` |  | Step into or over one instruction. |
| `debugger_get_context` |  | Read the broken thread's registers, optionally with FPU and XMM state. |
| `debugger_set_register` |  | Write one register of the broken thread. |
| `debugger_set_thread_ignored` |  | Add or remove a thread from the break-ignore list. |
| `debugger_get_stack_trace` |  | Read a heuristic stack trace of the broken thread. |
| `debugger_start_capture` |  | Start a job that records the instructions accessing an address. |
| `debugger_poll_capture` |  | Poll a capture job's hits. |
| `debugger_start_trace` |  | Start a step-trace job from an address. |
| `debugger_poll_trace` |  | Poll a trace job's steps. |
| `debugger_run_to` |  | Run until an address through a one-shot breakpoint. |

## exec (6)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `exec_inject_library` | TCE | Inject a native DLL into the target. |
| `exec_inject_dotnet` | TCE | Inject a .NET assembly and call a static method, waiting at most 30 s. |
| `exec_call_remote` | TCE | Call a target function with typed arguments, waiting at most 10 s. |
| `exec_call_method` | TCE | Call a target instance method, waiting at most 10 s. |
| `exec_call_local` | TCE | Call a function inside Cheat Engine's own process. |
| `exec_compile_c` | TCE | Compile C source into target memory. |

## dotnet (9)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `dotnet_get_status` |  | Report whether the .NET data collector is available and attached. |
| `dotnet_list_domains` |  | List .NET application domains. |
| `dotnet_list_modules` |  | Page a domain's .NET modules. |
| `dotnet_list_types` |  | Page a module's .NET types. |
| `dotnet_get_type` |  | Read a .NET type's fields. |
| `dotnet_list_methods` |  | Page a .NET type's methods. |
| `dotnet_get_object` |  | Inspect the .NET object at an address. |
| `dotnet_start_instance_search` |  | Start a job that finds instances of a .NET type. |
| `dotnet_poll_instance_search` |  | Poll a .NET instance search. |

## mono (14)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `mono_attach` | TCE | Inject Cheat Engine's Mono data collector into the target. |
| `mono_detach` |  | Detach the Mono data collector. |
| `mono_get_status` |  | Read the Mono data collector's state. |
| `mono_list_assemblies` |  | Page Mono assemblies. |
| `mono_list_classes` |  | Page an image's Mono classes. |
| `mono_find_class` |  | Find a Mono class by namespace and name. |
| `mono_list_fields` |  | List a Mono class's fields. |
| `mono_list_methods` |  | Page a Mono class's methods. |
| `mono_find_method` |  | Find a Mono method. |
| `mono_get_static_field_address` |  | Read a Mono class's static field base address. |
| `mono_compile_method` | TCE | JIT-compile a Mono method. |
| `mono_invoke_method` | TCE | Invoke a Mono method. |
| `mono_start_instance_search` |  | Start a job that finds instances of a Mono class. |
| `mono_poll_instance_search` |  | Poll a Mono instance search. |

## kernel (7)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `kernel_get_status` | KA | Read the DBK and DBVM state and control registers. |
| `kernel_initialize_dbvm` | KA | Initialize DBVM; may prompt and can destabilize the host. |
| `kernel_translate_address` | KA | Translate a virtual address to a physical one. |
| `kernel_read_physical` | KA | Read physical memory through DBVM. |
| `kernel_write_physical` | KA | Write physical memory through DBVM. |
| `kernel_start_watch` | KA | Start a DBVM watch job. |
| `kernel_poll_watch` | KA | Poll a DBVM watch job's events. |

## lua (2)

| Tool | Gate | Purpose |
| --- | --- | --- |
| `lua_execute` | UL | Execute Lua source and return bounded values. |
| `lua_find_api` |  | Search the local celua.txt Lua API reference. |

## Legacy tools (removed by 2.0.0)

These pre-v2 tools are still served while their domain is migrated; each disappears when its replacement lands.
They keep the old contract: a `success` field in band, no output schema and no annotations.

| Tool | Replaced by |
| --- | --- |
| `add_code_reference` | removed |
| `add_memory_record` | `record_create` |
| `add_structure_element` | `structure_add_elements` |
| `allocate_memory` | `memory_allocate` |
| `analyze_code_range` | `code_start_dissect` |
| `aob_scan` | `aob_find` |
| `aob_scan_unique` | `aob_find` |
| `assemble` | `asm_assemble` |
| `auto_assemble` | `asm_apply`, `asm_release_patch` |
| `auto_assemble_check` | `asm_check` |
| `autoguess_structure` | `structure_autoguess` |
| `clear_code_analysis` | `code_clear_dissect` |
| `compare_memory` | `memory_compare` |
| `convert_string` | removed |
| `copy_memory` | `memory_copy` |
| `create_process` | `process_create` |
| `create_structure` | `structure_create` |
| `dbk_control_registers` | `kernel_get_status` |
| `dbk_physical_address` | `kernel_translate_address` |
| `dbk_status` | `kernel_get_status` |
| `dbvm_cr4` | `kernel_get_status` |
| `dbvm_initialize` | `kernel_initialize_dbvm` |
| `dbvm_read_physical` | `kernel_read_physical` |
| `dbvm_status` | `kernel_get_status` |
| `dbvm_watch` | `kernel_start_watch` |
| `dbvm_watch_log` | `kernel_poll_watch` |
| `dbvm_watch_stop` | `runtime_stop_job` |
| `dbvm_write_physical` | `kernel_write_physical` |
| `debugger_add_breakpoint` | `debugger_set_breakpoint` |
| `debugger_break_thread` | `debugger_break_thread` |
| `debugger_breakpoints` | `debugger_list_breakpoints` |
| `debugger_context` | `debugger_get_context` |
| `debugger_continue` | `debugger_continue`, `debugger_step` |
| `debugger_detach` | `debugger_detach` |
| `debugger_ignore_thread` | `debugger_set_thread_ignored` |
| `debugger_poll_capture` | `debugger_poll_capture` |
| `debugger_poll_step_trace` | `debugger_poll_trace` |
| `debugger_remove_breakpoint` | `debugger_delete_breakpoint` |
| `debugger_set_register` | `debugger_set_register` |
| `debugger_start` | `debugger_attach` |
| `debugger_start_capture` | `debugger_start_capture` |
| `debugger_start_step_trace` | `debugger_start_trace` |
| `debugger_status` | `debugger_get_status` |
| `debugger_stop_capture` | `runtime_stop_job` |
| `debugger_stop_step_trace` | `runtime_stop_job` |
| `delete_code_reference` | removed |
| `delete_memory_record` | `record_delete` |
| `delete_pointer_map` | `pointer_delete_map` |
| `delete_structure` | `structure_delete` |
| `disassemble` | `code_disassemble` |
| `disassemble_bytes` | `code_disassemble_bytes` |
| `disassemble_range` | `code_disassemble` |
| `dump_memory` | `memory_dump_to_file` |
| `enum_memory_regions` | `memory_list_regions` |
| `enum_module_sections` | `module_get` |
| `enum_modules` | `module_list` |
| `enum_registered_symbols` | `symbol_list_registered` |
| `execute_local_code` | `exec_call_local` |
| `execute_lua` | `lua_execute` |
| `execute_remote_code` | `exec_call_remote` |
| `fill_structure_from_dotnet` | `structure_fill_from_dotnet` |
| `find_memory_records_by_description` | `record_find` |
| `free_memory` | `memory_free` |
| `full_access_memory` | `memory_set_protection` |
| `generate_api_hook_script` | `asm_generate_api_hook` |
| `generate_pointer_map` | `pointer_create_map` |
| `get_address_list` | `record_list` |
| `get_code_references` | `code_find_references` |
| `get_comment` | `code_get_comments` |
| `get_current_process` | `process_get_current` |
| `get_function_range` | `code_get_function` |
| `get_memory_protection` | `memory_get_address_info` |
| `get_memory_record_details` | `record_get` |
| `get_memory_region` | `memory_get_address_info` |
| `get_memory_scan_results` | `scan_list_results` |
| `get_memory_scan_status` | `scan_get_status` |
| `get_module_preference` | `symbol_get_module_preference` |
| `get_name_from_address` | `symbol_resolve` |
| `get_opened_file_size` | `process_get_current` |
| `get_plugin_version` | `runtime_get_info` |
| `get_pointer_scan_results` | `pointer_list_paths` |
| `get_pointer_size` | `process_get_current` |
| `get_previous_opcodes` | `code_disassemble` |
| `get_process_list` | `process_list` |
| `get_process_state` | `process_get_current` |
| `get_referenced_functions` | `code_list_functions` |
| `get_referenced_strings` | `code_find_strings` |
| `get_runtime_info` | `runtime_get_info` |
| `get_selected_memory_record` | `record_get_selected` |
| `get_speedhack_speed` | `speedhack_get_state` |
| `get_structure` | `structure_get` |
| `get_structure_element_value` | `structure_read` |
| `get_structure_elements` | `structure_get_pdb_layout` |
| `get_symbol_info` | `symbol_resolve` |
| `get_symbols_loading_state` | removed |
| `get_thread_list` | `process_list_threads` |
| `hash_memory` | `memory_hash` |
| `inject_dotnet_library` | `exec_inject_dotnet` |
| `inject_library` | `exec_inject_library` |
| `is_jump_destination` | `code_get_function` |
| `list_memory_scanners` | `scan_list_scanners` |
| `list_pointer_maps` | `pointer_list_maps` |
| `list_structures` | `structure_list` |
| `load_cheat_table` | `table_load` |
| `load_new_symbols` | `symbol_reload` |
| `lookup_rtti_class_name` | `memory_get_address_info` |
| `memory_scan` | `scan_first` |
| `next_memory_scan` | `scan_next` |
| `open_file_as_process` | `process_open_file` |
| `open_process` | `process_attach` |
| `pause_process` | `process_set_paused` |
| `pointer_scan` | `pointer_find_paths` |
| `read_memory` | `memory_read` |
| `read_pointer_chain` | `pointer_read_chain` |
| `register_symbol` | `symbol_register` |
| `reinitialize_dotnet_symbols` | `symbol_reload` |
| `reinitialize_symbols` | `symbol_reload` |
| `release_target_resources` | `runtime_release_resources` |
| `remove_structure_element` | `structure_remove_elements` |
| `rescan_pointer_scan` | `pointer_rescan_paths` |
| `reset_memory_scan` | `scan_reset`, `scan_delete` |
| `reset_pointer_scan` | `pointer_delete_scan` |
| `resolve_address` | `symbol_resolve` |
| `resume_process` | `process_set_paused` |
| `save_cheat_table` | `table_save` |
| `save_opened_file` | `process_save_file` |
| `select_memory_record` | `record_select` |
| `set_comment` | `code_set_comment` |
| `set_memory_protection` | `memory_set_protection` |
| `set_memory_record_active` | `record_set_active` |
| `set_memory_record_offsets` | `record_update` |
| `set_memory_record_script` | `record_set_script` |
| `set_module_preference` | `symbol_set_module_preference` |
| `set_pointer_size` | `process_set_pointer_size` |
| `set_speedhack_speed` | `speedhack_set_speed` |
| `set_structure_element_value` | `structure_write_element` |
| `unregister_symbol` | `symbol_unregister` |
| `update_memory_record` | `record_update` |
| `update_structure_element` | `structure_update_elements` |
| `wait_for_symbols` | removed |
| `write_memory` | `memory_write` |
