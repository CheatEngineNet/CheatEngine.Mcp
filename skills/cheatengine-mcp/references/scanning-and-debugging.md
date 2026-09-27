# Scanning and debugging workflows

Use the live MCP schema for bounds and defaults. Every call requires the exact `instanceId` from `list_instances`; pass it alongside the arguments shown below. Operations on live memory use that Cheat Engine instance's selected target; verify `get_current_process` on the same instance first. Scan names, pointer maps, captures, traces, and debugger state belong to one activation. After an instance restarts, rediscover it and discard its old handles. Saved pointer files can be loaded under new names, including in another instance; loading a file never attaches to its old process.

## Value scans

One tool family serves both scanner modes. Omit `scannerName` or pass `"main"` to operate on CE's currently visible scan tab. Other case-sensitive names create independent Client sessions (maximum 32 per instance). They never replace the UI scanner or its results. `main` is reserved and does not count toward that limit. Use `list_memory_scanners` to discover names, modes, states, types, and counts. Unknown names fail; they never fall back to main.

The default main scanner uses CE's normal scan actions, so its controls, found count, visible address/value list, and subsequent manual Next Scan stay synchronized. It also reads/narrows scans started manually in CE. Main means the currently selected CE scan tab on each call, not a retained tab ID. Changing tabs changes which results main exposes. Main is CE-owned, survives plugin disable, and is excluded from `release_target_resources`; MCP never destroys its scanner or found list. Independent sessions remain activation-owned and are released by reset, resource cleanup, or disable.

Main scans return after starting. Poll `get_memory_scan_status()` while `state` is `Scanning`; use `ResultsReady` for result paging and `BaselineReady` for narrowing an unknown-initial baseline. `NoTarget` means attach first; `Created` means no first scan; `Failed` includes CE's error text when CE retains it. CE can reset a failed/cancelled first scan back to `Created` and show a native error dialog. If a start returns to `Created`/`NoTarget`, do not report it as completed or retry automatically: inspect CE. `count` is present for completed enumerable UI results only. A scan start is not proof of completed results. Do not retry a start after an uncertain response: inspect status and the visible UI first. Independent Client scans still wait inside their Client operation and return when completed.

Main preserves the visible memory range, region filters, alignment, rounding, and case-sensitivity options. It sets the requested value type/comparison/value and clears inverse, Lua-formula, percentage, repeat, and saved-scan modifiers so those cannot silently change the requested comparison. Inputs are decimal for numeric types and hexadecimal byte tokens for `bytes`. Unsupported manually selected types (such as custom/grouped scans) cannot be narrowed through this typed API. Main pages contain at most 1024 entries with a 4096-byte per-value limit. Large values are refused, not silently truncated.

`memory_scan` accepts `comparison`: `exact` (default), `unknown`, `between`, `greater`, or `less`. Supply `value` for comparisons against a value and `upperValue` for an inclusive `between` range. Omit both values for `unknown`.

`next_memory_scan` supports `exact`, `between`, `greater`, `less`, `increased`, `decreased`, `increasedBy`, `decreasedBy`, `changed`, and `unchanged`. State comparisons omit both values. Delta comparisons take `value`. All requests use Client's typed scan factories, including its numeric/string compatibility and lifecycle validation.

Visible example: `memory_scan(valueType="int32", value="100")`, poll status, then `get_memory_scan_results()`. Change health, then `next_memory_scan(comparison="decreased")`, poll again, and page results. Independent example: `memory_scan(scannerName="health", valueType="int32", comparison="unknown")`, change health, then `next_memory_scan(scannerName="health", comparison="decreased")`. Read with `get_memory_scan_results(scannerName="health")`. Keep the name on every call for independent work; omitting it selects main.

Use `reset_memory_scan(scannerName="health")` to release only that independent scanner. `reset_memory_scan()` clears the visible main results with CE's New Scan action. If the main window is hidden, reset shows it first because CE's handler focuses its scan input. First scans never implicitly discard an existing main scan. Reset/read/narrow operations refuse a busy main scanner; wait or cancel it in CE. A running main scan also blocks MCP target switches, including the delay between iterations of CE's Repeat until stopped mode. Stop repeating in CE before resetting or switching targets. Unknown-initial baselines must be narrowed before paging results.

## Pointer maps, scans and rescans

