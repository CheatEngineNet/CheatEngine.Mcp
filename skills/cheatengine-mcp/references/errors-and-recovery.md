# Failure kinds and recovery

How CheatEngine.Mcp reports failures, what each field means, and how to recover without repeating a side effect. Session rules are in [workflows](workflows.md); responsibilities are in [safety](safety.md).

## Result shape

- Success: `isError` is absent and `structuredContent` holds the tool's object result; the same JSON is also sent as text. There is no `success` field.
- Failure: `isError: true`, one text block holding the error envelope, and no `structuredContent`:

```json
{"error":{"kind":"not_attached","message":"No process is attached.","operation":"Memory.ReadBytes","hostEffect":"not_started","retryable":false,"hint":"Attach a process with process_attach."}}
```

| Field | Meaning |
| --- | --- |
| `kind` | Failure class, snake_case; drives the next action (table below). |
| `message` | Human-readable cause. Quote it to the user; never parse it for logic. |
| `operation` | The host operation that failed, when known. Useful in reports and logs. |
| `hostEffect` | How far the host got. Decides whether anything changed. |
| `retryable` | `true` only when repeating the identical call later is safe and may succeed. |
| `hint` | Suggested next step, often naming a tool or setting. |
| `details` | Optional structured payload: blockers for `busy`, progress for `partial_effect`, release state. |

- Batch reads (`memory_read_batch`, `symbol_resolve`, `memory_get_address_info`) can succeed overall while an item carries its own `error`; check every item.
- A negative answer is not a failure: `asm_check` returns `accepted: false` with messages, and a scan can legitimately find zero results.

## Resource read failures

`resources/read` has no `isError`: a failed read is a JSON-RPC error.

| Code | When |
| --- | --- |
| -32602 | Invalid argument (bad address, limit, name); also "not found" for clients on protocol 2026-07-28 or later. |
| -32002 | Resource not found, for earlier protocol versions. |
| -32603 | Any other failure. |

`error.data` carries `kind`, `operation`, `hostEffect`, `retryable` and `hint` with the same meaning as the tool envelope. Resources never mutate, so a read may be repeated once the cause is fixed. Through the gateway, use `cheatengine://instances/{instanceId}/...`; the backend form `cheatengine://instance/...` is rejected. Paged templates match only `?offset=..&limit=..` in that order.

## Kinds

