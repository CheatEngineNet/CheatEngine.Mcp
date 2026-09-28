# Failure kinds and recovery

Read this page when a tool call returns `isError`, a resource or prompt read fails, or a call times out. It explains
the error envelope, every failure kind with its next step, and how to recover without repeating a side effect. Session
rules are in [workflows](workflows.md) and consent in [safety](safety.md); the
[explain_error](../Workflows/explain-error.md) prompt walks through one failure, and
[cleanup_session](../Workflows/cleanup-session.md) releases what a session owns.

## Decide the next step

1. Read `kind` for the class of failure and `hint` for the suggested next step; the hint often names a tool or a
   setting. Quote `message` to the user, but never parse it for logic.
2. Read `hostEffect` to know whether anything changed (see Host effects below):
   - `not_started` or `not_applied`: nothing changed. Fix the argument or precondition, then call again.
   - `completed`: the host work finished before the failure. Verify a change with a read and never repeat it.
   - `started`, `cleanup_unconfirmed` or `unknown`: something may have changed. Read the state first and decide with
     the user; never repeat a mutation blindly.
3. `retryable: true` means the identical call may succeed later as it is. Wait until the condition in the message has
   changed, then repeat it once. `false` does not mean impossible: fix something or inspect the state first.
4. Reads are safe to repeat: read-only tools, resource reads and job polls (a poll's `afterSequence` is a cursor, not
   an acknowledgement, so the same value returns the same page).
5. Retry at most once, and never in a loop. Before continuing a change, tell the user what failed and what, if
   anything, was applied.

## Result shape

- Success: `isError` is absent, `structuredContent` holds the tool's result object, and the same JSON is also sent as
  text. There is no `success` field.
- Failure: `isError: true`, one text block holding the error envelope, and no `structuredContent`. Null fields are
  omitted.

```json
{"error":{"kind":"not_attached","message":"No process is attached.","hostEffect":"not_started","retryable":false,"hint":"Attach a process with process_attach."}}
```

| Field | Meaning |
|---|---|
| `kind` | The failure class; it drives the next step (see Kinds below). |
| `message` | The cause, for people. A server-checked argument failure starts with the parameter name, as in `limit: ...`. |
| `operation` | The Cheat Engine operation or tool that failed, when known. |
| `hostEffect` | How far Cheat Engine got, so whether anything changed (see Host effects below). |
| `retryable` | `true` only when repeating the identical call later is safe and may succeed. |
| `hint` | The suggested next step, when there is one. |
| `details` | Structured context, when the tool has some (see Details by tool below); an `internal` error adds `errorId`. |

Not every negative answer is a failure. Check these fields in successful results:

- `memory_read_batch`, `memory_read_samples`, `memory_get_address_info` and `symbol_resolve` succeed overall while an
  item carries its own `error` (`kind` and `message`); `memory_read_batch` also counts them in `failed`, and
  `memory_read_samples` counts ticks skipped as busy in `missedTicks`. Check every item.
- `record_set_active` goes on past a record CE refuses: its entry's `active` differs from `requestedActive`, and
  `failure` gives a `reason` (such as `assert_failed`) and CE's `text`. With `pending: true`, read it again later.
- `asm_check` returns `accepted: false` with `failedSection` and `hostMessages`. `lua_execute` returns `ok: false` with
  `phase` (`compile` or `runtime`), `error` and the chunk's own `hostEffect` (`not_applied` after a compile error,
  `unknown` after a runtime error).
- `asm_release_patch` returns `released: false` with `retryable` and `requiresManualRecovery` instead of failing.
- `memory_write` and `memory_write_batch` report `verified: false` when the read-back differs or was not requested.
- `aob_generate_signature` can return `unique: false` with only a `triedPattern`.
- A job poll with `dropped` above zero lost its oldest items to the buffer (`Mcp:Execution:JobBufferLimit`, 4096 by
  default), for good: poll sooner or narrow the job.
- A scan can legitimately find nothing, and `process_get_current` returns `isOpen: false` when nothing is attached.

## Kinds