1. `generate_pointer_map(mapName="before", ...)` captures nonzero pointers using Client memory/module inspection. Optional `startAddress` and `endAddress` constrain the inclusive address range. Omit both to inspect the target's readable regions subject to the byte budget.
2. `pointer_scan(scannerName="healthPointers", mapName="before", targetAddress="0x12345678")` searches the snapshot. `targetAddress` is hexadecimal, not a symbol expression. The default searches up to five dereferences and offsets from zero to 4096, keeping only module roots.
3. `get_pointer_scan_results` pages the stored paths. `offsets` are in dereference order for `read_pointer_chain`; `addressListOffsets` provide CE address-list order. `module` plus `moduleOffset` is the portable root; `baseAddress` is the original snapshot address.
4. After a target restart or value relocation, select the intended process, find the new address, and call `rescan_pointer_scan` with that address. Omit `mapName` for live Client pointer-chain reads, or provide a newly captured map to compare snapshots. Module roots rebase by name. Absolute roots stay absolute. Only confirmed mismatches are discarded; unreadable or missing hops remain candidates and increment `unresolved`.
5. Release data with `delete_pointer_map` and `reset_pointer_scan`. Deleting a map preserves already copied scan paths.

### Save now, continue later

Before closing CE, call both:

```text
save_pointer_map(mapName="before", filePath="C:\\Scans\\before.scandata")
save_pointer_scan(scannerName="healthPointers", filePath="C:\\Scans\\health-pointers.json")
```

Use your own existing local directory. Save never replaces an existing file unless `overwrite=true`; replacement is atomic. `.scandata` stores the map, while the separate `.json` stores the result paths. Keep both if you want to reproduce the old search and continue filtering its candidates.

After restarting CE, rediscover `instanceId`, then:

```text
load_pointer_map(mapName="before", filePath="C:\\Scans\\before.scandata")
load_pointer_scan(scannerName="healthPointers", filePath="C:\\Scans\\health-pointers.json")
```

Reopened maps can be searched offline without an attached target. To continue against a restarted game, attach it, find the value's **new** address, and call `rescan_pointer_scan(scannerName="healthPointers", targetAddress=...)`, or generate a new map and pass its name. Reopened paths begin `unresolved`; only a rescan establishes a new match. Module roots rebase by name; absolute roots do not become portable by saving them. Deleting/resetting in-memory data never deletes saved files.

Map files use CE's native version-1 `.scandata` format: zlib compression, 32/64-bit addresses, module metadata, and per-address static roots. They can be imported from CE and exported for CE's pointer-map loader. Imports accept at most 64 MiB compressed and one million pointer addresses (or a lower `maximumPointers`); larger files are refused without partial state. Module names must be UTF-8, up to 4096 bytes, with at most 4096 modules. Empty maps cannot be exported because CE's native loader cannot load them. Static module offsets above 32 bits cannot be exported losslessly and are refused.

The native format does not contain the capture's process identity, module image sizes, completeness, or read statistics. Loaded maps therefore report `processId=null`, `captureCompleteness="unknown"`, and `incomplete=true`; zero byte statistics mean unavailable. Exporting a bounded capture does **not** turn it into a full-process map. Results use versioned MCP JSON (16 MiB maximum), preserving pointer width, module-relative paths, and the incomplete flag. This is not CE `.ptr` import/export or native `.resume.*` scan-queue continuation. The scanner still has no distributed workers, stack-root heuristics, or negative offsets.

Bounds: four maps, two million total retained pointers, 16 scan result lists, at most 64 MiB attempted reads per capture (16 MiB default), one million pointers per map, eight dereferences, 10,000 result paths, and one million visited candidates. Capture/live rescan work stops after five seconds between host calls; pure snapshot searches stop after one second. One host call itself cannot be interrupted. Guard pages are skipped. Memory changes while the target runs can produce an inconsistent snapshot; pause explicitly if appropriate.

Inspect `incomplete`, `limited`, `unreadableBytes`, `traversalLimited`, and `unresolved`. A bounded or partial map cannot prove that a pointer path does not exist. Narrow capture ranges or raise bounds within the supported limits when necessary. Results after an unresolved rescan include both verified matches and retained uncertain candidates: each path's `verification` is `snapshotMatch`, `liveMatch`, or `unresolved`, and `verifiedMatches` excludes uncertain paths.

