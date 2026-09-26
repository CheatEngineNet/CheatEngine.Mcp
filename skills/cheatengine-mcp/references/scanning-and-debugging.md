# Scanning and debugging workflows

Use the live MCP schema for bounds and defaults. Every call requires the exact `instanceId` from `list_instances`; pass it alongside the arguments shown below. These operations use that Cheat Engine instance's selected target; verify `get_current_process` on the same instance first. Scan names, pointer maps, captures, traces, and debugger state cannot be shared across instances. After an instance restarts, rediscover it and discard its old handles.

## Value scans

`memory_scan` accepts `comparison`: `exact` (default), `unknown`, `between`, `greater`, or `less`. Supply `value` for comparisons against a value and `upperValue` for an inclusive `between` range. Omit both values for `unknown`.

`next_memory_scan` supports `exact`, `between`, `greater`, `less`, `increased`, `decreased`, `increasedBy`, `decreasedBy`, `changed`, and `unchanged`. State comparisons omit both values. Delta comparisons take `value`. All requests use Client's typed scan factories, including its numeric/string compatibility and lifecycle validation.

Example: `memory_scan(scannerName="health", valueType="int32", comparison="unknown")`, change health in the target, then `next_memory_scan(scannerName="health", comparison="decreased")`. Read a bounded page with `get_memory_scan_results`; repeat narrowing, then `reset_memory_scan`. Unknown-initial scans represent the initial baseline: enumerating it may not be meaningful until a next scan materializes matches.

## Pointer maps, scans and rescans

1. `generate_pointer_map(mapName="before", ...)` captures nonzero pointers using Client memory/module inspection. Optional `startAddress` and `endAddress` constrain the inclusive address range. Omit both to inspect the target's readable regions subject to the byte budget.
2. `pointer_scan(scannerName="healthPointers", mapName="before", targetAddress="0x12345678")` searches the snapshot. `targetAddress` is hexadecimal, not a symbol expression. The default searches up to five dereferences and offsets from zero to 4096, keeping only module roots.
3. `get_pointer_scan_results` pages the stored paths. `offsets` are in dereference order for `read_pointer_chain`; `addressListOffsets` provide CE address-list order. `module` plus `moduleOffset` is the portable root; `baseAddress` is the original snapshot address.
4. After a target restart or value relocation, select the intended process, find the new address, and call `rescan_pointer_scan` with that address. Omit `mapName` for live Client pointer-chain reads, or provide a newly captured map to compare snapshots. Module roots rebase by name. Absolute roots stay absolute. Only confirmed mismatches are discarded; unreadable or missing hops remain candidates and increment `unresolved`.
5. Release data with `delete_pointer_map` and `reset_pointer_scan`. Deleting a map preserves already copied scan paths.

Maps are managed snapshots retained only for the current plugin activation. They survive process selection changes to permit rescans, but are not CE `.scandata`/`.ptr` files and are not saved to disk. This is a bounded in-memory scanner, without CE's distributed scanner, stack-root heuristics, negative offsets, or native map-file import/export.

Bounds: four maps, two million total retained pointers, 16 scan result lists, at most 64 MiB attempted reads per capture (16 MiB default), one million pointers per map, eight dereferences, 10,000 result paths, and one million visited candidates. Capture/live rescan work stops after five seconds between host calls; pure snapshot searches stop after one second. One host call itself cannot be interrupted. Guard pages are skipped. Memory changes while the target runs can produce an inconsistent snapshot; pause explicitly if appropriate.

Inspect `incomplete`, `limited`, `unreadableBytes`, `traversalLimited`, and `unresolved`. A bounded or partial map cannot prove that a pointer path does not exist. Narrow capture ranges or raise bounds within the supported limits when necessary. Results after an unresolved rescan include both verified matches and retained uncertain candidates: each path's `verification` is `snapshotMatch`, `liveMatch`, or `unresolved`, and `verifiedMatches` excludes uncertain paths.

## Find writes/accesses

Attach with `debugger_start`, then `debugger_start_capture(address=..., trigger="write")` or `trigger="access"`. Data breakpoint sizes are 1, 2, 4, or 8 and require matching address alignment; eight-byte watches need x64. CE/debugger hardware limits may reject additional watches.

`debugger_poll_capture` returns FIFO hits with thread ID, trap IP, and copied general-purpose registers. Capture callbacks continue the target automatically. Hardware data traps usually report the instruction after the access. `instructionAddress`/`disassembly` are a best-effort reverse-disassembly candidate when `instructionAddressIsHeuristic=true`; verify the boundary using disassembly. Do not treat the trap IP itself as the writer instruction.

Polling with `clear=true` consumes only returned hits. Buffers hold at most 1024 hits; overflow increments `dropped`. Call `debugger_stop_capture` to remove the owned breakpoint and release results. Capture lifetime is at most 300 seconds. After expiry, results remain available for 30 seconds before automatic deletion, provided CE's GUI timer can run.

## Break and trace

Remove existing breakpoints and stop captures, then use `debugger_start_step_trace(address=..., maximumSteps=32)`. The target must be running, and another script must not own `debugger_onBreakpoint`. The entry breakpoint arms the trace; subsequent single-step events are restricted to its thread. Other thread events are not automatically continued.

`debugger_poll_step_trace` copies up to 256 contexts, including the entry instruction. Completion leaves the target stopped at the last recorded instruction. `debugger_stop_step_trace` releases the trace without resuming it; use `debugger_continue` explicitly. Traces expire after at most 60 seconds, and results expire 30 seconds after completion/timeout. Expiring a running single-step trace may leave its next debugger event stopped in CE's UI.

## Register editing and cleanup

While broken, use `debugger_set_register(register="RAX", value=42)`. This supports x86/x64 general-purpose registers and EFLAGS, with width validation and read-back verification. On x64, EAX-style aliases write the corresponding RAX-style register with zero extension. Values for 64-bit registers use signed JSON integers; negative values represent their 64-bit two's-complement bit pattern. Floating-point/SIMD registers are readable through `debugger_context(includeExtraRegisters=true)` but are not edited by this tool.

Active jobs block MCP target changes and interfering debugger operations. Stop jobs before switching targets. If cleanup fails, the error returns a job ID and recovery details; retry stop with that ID. A target switched outside MCP may require manual cleanup in CE. Callbacks and timers contain only Lua and can outlive plugin disable until their finite expiry; they hold no managed callbacks. CE-owned timer execution can be delayed while the GUI is busy. Arbitrary Lua or external CE actions can interfere with ownership.

Bindings follow the installed `celua.txt`. CE's [Lua breakpoint implementation](https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas) also establishes that callbacks receive `THREADID` and register globals. Automated tests execute these scripts against standalone Lua with CE functions stubbed; native debugger behavior still requires a fresh CE-hosted test.