| Kind | What happened | Next step |
|---|---|---|
| `invalid_argument` | Input rejected before anything ran: bad expression, type, range, format or option mix, an ambiguous name or pattern; at the gateway also a missing `instanceId` or an unknown tool. | Fix the argument named in `details.parameter` (or at the start of `message`) from the schema and `hint`; call again. |
| `invalid_state` | CE or the target forbids it now: target paused or stopped in the debugger, no process for `process_set_paused` or `speedhack_set_speed`, wrong record kind or an active script record, a scanner still scanning, without results or invalidated for good, a name in use, a record id from before the last table load, a failed Mono collector pipe or CE's Mono timeout dialog, a changed CE Lua runtime. | Read the state (`process_get_current`, `debugger_get_status`, `scan_get_status`), fix the precondition, call again; after a Mono pipe failure the next Mono call reconnects once, else follow the hint. |
| `not_found` | Unknown or expired id, name, symbol, module or address: a stopped job or one past its lifetime (`Mcp:Execution:JobDefaultTtlSeconds`, 120 s by default, 300 s at most), a released patch, a deleted record or scanner, a symbol this activation did not register, an id of an earlier activation. | List the current items and use a fresh id. Never read it as success. |
| `not_attached` | No target process is selected; for a `mono_` or `dotnet_` tool, also no attached (or an aborted) Mono or .NET data collector. | `process_list`, then `process_attach` the target the user authorized; for Mono, `mono_get_status` and, with consent, `mono_attach`. |
| `busy` | Something competes: the main scan runs, owned resources block `debugger_detach` or a target change (orphans too), a snapshot, rescan or code dissect still runs, nested first scans disagree on `scan_first.includeMapped`, the target is paused or stopped where a call must run code in it or read .NET data, or the activation is at `Mcp:Execution:MaxConcurrentDispatches` (4) or `Mcp:Execution:MaxJobs` (16). | Wait or poll, stop the scan (`scan_stop`) or a job, or release resources with consent; repeat once when `retryable`. |
| `timeout` | A deadline passed. At the gateway the outcome is `unknown`; a bounded read past its dispatch budget reports `completed` (see Timeouts below). | Never repeat a mutation; read the state. Narrow a read and repeat it. |
| `cancelled` | A cancellation was observed before the operation completed. | Repeat only after `not_started` or `not_applied` (then it is `retryable`). |
| `capability_disabled` | An `Mcp:Enable...` switch named in the message is off; nothing ran (`runtime_get_info.gates` shows all four). Some tools need it only for some arguments or content, such as `debugger_attach` with VEH or kernel, or `table_load`. | Tell the user; only they change it, then disable and re-enable the plugin ([configuration](configuration.md)). Never work around it. |
| `unsupported` | This CE build, target or runtime cannot do it: a missing CE API or extension (DBVM, Mono, the scan-region override), a speedhack on a non-x86/x64 target, no .NET runtime, a generic or array Mono object, a debugger MCP cannot drive (DBVM, GDB server, ceserver); also `asm_check` on a script that needs more than the Auto Assembler switch, uses `globalalloc` or a Lua `$` token. | Do not retry; choose another approach. `runtime_get_info` lists the Client capabilities. After a `debugger_attach` refusal with `completed`, detach the debugger. |
| `target_changed` | CE selected another process, or the target's identity could not be confirmed, during the call or since a live snapshot. | `process_get_current`, confirm the target with the user, re-derive addresses, repeat reads only. |
| `host_refused` | CE refused: an Auto Assembler or Lua error, a refused call, a failed precondition inside a fixed script (see Debugger preconditions below). `hostEffect` ranges from `not_started` to `unknown`. | Fix the input from `message`; check scripts with `asm_check`; after `started` or `unknown`, inspect first. |
| `memory_read_failed` | Unreadable address: unmapped, guard page, protection, freed object, broken pointer hop. | `memory_get_address_info`; for a pointer chain, `details` names the hop. |
| `memory_write_failed` | A write was rejected; `hostEffect` says whether any byte changed. | Check the region with `memory_get_address_info`; `memory_set_protection` only with consent; read before a retry. |
| `limit_exceeded` | A size, count or result bound was exceeded. A server check names the parameter in `details`; a result too large to copy back reports `completed` (the script already ran). | Lower the limit or size, page, or narrow the range or module. |
| `partial_effect` | A composite operation stopped midway; some steps applied. Never a success. | Report what applied from `details`, inspect, then finish or undo explicitly. |
| `stopping` | The plugin activation is stopping or has ended: `not_started` when the call never reached CE, otherwise usually `unknown`. | Stop. Through the gateway, call `instance_list` later (a re-enable issues a new id); read the state before any change. |
| `instance_unavailable` | Gateway only: unknown or stale instance id, or the instance could not be reached, verified or kept connected. | `instance_list`; never redirect. With `unknown` the call may have run. |
| `internal` | Unexpected fault or indeterminate host result. The outcome is unknown unless `hostEffect` is `not_started`. | Read the state. Quote `operation` and `errorId`; the user searches for that id in the plugin log `CheatEngine.Mcp.<CE pid>.log` of that instance ([configuration](configuration.md)). Never treat it as "absent". |

