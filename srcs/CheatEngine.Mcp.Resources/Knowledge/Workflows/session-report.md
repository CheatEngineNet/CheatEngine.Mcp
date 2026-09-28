# Report what this session found and changed

## Goal

Give a read-only account of this session at `{detail}` detail, for the user or a hand-off: the target and its build,
the findings and how each was proven, the changes still applied, and what cleanup would release. Nothing changes,
except saving the table to `{tablePath}` when a path is given and the user agrees.

## Steps

1. `runtime_get_overview()`: `runtime` (`hostVersion`, `pluginVersion`, `gates`: the Mcp:Enable switches), the
   process (`processName`, `processId`, `pointerSize`), `resourceCount`, `jobCount`.
2. `module_get(module="<processName>")`: keep `pe.timeDateStamp`, which identifies the build: a different stamp later
   means an update.
3. Address list: `record_list(offset=0, limit=100)`, next pages from `record_list.nextOffset`; it pages the whole
   list, nested records included, and a pointer record carries its `offsets` (hex, nearest the base first).
   `record_get(ids=[...])` for chosen ones.
4. Changes: `asm_list_patches()` (`patchId`, `name`, `canDisable`), and the active records: frozen values and enabled
   scripts (`variableType` 11).
5. Owned state: `runtime_list_resources()`, `runtime_list_jobs()`. Kinds `pause`, `speedhack` and `mono` are MCP's
   own pause, speed and Mono attachment.
6. Session data: `scan_list_scanners()`, `pointer_list_maps()`, `pointer_list_scans()`, `memory_list_snapshots()`,
   `symbol_list_registered(offset=0, limit=200)` (`ownedByMcp`), `structure_list(offset=0, limit=100)`.
7. Live state: `debugger_get_status()`, `debugger_list_breakpoints()`, `speedhack_get_state()`, `mono_get_status()`.
   A pause the user made is not reported: take it from the conversation.
8. Only when `{tablePath}` is given: explain that a save writes the whole table (records, scripts, structures, the
   symbols not marked `doNotSave`) and may show a Cheat Engine dialog, ask, then
   `table_save(path="{tablePath}", overwrite=false)`.

## Decisions

- `{detail}` is summary: only what this session created, changed or proved, one short table per section. full: also
  every record, structure, scanner and registered symbol.
- A raw heap address with no module, symbol, pointer chain or signature lasts only until the game restarts: mark it
  "this run only" and suggest a [pointer scan](pointer-scan.md) or an [AOB signature](make-aob-signature.md).
- `orphaned` or `requiresManualRecovery` true, or a patch with `canDisable` false: list it first, with any
  `cleanupError`; it needs the user's attention.
- No process is open: skip step 2 and report what the lists still hold.
- A read fails: note its `kind` and continue; repeat it once at most.
- `table_save` fails with `invalid_argument` on overwrite (the file exists): ask before
  `table_save(path="{tablePath}", overwrite=true)`. A path outside `table_list_files.roots`, or not ending in .CT, .XML
  or .CETRAINER, is refused; no roots means saving is off.
- The user is done: [clean up this session](cleanup-session.md).

## Pitfalls

- Report, do not repair: stop, release, deactivate or delete nothing here.
- Record ids change after a `table_load`; job, patch and resource ids end with a plugin disable. Give descriptions and
  addresses beside ids.
- `asm_list_patches` lists only this activation's patches; scripts enabled in the address list show as active
  records. Raw `memory_write` and `memory_set_protection` changes and what Lua, remote calls or injection left are
  untracked: report them from the history.
- `speedhack_get_state.speed` is Cheat Engine's setting, not a measurement; hooks stay until the game exits.
  `mono_get_status.attached` is true whoever attached the collector; only a `mono` resource is MCP's.
- A failed save can leave a partial file: never overwrite the only copy.

## Report

1. Target: instance (gateway), process, PID, `pointerSize`, build stamp, Cheat Engine and plugin versions, switches
   that are off.
2. Findings: description | address or expression | type | how proven | stable form (signature, chain, symbol).
3. Changes still applied: change | id (record id, `patchId`, resource id) | how to undo.
4. Live state: debugger, owned breakpoints, speed, pause, Mono collector (whose), running jobs.
5. What cleanup would release, and what stays with Cheat Engine (records, structures, main results).
6. How to find each value again, and the saved table path with `table_save.replacedExisting`.

Background: [session rules](../Documents/workflows.md), [cheat tables](../Documents/cheat-tables.md),
[errors and recovery](../Documents/errors-and-recovery.md).
