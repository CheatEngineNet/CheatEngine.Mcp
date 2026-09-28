# Clean up this session

## Goal

Undo what this session changed in the game and in Cheat Engine, release what it owns in a safe order, and report what
needs manual recovery. Touch only this session's work. `{keepAddressList}`: yes (the default) keeps its records; no
deletes them after asking.

## Steps

1. Inventory, read-only: `runtime_list_resources()`, `runtime_list_jobs()`, `asm_list_patches()`,
   `debugger_get_status()`, `debugger_list_breakpoints()`, `speedhack_get_state()`, `mono_get_status()`,
   `scan_list_scanners()`, `pointer_list_maps()`, `pointer_list_scans()`, `memory_list_snapshots()`,
   `symbol_list_registered(offset=0, limit=200)`, and `record_get(ids=[...])` for this session's records.
2. Show the user what will be stopped, restored, released, deleted or kept; get consent.
3. `runtime_stop_job(jobId="...")` for each running job; a stop discards its items, so poll first if needed. A main
   scan this session started that still runs: `scan_stop(scannerName="main")`.
4. Owned breakpoints (`debugger_list_breakpoints.owned`): `debugger_delete_breakpoint(address="...")`. Only then, if
   `debugger_get_status.broken` is true: `debugger_continue()`.
5. Active records this session created: `record_set_active(ids=[...], active=false)`, unless the user keeps some on.
   A script record (`variableType` 11) runs its DISABLE. An entry whose `active` differs from `requestedActive` was
   refused (`failure.reason`); read a `pending` one again with `record_get`.
6. Patches, newest first: `asm_release_patch(patchId="...")`, then `code_disassemble(address="...", count=5)` at
   the site.
7. A `pause` resource: `process_set_paused(paused=false)`; then, if this session changed the speed,
   `speedhack_set_speed(speed=1)` (refused while paused).
8. `{keepAddressList}` is no: ask, then `record_delete(ids=[...])` for this session's records.
9. Ask, then `structure_delete(name="...")` for structures this session created.
10. `scan_delete(scannerName="...")`, `pointer_delete_scan(scanName="...")`, `pointer_delete_map(mapName="...")`,
    `memory_delete_snapshot(name="...")`. Reset main only if the user asks: `scan_reset(scannerName="main")`.
11. After the patches, whose DISABLE may use them: `symbol_unregister(name="...")`, `memory_free(name="...")`.
12. A `mono` resource: `mono_detach()`; report `mono_detach.remainingEffects`. `mono_detach.alreadyEnded` true: it had
    already ended in Cheat Engine, and any current attachment was left untouched.
13. `runtime_release_resources()` for the rest, newest first (it also resumes MCP's pause and restores its speed); it
    stops at the first incomplete release.
14. Debugger attached by this session: `debugger_detach()`. It resumes and unpauses the game, and refuses with
    `busy` while any tracked resource remains: release the one it names first.
15. Confirm: `runtime_list_resources()`, `runtime_get_overview()`.

## Decisions

- `partial_effect`, an incomplete release or `asm_release_patch.released` false: stop there, report the `hint` and
  what remains; repeat once only when the result says retryable.
- `not_found` on a stop, delete or release: it already ended; confirm with the list tools.
- `hostEffect` `started`, `unknown` or `cleanup_unconfirmed`: inspect first (patch list, disassembly, speed).
- `capability_disabled` (`speedhack_set_speed` needs Mcp:EnableTargetCodeExecution, an active script record
  Mcp:EnableAutoAssembler): `runtime_release_resources()` still restores a speed this session set; for the rest, tell
  the user what to undo in Cheat Engine.
- `orphaned` true (an earlier plugin activation): only with consent, `runtime_release_resources(includeOrphans=true)`.
- A `cleanup_failed` entry (its process is no longer the opened one) needs manual recovery; once the user confirms it
  is undone, end with `runtime_release_resources(acknowledgeIds=["<id>"])`, which also releases the rest. A failed
  patch, allocation, scanner or symbol release shows `requiresManualRecovery` and cannot be acknowledged.
- `debugger_get_status.activeInterface` was veh: the game must restart before the debugger attaches again.

## Pitfalls

- Records, freezes, structures and main results are Cheat Engine's and survive a plugin disable.
- Never touch records, patches, breakpoints, structures or a pause the user made; `debugger_delete_breakpoint` refuses
  the breakpoints MCP does not own.
- Raw `memory_write` and `memory_set_protection` changes and what Lua or remote calls left are untracked: restore them
  only from recorded original values, with consent.
- `process_attach` refuses with `busy` while owned resources or orphans remain; never clean up through another
  instance.

## Report

A table: item | action (stopped, restored, released, deleted, kept) | result | what the user must still do by hand.
Then what was kept on purpose, what failed (with its `hint`), and what stays until the game restarts (speedhack
hooks, even at speed 1; the Mono collector DLL). See [errors and recovery](../Documents/errors-and-recovery.md).