| Kind | Meaning | Next action |
| --- | --- | --- |
| `invalid_argument` | Input rejected: bad expression, type, range, format or option combination; at the gateway also a missing `instanceId` or an unknown tool. | Fix the argument from the live schema and `hint`. |
| `invalid_state` | Host state forbids it: target paused or stopped in the debugger, debugger not attached, wrong record kind, scan without a baseline, CE runtime changed. | Read state (`runtime_get_overview`, `debugger_get_status`, `scan_get_status`), fix the precondition, call again. |
| `not_found` | Unknown or expired id, name, symbol or address: purged job, released patch, deleted structure, record id older than the last table load. | List the current items and use a fresh id. Never read it as success. |
| `not_attached` | No target process is selected. | `process_list`, then `process_attach` the authorized target. |
| `busy` | In use: a running main scan, owned resources blocking a target change, too many concurrent dispatches (4) or jobs. `details` names blockers. | Wait and poll, stop the scan or job, or release resources; repeat only when `retryable`. |
| `timeout` | A deadline passed; the outcome is unknown. | Never repeat a mutation; inspect state. A read may be repeated. |
| `cancelled` | The request was cancelled before completion. | Check `hostEffect`; repeat only after `not_started` or `not_applied`. |
| `capability_disabled` | A gate is off (`Mcp:Enable...` in `hint`). | Report it; never bypass it. See [safety](safety.md). |
| `unsupported` | This CE, Client, host or target cannot do it (bitness, a feature missing from CE's partial IL2CPP mode, missing capability). | Choose another approach or tool; on IL2CPP fall back to native analysis. |
| `target_changed` | The target changed or its identity could not be confirmed during the call. | `process_get_current`; re-derive addresses; never repeat a mutation blindly. |
| `host_refused` | CE rejected the operation: Lua error, Auto Assembler error, refused call. | Read `message`, fix the input; validate scripts with `asm_check` first. |
| `memory_read_failed` | Address unreadable: unmapped, guard page, protection, freed object, broken pointer hop. | `memory_get_address_info`; re-walk the chain with `pointer_read_chain`. |
| `memory_write_failed` | Write rejected; `hostEffect` says whether any byte changed. | Check the region; change protection only with consent; re-read before any retry. |
| `limit_exceeded` | A size, count or result bound was exceeded. | Lower `limit` or `size`, page, narrow the range or module. |
| `partial_effect` | A composite operation stopped midway; some steps applied. `details` holds progress such as `completed`, `failedIndex`, applied or created ids, `rolledBack`. | Report exactly what applied, inspect, then finish or undo explicitly. Never success. |
| `stopping` | The plugin activation is being disabled or has expired. | Stop; call `instance_list` later; the id changes after re-enable. |
| `instance_unavailable` | The gateway could not reach or verify the instance: unknown or stale id, identity mismatch, connection lost. | `instance_list`; never redirect to another instance. With `hostEffect: unknown` the call may have run. |
| `internal` | Unexpected fault or indeterminate host result. | Report `operation` and `message`, inspect state, check the plugin log; never treat it as "absent". |

## Host effects

| hostEffect | Implies | Do |
| --- | --- | --- |
| `not_started` | Nothing reached the host. | Fix the cause and call again. |
| `not_applied` | The host was reached and confirmed no change. | Safe to call again. |
| `started` | Work began and may be partly applied. | Inspect, then finish or undo explicitly. |
| `completed` | The host action finished; the failure came afterwards (verification, result copy). | Treat the change as applied; verify with a read. |
| `cleanup_unconfirmed` | A release or cleanup ran but could not be confirmed. | Treat the resource as possibly alive; list and verify; manual recovery may be needed. |
| `unknown` | The outcome cannot be known (timeout, lost connection, unexpected fault). | Assume it may have happened; read state before anything else. |

`retryable: true` appears only for `busy` or `cancelled` with `not_started` or `not_applied`, or for a release whose outcome is retryable. `false` does not mean impossible; it means fix something or inspect state first. Retry at most once after the stated condition changes; never loop.

## Timeouts and lost connections

- The gateway waits 45 s per call by default (`--call-timeout-seconds`, `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`), then returns `timeout` with `hostEffect: unknown`. The backend may still be running the operation or may have finished it.
- The gateway never resends a call to any instance.
- After an unknown outcome, read the state on the same instance (`memory_read`, `record_get`, `asm_list_patches`, `runtime_list_resources`, `runtime_get_overview`) and decide with the user. Polls are safe: repeat them with the same `afterSequence`.

## Instance unavailable

- Causes: CE closed, plugin disabled or re-enabled, a stale registry record, an identity mismatch after PID reuse, or a backend that stopped answering.
- Call `instance_list`. `discoveryIncomplete: true` means call it again. An instance that returns under a new id is a new activation: re-run orientation and discard every old id.
- Never move the work to another instance, even one with the same name. Report the outage instead.

## Stale identifiers

| Event | Invalidated | Recover with |
| --- | --- | --- |
| CE restart, plugin disable and re-enable | `instanceId`, job, patch, allocation, symbol, scanner, pointer-map and pointer-scan ids | `instance_list`, then the list tools on the new instance |
| `table_load` | Every record id (`not_found`: the id predates the last table load) | `record_list` or `record_find` |
| Target switch or target restart | Absolute addresses, pointer bases, capture and trace results, structure base addresses | AOB signatures, `pointer_read_chain`, `pointer_rescan_paths` |
| Main scan tab change or a manual New Scan in CE | `main` scanner results | `scan_get_status`, then scan again |

## Releasing owned resources

- `runtime_list_resources` lists what this activation owns (allocations, patches, independent scans, registered symbols, jobs, MCP host state) with bounded recovery descriptors.
- `runtime_release_resources` releases newest first, stops at the first incomplete release and keeps retryable entries. An incomplete run returns `partial_effect` whose `details` list what was released, the failing resource and how many remain.
- Each release outcome says whether it completed, can be retried later, or needs manual recovery. Retry only retryable ones; never skip past a failing entry.
- Resources tied to a process that has changed, exited or lost its identity cannot be released automatically. Tell the user what to undo in CE by hand: disable the script or record, delete the breakpoint, reset the speed, unpause, remove the registered symbol. Allocations in an exited process vanish with it.
- Switching the process in CE's own UI instead of `process_attach` leaves MCP resources bound to the old process; expect refusals and manual recovery.

## After a plugin disable or re-enable

- Disable stops admission, then the Client drains its leases (allocations, patches, independent scans, symbol registrations). A lease whose cleanup failed at disable may need manual recovery in CE.
- CE-owned state survives: the address list and freezes, structures, the main scan tab, breakpoints, speedhack, pause, dissect data and the Mono collector. Lua-side jobs keep running until their TTL.
- State the plugin created outside the Client (jobs, speedhack not at 1, pause, MCP breakpoints, Mono hooks) is recorded in CE's Lua state. The next activation reports it as `orphaned: true` in `runtime_list_resources`, in the `busy` details of a refused target change, and in `runtime_release_resources`. Release it, or acknowledge a recorded `cleanupError`, before switching targets.
- Ids from the previous activation never work in the new one.

## Quick reference

- Empty `instance_list`: [connection-troubleshooting](connection-troubleshooting.md).
- `busy` on `process_attach`: run [cleanup_session](workflows/cleanup-session.md), then attach.
- `not_found` for a job: it expired (120 s default, 300 s cap for target jobs); start a new one.
- `dropped` above zero in a poll: poll sooner or narrow the job; lost items are not recoverable.
- `memory_read_failed` on a saved pointer: the chain broke; re-find the base ([pointers](pointers.md)).
- Anything with `hostEffect: unknown`: read first, decide second.
