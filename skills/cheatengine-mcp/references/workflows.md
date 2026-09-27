# Workflows and session rules

Session rules for driving Cheat Engine 7.7 through CheatEngine.Mcp, and the index of the 22 guided workflows. Each workflow is also an MCP prompt of the same name; its body is served as `cheatengine://docs/workflows/<name-with-dashes>`. Read [safety](safety.md) before the first mutation and [errors-and-recovery](errors-and-recovery.md) before handling any failure.

## Golden rules

- Name the instance on every call: pass the exact `instanceId` from `instance_list` to every other tool. There is no default instance, no selected-instance setting, no fallback to another instance and no automatic retry, even when only one Cheat Engine (CE) is running.
- Stay on the authorized target: attach only to the process the user named and is entitled to modify.
- Read before you write: confirm address, type and current value with read-only tools, explain the change, then mutate and report its `hostEffect`.
- Never repeat a mutation after `timeout`, a lost connection, or a `hostEffect` of `started`, `unknown` or `cleanup_unconfirmed`; inspect state first.
- Keep work bounded: narrow scans before listing, page with `offset`/`limit`, scope searches to a module.
- Undo what you create, newest first, before switching targets or ending the session.

## Session start

1. `instance_list`: match the user's intent to `name` and the CE `processId`. Names can repeat; ask when the choice is ambiguous. `discoveryIncomplete: true` means the discovery deadline expired: call it again before concluding that an instance is absent. An empty list: see [connection-troubleshooting](connection-troubleshooting.md).
2. `runtime_get_info`: plugin, CE and Client versions, `pluginPath`, capabilities, limits and the four gates (`unsafeLua`, `autoAssembler`, `targetCodeExecution`, `kernelAccess`). A listed tool can still be refused (`capability_disabled`) or unsupported by this host.
3. `runtime_get_overview`: attached target, debugger state, main and independent scanners, owned resources, speedhack, running jobs. Existing resources or jobs mean earlier work is still live; understand them before starting new work.
4. Only when no target or the wrong one is attached: `process_list` (filter with `nameContains`), confirm the process with the user, then `process_attach` with `processId` or an exact `processName`. Verify with `process_get_current` (bitness, pointer size, `paused`). The CE process ID from `instance_list` is not the target process ID.
5. Orient with `module_list`: `mono-2.0-bdwgc.dll` or `mono.dll` means Mono (Unity), `GameAssembly.dll` means IL2CPP (the `mono_*` tools work only in CE's partial IL2CPP mode, which MCP has not qualified; fall back to native analysis when a `mono_*` call fails), `coreclr.dll` or `clr.dll` means .NET, otherwise native. Ask whether the game is offline or single-player and whether it uses anti-cheat.

## Instances and identifiers

- An `instanceId` lives for one plugin activation. Restarting CE or disabling and re-enabling the plugin creates a new id; every job, patch, record, scanner and pointer-map id from the old activation is invalid. Call `instance_list` again and re-orient.
- `instance_unavailable` means the gateway could not reach or verify that instance. Rediscover; never redirect the work to another instance, even one with the same name.
- Keep the `instanceId` beside every id you save. Identical names in two instances are unrelated objects.
- Two instances attached to the same target share its memory; coordinate or use one instance per target.

## Values and addresses

- Address inputs accept CE expressions: `game.exe+1A2B`, `7FF6A1B2C3D0`, registered symbols. Outputs use uppercase hexadecimal without `0x`.
- 64-bit integers and all memory values are strings; counts, sizes and indexes are integers; bytes are spaced hex such as `48 8B 05`. Numeric scan values are decimal.
- Pointer offsets are signed hex in dereference order: the first offset is added after the base is read. CE's own dialogs list them in reverse; never reverse them for the tools.
- Lists page with `offset` and `limit`; `nextOffset` is absent when the list is complete; `truncated` marks a host-side cap.

## Scanners

- `scannerName` omitted or `main` drives CE's visible scan tab: CE owns it, it survives plugin disable and follows tab changes. Any other name is an independent session (at most 32) that never touches the UI.
- Scans return once started: poll `scan_get_status` until results are ready, then page `scan_list_results`. Never repeat a start after an uncertain response; inspect status first.
- A running main scan blocks target changes; `scan_stop` cancels it. Details: [value-scans](value-scans.md).

## Jobs

Long operations are jobs: start, poll, stop. Every `*_start_*` tool below returns a `jobId`, every `*_poll_*` tool takes `jobId`, `afterSequence` and `limit`, and `runtime_stop_job(jobId)` stops any job.

| Start | Poll or status | Stop |
| --- | --- | --- |
| `debugger_start_capture` | `debugger_poll_capture` | `runtime_stop_job` |
| `debugger_start_trace` | `debugger_poll_trace` | `runtime_stop_job` |
| `code_start_dissect`, `code_start_search` | `code_poll_job` | `runtime_stop_job` |
| `dotnet_start_instance_search` | `dotnet_poll_instance_search` | `runtime_stop_job` |
| `mono_start_instance_search` | `mono_poll_instance_search` | `runtime_stop_job` |
| `kernel_start_watch` | `kernel_poll_watch` | `runtime_stop_job` |
| `pointer_create_map` | `pointer_list_maps` | `runtime_stop_job` or `pointer_delete_map` |
| `pointer_find_paths` | `pointer_list_scans`, then `pointer_list_paths` | `runtime_stop_job` or `pointer_delete_scan` |

- Job ids carry an activation prefix, for example `capture-7f3a2c-1`. An unknown, expired or purged id returns `not_found`, never success.
- Polls are read-only and non-consuming. Start with `afterSequence: 0`, then pass back each `nextAfterSequence`. Repeating a poll after a lost response returns the same items. `more: true` means poll again; `dropped` counts items evicted from the ring buffer (poll sooner or narrow the job); `firstSequence` is the oldest item still held.
- `runtime_list_jobs` shows every job and its state; `runtime_stop_job` is idempotent for a known job.
- Jobs expire: 120 s by default, at most 300 s for any job that touches the target. A finished job stays pollable until its TTL; stop or expiry discards its results, so poll before stopping. Lua-side jobs can outlive a plugin disable until their TTL.
- Ask the user to perform the in-game action between polls; poll at human pace, never in a tight loop.

## Calls that block or prompt

- `table_load`, `kernel_initialize_dbvm` and `mono_attach` can open a CE dialog, and so can a failing first `speedhack_set_speed`; tell the user before calling.
- `exec_call_remote`, `exec_call_method` and `exec_call_local` hold CE for up to 10 s; `exec_inject_dotnet` up to 30 s.
- Each instance runs at most 4 dispatches at once; more return `busy` with `hostEffect: not_started`.

## Resources

- Docs: `cheatengine://docs/<slug>`; this file is `cheatengine://docs/workflows`. They need no instance.
- Live read-only snapshots: `cheatengine://instances/{instanceId}/` followed by `runtime`, `overview`, `process`, `modules`, `modules/{module}`, `regions`, `memory/{address}?size=`, `disassembly/{address}?count=`, `address-list`, `address-list/{recordId}`, `structures`, `structures/{structure}`, `scanners`, `resources`, `patches` or `jobs`. Each equals the result of its source tool. Paged forms take `?offset=..&limit=..` in exactly that order.
- `cheatengine://instances` lists instances like `instance_list`, without tokens.

## Workflow index

| Prompt | Use when | Key tools | Docs |
| --- | --- | --- | --- |
| [attach_and_orient](workflows/attach-and-orient.md) | Start of any session | `instance_list`, `runtime_get_info`, `runtime_get_overview`, `process_list`, `process_attach`, `module_list` | [safety](safety.md), [mono-and-dotnet](mono-and-dotnet.md) |
| [find_known_value](workflows/find-known-value.md) | The value is shown as a number | `scan_first`, `scan_get_status`, `scan_next`, `scan_list_results`, `memory_read`, `memory_write`, `record_create` | [value-scans](value-scans.md), [ce-tutorial](ce-tutorial.md) |
| [find_unknown_value](workflows/find-unknown-value.md) | Only a bar or a change is visible | `scan_first` (`unknown`), `scan_next` (`increased`, `decreased`, `changed`, `unchanged`), `scan_list_results` | [value-scans](value-scans.md) |
| [find_float_value](workflows/find-float-value.md) | Floats, doubles, percentages, positions | `scan_first` (`float`, `between`), `scan_next`, `structure_read` | [value-scans](value-scans.md), [structures](structures.md) |
| [freeze_value](workflows/freeze-value.md) | Hold a value constant | `memory_read`, `record_create`, `record_set_active`, `record_delete` | [cheat-tables](cheat-tables.md) |
| [find_writer](workflows/find-writer.md) | Which instruction writes or reads an address | `debugger_attach`, `debugger_start_capture`, `debugger_poll_capture`, `code_disassemble`, `symbol_resolve`, `runtime_stop_job` | [debugger](debugger.md), [code-analysis](code-analysis.md) |
| [nop_patch](workflows/nop-patch.md) | Disable an instruction reversibly | `code_disassemble`, `asm_apply_code_patch`, `asm_list_patches`, `asm_release_patch` | [auto-assembler](auto-assembler.md), [x64-injection](x64-injection.md) |
| [manual_pointer_chain](workflows/manual-pointer-chain.md) | Walk from a writer back to a static base | `debugger_start_capture`, `pointer_find_references`, `memory_get_address_info`, `pointer_read_chain`, `record_create` | [pointers](pointers.md), [debugger](debugger.md) |
| [pointer_scan](workflows/pointer-scan.md) | Static base via map, paths and rescans | `pointer_create_map`, `pointer_find_paths`, `pointer_rescan_paths`, `pointer_list_paths`, `pointer_read_chain` | [pointers](pointers.md) |
| [aob_injection](workflows/aob-injection.md) | Run custom code at an instruction found by pattern | `aob_generate_signature`, `asm_generate_injection`, `asm_check`, `asm_apply`, `asm_release_patch`, `record_set_script` | [auto-assembler](auto-assembler.md), [x64-injection](x64-injection.md), [aob-signatures](aob-signatures.md) |
| [shared_code_filter](workflows/shared-code-filter.md) | One instruction serves player and enemies | `debugger_start_capture` (`execute`), `structure_compare`, `memory_get_address_info` (RTTI), `asm_apply` | [structures](structures.md), [auto-assembler](auto-assembler.md), [debugger](debugger.md) |
| [injection_copy_base](workflows/injection-copy-base.md) | Copy a base pointer from code into a symbol | `asm_generate_injection`, `asm_check`, `asm_apply`, `symbol_resolve`, `record_create` | [auto-assembler](auto-assembler.md), [pointers](pointers.md) |
| [make_aob_signature](workflows/make-aob-signature.md) | Unique byte pattern for code | `code_disassemble`, `aob_generate_signature`, `aob_find` | [aob-signatures](aob-signatures.md) |
| [dissect_structure](workflows/dissect-structure.md) | Name the fields around a base address | `structure_create`, `structure_autoguess`, `structure_read`, `structure_get_pdb_layout`, `structure_update_elements`, `structure_delete` | [structures](structures.md), [mono-and-dotnet](mono-and-dotnet.md) |
| [trace_logic](workflows/trace-logic.md) | Find the branch that decides an outcome | `debugger_start_trace`, `debugger_poll_trace`, `code_decode`, `debugger_continue` | [debugger](debugger.md), [code-analysis](code-analysis.md), [x64-injection](x64-injection.md) |
| [unity_mono_recon](workflows/unity-mono-recon.md) | Unity (Mono) classes, fields and statics | `mono_attach`, `mono_find_class`, `mono_list_fields`, `mono_get_static_field_address`, `mono_compile_method`, `mono_detach` | [mono-and-dotnet](mono-and-dotnet.md), [safety](safety.md) |
| [dotnet_recon](workflows/dotnet-recon.md) | .NET types, fields and instances | `dotnet_list_modules`, `dotnet_list_types`, `dotnet_get_type`, `dotnet_start_instance_search`, `structure_fill_from_dotnet` | [mono-and-dotnet](mono-and-dotnet.md) |
| [speedhack](workflows/speedhack.md) | Change game speed, then restore it | `speedhack_get_state`, `speedhack_set_speed` | [speedhack](speedhack.md), [safety](safety.md) |
| [build_robust_table](workflows/build-robust-table.md) | A table that survives restarts | `record_list`, `aob_generate_signature`, `asm_generate_injection`, `record_create`, `record_set_active`, `table_save` | [cheat-tables](cheat-tables.md), [aob-signatures](aob-signatures.md), [auto-assembler](auto-assembler.md) |
| [repair_after_update](workflows/repair-after-update.md) | A game update broke a table | `record_get`, `aob_find`, `aob_generate_signature`, `asm_check`, `record_set_script`, `table_save` | [aob-signatures](aob-signatures.md), [cheat-tables](cheat-tables.md) |
| [cleanup_session](workflows/cleanup-session.md) | End of session or before a target switch | `runtime_list_jobs`, `runtime_stop_job`, `asm_release_patch`, `runtime_release_resources`, `debugger_detach` | [errors-and-recovery](errors-and-recovery.md) |
| [ce_tutorial_walkthrough](workflows/ce-tutorial-walkthrough.md) | Learn a technique or smoke-test a setup | `process_attach`, then the mapped workflow | [ce-tutorial](ce-tutorial.md) |

## Switching targets

- Before `process_attach` to another process: finish or `scan_stop` the main scan, stop jobs, restore speed and pause state, release patches, then call `runtime_release_resources`. The attach is refused with `busy` (blockers in `details`) while owned resources remain; re-attaching the already selected PID is allowed.
- After a switch, every absolute address, pointer base and capture result from the old process is meaningless. Re-resolve module-relative expressions and AOB signatures.
- Changing the process in CE's own UI bypasses these checks and can leave resources that need manual recovery.

## Cleanup checklist

Run [cleanup_session](workflows/cleanup-session.md) at the end, before a target switch and before the user disables the plugin. Undo only what this session created.

1. `runtime_list_jobs`, then `runtime_stop_job` for each job the session started.
2. `debugger_list_breakpoints`, then `debugger_delete_breakpoint` for the session's breakpoints; `debugger_continue` if the target is stopped.
3. `asm_list_patches`, then `asm_release_patch` newest first.
4. `record_set_active` with `active: false` for records the session froze or enabled; ask before `record_delete`.
5. `speedhack_set_speed` back to the recorded speed (normally 1); `process_set_paused` with `paused: false` if the session paused the target.
6. `structure_delete` for session structures; `scan_delete` for session scanners (reset `main` only when asked); `pointer_delete_scan` and `pointer_delete_map`; `symbol_unregister` and `memory_free` for session symbols and allocations.
7. `runtime_release_resources`, then `debugger_detach` if the session attached the debugger.
8. `runtime_get_overview` to confirm. Stop at the first incomplete release, report it with its `details`, and never call the cleanup complete.

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Memory_Scanning
- https://wiki.cheatengine.org/index.php?title=Tutorials:Pointers
- https://wiki.cheatengine.org/index.php?title=Mono:Lua
- https://docs.unity3d.com/2021.3/Documentation/Manual/WindowsPlayerIL2CPPScriptingBackend.html
- https://modelcontextprotocol.io/specification/2025-11-25/server/prompts
- https://modelcontextprotocol.io/specification/2025-11-25/server/resources