## Debugger attachment and state

`debugger_start` is idempotent for the active interface. A different interface requires an explicit `debugger_detach`; MCP never combines detach and reattach. Keep one debugger attached while moving between tutorial steps, and remove individual breakpoints or captures when finished with them.

The installed CE 7.7 VEH debugger crashed on a second attachment to the same running tutorial. MCP retains a process-lifetime VEH attempt marker in CE Lua state, including failed/partial attaches, and refuses another attach after detachment. Restart the target before attaching again; choose a fresh process and discard all old addresses. The marker uses PID plus process creation time so PID reuse does not block a new target, and survives plugin reload. CE itself still owns the native debugger; this guard contains the observed sequence rather than repairing its DLL, and cannot protect operations performed outside MCP. Restarting CE clears its Lua state and must not be used to bypass the target-restart requirement.

`debugger_status` returns `stateValid=true` for validated native state. `broken` means CE can actually supply a stopped context; `reportedBroken` retains CE's weaker raw flag for diagnostics. Invalid native return types produce `success=false`, `stateValid=false` and a diagnostic, not an opaque Lua value. Context reads, register edits and continuation require a readable stopped context. A failed or timed-out attach must not be retried automatically.

## Find writes/accesses

Attach with `debugger_start`, then `debugger_start_capture(address=..., trigger="write")` or `trigger="access"`. Data breakpoint sizes are 1, 2, 4, or 8 and require matching address alignment; eight-byte watches need x64. CE/debugger hardware limits may reject additional watches.

`debugger_poll_capture` returns FIFO hits with thread ID, trap IP, and copied general-purpose registers. Capture callbacks return control to CE for automatic continuation. Breakpoint IDs may be integers or copied `{PID, DTID, ID}` descriptors; use capture/trace IDs for cleanup. MCP retains the original CE breakpoint identifier internally. Hardware data traps usually report the instruction after the access. `instructionAddress`/`disassembly` are a best-effort reverse-disassembly candidate when `instructionAddressIsHeuristic=true`; verify the boundary using disassembly. Do not treat the trap IP itself as the writer instruction.

Polling with `clear=true` consumes only returned hits. Buffers hold at most 1024 hits; overflow increments `dropped`. Call `debugger_stop_capture` to remove the owned breakpoint and release results. Capture lifetime is at most 300 seconds. After expiry, results remain available for 30 seconds before automatic deletion, provided CE's GUI timer can run.

## Break and trace

Remove existing breakpoints and stop captures, then use `debugger_start_step_trace(address=..., maximumSteps=32)`. The target must be running, and another script must not own `debugger_onBreakpoint`. The entry breakpoint arms the trace; subsequent single-step events are restricted to its thread. Other thread events are not automatically continued.

`debugger_poll_step_trace` copies up to 256 contexts, including the entry instruction. Completion leaves the target stopped at the last recorded instruction. `debugger_stop_step_trace` releases the trace without resuming it; use `debugger_continue` explicitly. Traces expire after at most 60 seconds, and results expire 30 seconds after completion/timeout. Expiring a running single-step trace may leave its next debugger event stopped in CE's UI.

## Register editing and cleanup

While broken, use `debugger_set_register(register="RAX", value=42)`. This supports x86/x64 general-purpose registers and EFLAGS, with width validation and read-back verification. On x64, EAX-style aliases write the corresponding RAX-style register with zero extension. Values for 64-bit registers use signed JSON integers; negative values represent their 64-bit two's-complement bit pattern. Floating-point/SIMD registers are readable through `debugger_context(includeExtraRegisters=true)` but are not edited by this tool.

Active jobs block MCP target changes and interfering debugger operations. Stop jobs before switching targets. If cleanup fails, the error returns a job ID and recovery details; retry stop with that ID. A target switched outside MCP may require manual cleanup in CE. Callbacks and timers contain only Lua and can outlive plugin disable until their finite expiry; they hold no managed callbacks. CE-owned timer execution can be delayed while the GUI is busy. Arbitrary Lua or external CE actions can interfere with ownership.

Bindings follow the installed `celua.txt`. CE's [Lua breakpoint implementation](https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas) also establishes that callbacks receive `THREADID` and register globals. Automated tests execute these scripts against standalone Lua with CE functions stubbed; native debugger behavior still requires a fresh CE-hosted test.