Debugger preconditions are fixed-script checks: "Debugger is not attached", "Debugger has no stopped context", a
capture or trace started while stopped, another trace's hook, or a bad breakpoint address return `host_refused` with
`started`, not retryable, although nothing changed yet. Inspect first (`debugger_get_status()`, and
`debugger_list_breakpoints()` for a breakpoint tool), fix the precondition (attach, `debugger_continue()`, or stop the
other trace), then call again ([debugger](debugger.md)).

## Host effects

| hostEffect | Implies and do |
|---|---|
| `not_started` | No Cheat Engine work started. Fix the cause and call again. |
| `not_applied` | Cheat Engine was asked but did not apply the change. Call again once the cause is fixed. |
| `started` | Work began and may be partly applied. Inspect, then finish or undo explicitly. |
| `completed` | The host work finished; the failure came afterwards (verification, copying the result, a cleanup step). Treat a change as applied and verify it with a read. |
| `cleanup_unconfirmed` | The work or a release ran, but its cleanup was not confirmed. Treat the resource as possibly alive; list and verify it. |
| `unknown` | Whether anything ran is unknown (timeout, lost connection, unexpected fault). Read the state before anything else. |

`retryable` is `true` only for `busy` or `cancelled` with `not_started` or `not_applied`, and for the `partial_effect`
of a release that did nothing yet: `memory_free`, `symbol_unregister`, `scan_delete`, `runtime_stop_job` (a job still
ending) and `runtime_release_resources`.

## Details by tool

A server-checked argument failure (`invalid_argument`, `limit_exceeded`) carries `{"parameter": "<name>"}`, sometimes
an item path such as `records[2].offsets`. An `internal` error carries `errorId` (16 hex characters). Acknowledge an
entry only at the end of a cleanup (see Releasing owned resources below). Other structured details:

| Tool | Kind | `details` | Next step |
|---|---|---|---|
| `process_attach`, `process_create`, `process_open_file` | `busy` | `resources` (blockers, newest first), or `refusals` (`message`, `hint`, `details`) when owned resources and a running main scan both block | Release the resources with consent or stop the scan, then repeat |
| `memory_write_batch` | `partial_effect` (the mapped kind, such as `memory_write_failed`, when nothing was written) | `completed`, `failedIndex`, `effectState` (`not_started`, `partial`, `unknown`) | `memory_read_batch` the items; completed ones stay written |
| `memory_load_from_file` | `partial_effect` | `path`, `address`, `bytesWritten`, `totalBytes` | Read the destination range first |
| `record_create` | `partial_effect` (the rollback kept records) | `created`, `rolledBack` | `record_find` or `record_get` the kept records |
| `structure_update_elements`, `structure_remove_elements` | `partial_effect` | `applied`, `failedIndex` (position in your list; it may be partly applied), `failure` | `structure_get`, then retry only the rest |
| `pointer_read_chain` | `memory_read_failed` | `hopIndex` (equal to the offset count when the final value failed), `readAt` | Re-find that hop ([pointers](pointers.md)) |
| `scan_first` (named, mapped memory included) | `partial_effect`, `cleanup_unconfirmed`: CE's mapped-memory override stayed on | `scannerName`, `scanCompleted`, `status` | Results stay when `scanCompleted`; another scan with mapped memory ends the override, else ask the user to restart CE |
| `aob_find`, `aob_find_value` | `host_refused`, `cleanup_unconfirmed`: the same override stayed on | | Repeat the call, which clears it again; if that fails, ask the user to restart CE |
| `memory_free`, `symbol_unregister` | `partial_effect` | `name`, `address` and the `release` (`kind`, `hostEffect`, `isComplete`, `isRetryable`, `requiresManualRecovery`) | Repeat when retryable; otherwise it is no longer tracked: remove it in CE by hand |
| `scan_delete`, `scan_stop` (named) | `partial_effect` | `scannerName`, `retryable`, `requiresManualRecovery` | Repeat when retryable; otherwise recover by hand, then acknowledge |
| `runtime_stop_job`, or a job start whose cleanup failed | `partial_effect` | `resource` and its `release` | Repeat a stop shortly when retryable; after a failed Lua cleanup, recover by hand, then acknowledge |
| `pointer_find_references` | `partial_effect` (references found, temporary scan kept) | `resourceId`, `retryable` | Release it with the other resources, then repeat the search |
| `debugger_delete_breakpoint` | `partial_effect`, `cleanup_unconfirmed` | `resourceId`, `address`, `released`, `cleanupError` | `debugger_list_breakpoints`; remove it in CE, then acknowledge |
| `speedhack_set_speed` | `partial_effect` (the old speed was not restored first) | `resourceId` of the old speedhack | Set speed 1 in CE, then acknowledge; speed changes keep failing until a plugin re-enable ([speedhack](speedhack.md)) |
| `process_set_paused` | `partial_effect` (a resume when the process MCP paused is no longer the opened one) | `resource` and its `release` | Resume that process in CE, then acknowledge the pause |
| `runtime_release_resources` | `partial_effect` | `released`, `failed`, `remaining` | See Releasing owned resources below |
| `process_save_file` | `partial_effect` | `destinationPublished`, `temporaryFileCleanupConfirmed` | When cleanup is unconfirmed, a protected `.partial` file may remain in the write root |

