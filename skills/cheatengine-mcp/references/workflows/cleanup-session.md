# Clean up this session

## Goal

Undo what this session changed in the game and in Cheat Engine, release every owned resource in a safe order, and report anything that needs manual recovery. Address-list records are kept when `{keepAddressList}` is yes (the default).

## Steps

1. Inventory: `runtime_get_overview()`, `runtime_list_resources()`, `runtime_list_jobs()`, `asm_list_patches()`, `debugger_get_status()`, `debugger_list_breakpoints()`, `speedhack_get_state()`, `scan_list_scanners()`, `pointer_list_maps()`, `pointer_list_scans()`, `symbol_list_registered()`.
2. Show the user what will be stopped, restored, released or deleted, and get consent.
3. Jobs: `runtime_stop_job(jobId=...)` for each capture, trace, search and watch.
4. If `debugger_get_status()` shows `broken=true`: `debugger_continue()`.
5. Breakpoints this session set: `debugger_delete_breakpoint(address=...)`; ask about any others.
6. Patches, newest first: `asm_release_patch(patchId=...)`; check each with `code_disassemble`.
7. Records created in this session: `record_set_active(ids=[...], active=false)`; when `{keepAddressList}` is no, ask, then `record_delete(ids=[...])`.
8. Speed: if not 1, `speedhack_set_speed(speed=1)` (or the value from before the session).
9. If this session paused the game: `process_set_paused(paused=false)`.
10. Structures created in this session: ask, then `structure_delete(name=...)`.
11. Named scanners: `scan_delete(scannerName=...)`. Reset main only if the user asks: `scan_reset(scannerName="main")`.
12. Session data: `pointer_delete_scan(scanName=...)`, `pointer_delete_map(mapName=...)`, `symbol_unregister(name=...)`, `memory_free(name=...)`.
13. `runtime_release_resources()` for everything left; it releases newest first and stops at the first incomplete release.
14. `debugger_detach()` if this session attached (after VEH the game must restart before another attach); `mono_detach()` if this session attached Mono.
15. `runtime_list_resources()` and `runtime_get_overview()` to confirm.

## Decisions

- Any `partial_effect` or incomplete release: stop there, report what remains and the hint; never repeat the call blindly.
- `hostEffect` `started`, `unknown` or `cleanup_unconfirmed`: inspect the state (patch list, disassembly, speed) before any retry.
- `capability_disabled` while restoring (for example the speed, which needs the targetCodeExecution gate): tell the user what to undo by hand in CE.
- Entries marked `orphaned` come from an earlier plugin activation; release them only with consent, with `runtime_release_resources(includeMcpState=true)`.

## Pitfalls

- Records, freezes, structures and main scan results belong to CE and survive plugin disable; only explicit calls remove them.
- Never touch records, patches or breakpoints the user made by hand.
- Release before switching target; `process_attach` refuses while resources remain.
- Never switch to another instance to clean up; each instance cleans its own state.

## Report

What was stopped, restored, released and deleted; what was kept on purpose (address list, main results); what could not be released, with the tool's hint; and what the user must undo by hand in CE. See [errors and recovery](../errors-and-recovery.md).