## Resource and prompt read failures

`resources/read` and `prompts/get` have no `isError`: a failure is a JSON-RPC error. When this server raises it,
`error.data` carries `kind`, `operation`, `hostEffect`, `retryable` and `hint` with the same meaning as the tool
envelope, plus `errorId` on an `internal` error; the cause is `error.message` and there are no `details`. A URI that
matches no resource at all can come back from the protocol library without `data`.

| Code | When |
|---|---|
| -32602 | `invalid_argument`: an empty, malformed or out-of-range URI variable, checked before anything runs, or an invalid prompt argument. From protocol 2026-07-28 also every "not found" case below; tell them apart by `error.data.kind`. |
| -32002 | Not found, before protocol 2026-07-28: an unknown URI or path, a query key that is unknown or out of the template's order, or the source tool's `not_found`. |
| -32603 | Any other kind: what the source tool raised (`busy`, `not_attached`, `limit_exceeded`, ...), a disabled switch (`capability_disabled`), a gateway `timeout` or `instance_unavailable`, or an unexpected fault in the resource (`internal`, `unknown`, a generic message and an `errorId`). |

- A live template matches only its own query keys, in their order; a bare list path reads the default page, and
  path values are percent-encoded (`+` as `%2B`). Paths, keys and bounds are in [workflows](workflows.md#resources).
- Through the gateway, read `cheatengine://instances/{instanceId}/...`; the backend form `cheatengine://instance/...` is
  refused as not found, with a hint. `cheatengine://instances` lists the instance ids.
- A resource read never changes anything: read it again once the cause is fixed, including after a gateway `timeout`.

## Timeouts, cancellation and lost connections

- The gateway waits 45 s per tool call or resource read by default (`--call-timeout-seconds` or
  `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`, 5 to 3600, set by the user), a CE dialog waiting for the user included, then
  returns `timeout` with `hostEffect: unknown`. The backend may still run or have finished the operation. The gateway
  never sends the call again, and never to another instance.
- After an unknown outcome, read the state on the same instance (`process_get_current`, `runtime_list_resources`,
  `runtime_list_jobs`, and a read of what the call would change) and decide with the user.
- `code_find_references`, `code_find_strings` and `lua_find_api` fail with `timeout` and `completed` when they run past
  their dispatch budget: nothing changed. Narrow the request and read again.
- When the client cancels a request, the server sends no answer, and whatever already ran stands. Inspect before any
  new mutation.

## Instance unavailable

- Causes: CE closed, the plugin disabled or re-enabled, a stale registry record, an identity mismatch after process-id
  reuse, or a backend that stopped answering. `not_started` means the call never reached the backend; `unknown`
  means it may have run.
- Call `instance_list`, again when it reports `discoveryIncomplete: true`. An instance back under a new id is a new
  activation: orient again ([attach_and_orient](../Workflows/attach-and-orient.md)) and discard every old id.
- Never move the work to another instance, even one with the same name; report the outage. For an empty list, see
  [connection-troubleshooting](connection-troubleshooting.md).

## Stale identifiers

| Event | Invalidated | Recover with |
|---|---|---|
| CE restart, plugin disable and re-enable | The `instanceId` and every id or name the activation issued (jobs, patches, allocations, snapshots, scanners, symbols, pointer maps and scans) | `instance_list`, then the list tools on the new instance |
| `table_load` | Every record id handed out before the load; the record tools refuse it with `invalid_state` or `not_found` (`record_get` fails for the whole read) | `record_list` or `record_find` |
| Target switch or target restart | Absolute addresses (heap objects, allocations), capture and trace results, structure base addresses, live snapshot comparisons | Module-relative expressions, AOB signatures, `pointer_read_chain`, `pointer_rescan_paths`; compare two snapshots instead |
| A New Scan or another main-scan change in CE's UI | The `main` scanner's results | `scan_get_status`, then scan again |
| A named scanner in state `Invalidated` | Its results | `scan_reset` on it, then `scan_first`; after a target or Lua runtime change the reset is refused (`invalid_state`): `scan_delete` it, following its hint, and scan under another name |

## Releasing owned resources

- `runtime_list_resources` lists what this activation owns (allocations, patches, named scanners, registered symbols,
  breakpoints, a speedhack, MCP's pause, the Mono attachment, jobs), newest first, then Lua state that no handle
  tracks: orphans of earlier activations and failed cleanups awaiting an acknowledgement. Each entry has a `category`,
  a `state` (`active`, `ended`, `stop_pending`, `cleanup_failed`) and, when relevant, `orphaned`, `cleanupError` and
  `requiresManualRecovery`. The cleanup order is in [cleanup_session](../Workflows/cleanup-session.md).
- `runtime_release_resources` releases newest first and stops at the first release that does not complete, keeping
  the older ones. A retryable one stays tracked and the error is retryable. One that needs manual recovery is reported
  in `failed`: a Client lease (allocation, patch, scan, symbol) is then no longer tracked, while a Lua entry (job,
  breakpoint, speedhack, pause) stays listed with its `cleanupError`. A Mono attachment already ended in CE is
  released as `externally_removed`.
- Orphans are released only with `runtime_release_resources(includeOrphans=true)`. Without it they stay in
  `remaining`, and the call ends in `partial_effect` even when everything this activation owned was released.
- A Client lease tied to a process that has changed, exited or lost its identity is not released automatically
  (release kind `refused_target_changed`, `refused_target_not_attached` or `refused_target_identity_unavailable`).
  Tell the user what to undo in CE by hand: restore the patched bytes or disable the script, delete the breakpoint, set
  the speed back to 1, resume the process, remove the registered symbol. Memory allocated in an exited process is gone.
- After manual recovery, `runtime_release_resources(acknowledgeIds=["<id>"])` (1 to 64 distinct ids) forgets a
  `cleanup_failed` entry, an orphan or an ended entry without cleanup, then releases every other owned resource. So
  call it only at the end of a cleanup, or when the user agrees to release everything (a target switch requires it);
  never mid-session to forget one leftover. A bad or repeated id is `invalid_argument`, an unknown id `not_found`
  (also a Client lease already reported in `failed`), an entry still active or running `invalid_state`; one refusal
  refuses the whole call. Acknowledge only what the user confirmed.
- Until then the leftover stays listed, and an orphan or failed cleanup blocks `process_attach`, `process_create` and
  `process_open_file` (`busy`). Switching the process in CE's own UI instead of `process_attach` leaves MCP resources
  bound to the old process; expect refusals and manual recovery.

## After a plugin disable or re-enable

- A disable stops admitting calls (a call in flight fails with `stopping`), releases the Client leases newest first
  (allocations, patches, independent scans, symbols) and asks every job to stop; CE's Lua timer ends a Lua job still
  running when its lifetime runs out (at most 300 s).
- CE-owned state stays: the address list with its freezes and active scripts, structures, the main scan, breakpoints
  and a pause the user made, and a loaded Mono collector (the next activation does not list it).
- MCP state recorded in CE's Lua state survives too: MCP-set breakpoints, a speedhack other than 1, MCP's pause, and a
  Lua job that has not ended or whose cleanup failed. The next activation lists it as `orphaned: true`; release it
  with `runtime_release_resources(includeOrphans=true)`, or acknowledge it after manual recovery, before switching
  targets.
- A Client lease that the disable could not release is not listed by the next activation; if the user reports a
  leftover patch, allocation or symbol, recover it in CE by hand.

## Sources

- MCP resources, errors: https://modelcontextprotocol.io/specification/2025-11-25/server/resources
- MCP 2026-07-28 changelog, a missing resource becomes -32602:
  https://modelcontextprotocol.io/specification/2026-07-28/changelog
- MCP tools, `isError` results: https://modelcontextprotocol.io/specification/2025-11-25/server/tools
- MCP cancellation, no response to a cancelled request:
  https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation
