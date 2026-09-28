# Explain a failure and recover safely

## Goal

Explain in plain words the `{kind}` failure of `{tool}` (host effect `{hostEffect}`), find out with read-only checks
what, if anything, changed, and agree one safe next step with the user. Never repeat a side effect to see what happens.

## Steps

1. Read the failed result: `kind`, `message`, `hostEffect`, `retryable`, `hint`, `operation` and `details`. Quote the
   message and follow the hint. When `{tool}` or `{hostEffect}` is not given, take it from the result.
2. Read the kind and host effect guidance under Decisions.
3. Inspect, read-only and on the same instance, the state `{tool}` touched:
   - process: `process_get_current()`;
   - memory: `memory_get_address_info(addresses=["<address>"])`,
     `memory_read(address="<address>", valueType="bytes", size=16)`;
   - scans: `scan_get_status(scannerName="main")` or the named scanner;
   - patches and code: `asm_list_patches()`, `code_disassemble(address="<address>", count=5)`;
   - records: `record_get(ids=[...])`;
   - debugger: `debugger_get_status()`, `debugger_list_breakpoints()`;
   - speed and Mono: `speedhack_get_state()`, `mono_get_status()`;
   - pointer maps and scans: `pointer_list_maps()`, `pointer_list_scans()`;
   - jobs and owned state: `runtime_list_jobs()`, `runtime_list_resources()`;
   - gateway: `instance_list()`.
4. Tell the user what failed, what the checks show changed, and the next step. Repeat the call at most once, with
   consent for a change, and only when `retryable` is true or nothing changed and the cause is fixed.

## Decisions

Both sections below apply: the kind says why the call failed, the host effect says what it changed.

### kind: invalid_argument

Rejected before anything ran. `details.parameter` names the argument, sometimes an item such as `patterns[2]`: a bad
expression, type, range or option mix, an ambiguous pattern or name, an existing file without overwrite; at the
gateway, a missing `instanceId` or an unknown tool. Fix it from the schema and the hint, then call again once.

### kind: invalid_state

Cheat Engine or the target forbids it now: the game paused or stopped in the debugger, no attached process for a pause
or speed change, a scanner with no results to read or narrow, main already holding a scan (`scan_reset` first), a named
scanner whose target or CE runtime changed (it accepts only `scan_delete`), an active script record whose script would
change (deactivate it first), a name in use, a record id from before the last table load (list the records again), or
a changed CE runtime (disable and re-enable the plugin). For a `mono_` tool: Cheat Engine's Mono timeout dialog is open
(the user answers it), or the collector pipe failed. Follow the hint: repeat once when it says the next call reconnects;
otherwise `mono_detach()`, and when `mono_detach` reports `not_found` or `mono_detach.alreadyEnded` true, Cheat Engine
owns the attachment and the user re-activates Mono in its Mono menu; attach again only with consent. Read the state,
fix the precondition with the user, then call again once.

### kind: not_found

The id or name is unknown or expired: a stopped or expired job, a released patch, a deleted record, scanner, structure
or snapshot, an id from an earlier plugin activation, a symbol this activation did not register, a breakpoint or Mono
attachment MCP does not own, or no managed object at an address. It never means success. List again
(`record_list(offset=0, limit=100)`, `runtime_list_jobs()`, `asm_list_patches()`, `symbol_list_registered(offset=0,
limit=200)`) and use a current id. A job ends with its lifetime (`Mcp:Execution:JobDefaultTtlSeconds`, 120 s by default,
unless its start call set one; 300 s at most); a lost job is started again.

### kind: not_attached

No process is selected; for a `mono_` tool, Cheat Engine's Mono collector is not attached to it, or Cheat Engine
aborted its connection (follow the hint: when `mono_detach` reports `not_found` or `mono_detach.alreadyEnded` true, the
user re-activates Mono in Cheat Engine's Mono menu); for a `dotnet_` tool, the .NET data collector is unavailable.
Check `process_get_current()`, `mono_get_status()` or `dotnet_get_status()`. Attaching is a consented step:
[attach and orient](attach-and-orient.md) or [Unity (Mono) recon](unity-mono-recon.md).

### kind: busy

Something competes: the concurrent dispatch limit (`Mcp:Execution:MaxConcurrentDispatches`, 4 by default), the job
limit (`Mcp:Execution:MaxJobs`, 16), a running main scan, a snapshot or rescan in progress, a game paused or stopped at
a breakpoint (code calls, .NET reads), or owned resources that block a target change (orphans included) or
`debugger_detach` (the error names them). Wait for the running work, or with consent stop it
(`scan_stop(scannerName="main")`, `runtime_stop_job(jobId="...")`) or [clean up](cleanup-session.md), then repeat once.

### kind: timeout

At the gateway: its call timeout (45 s by default) passed before the backend answered (host effect `unknown`). The
work may still be running or may have completed, and the gateway never resends it. Inspect on the same instance;
never repeat a mutation. Only the user raises the limit (`--call-timeout-seconds` or
MCP_GATEWAY_CALL_TIMEOUT_SECONDS, 5 to 3600). A bounded read that passed its dispatch budget fails with `completed`:
nothing changed; narrow it and read again.

### kind: cancelled

The cancellation was observed before the call completed. With `not_started` or `not_applied` it is retryable: repeat
once if the user still wants it. Otherwise inspect first.

### kind: capability_disabled

A setting named in the message is off: Mcp:EnableUnsafeLua, Mcp:EnableAutoAssembler,
Mcp:EnableTargetCodeExecution or Mcp:EnableKernelAccess; `runtime_get_info.gates` shows all four. Nothing ran. Some
tools need one only for some arguments or content: the veh or kernel debugger (also when Cheat Engine's setting picks
it), script records, a table's content, an attach while the table sets Uses Mono. Only the user changes it, in
appsettings.json followed by a plugin disable and re-enable, which ends every id and owned resource:
[clean up](cleanup-session.md) first. Never work around a gate; offer an ungated route.

### kind: unsupported

This Cheat Engine, target or runtime cannot do it: bitness, a missing CE feature, extension or Client capability, an
unknown pointer size, a debugger interface MCP cannot drive (DBVM, ceserver), a generic or array object for
`mono_get_object` (or an IL2CPP image whose classes the collector cannot list). An Auto Assembler check is refused for
a script that needs a switch beyond Mcp:EnableAutoAssembler (Lua, C code, loadlibrary and the like), uses globalalloc
or writes `$` before anything but hex digits, because Cheat Engine runs parts of a script while checking it:
[review it](review-aa-script.md) by reading. `runtime_get_info()` lists each capability with `isAvailable` and
`reason`. Do not retry; choose another approach.

### kind: target_changed

The selected process changed, or its identity could not be confirmed, during the call. `process_get_current()`,
confirm the target with the user, re-derive addresses from module offsets, signatures or pointer chains, and repeat
reads only. Resources bound to the old process may need manual recovery (`runtime_list_resources()`).

### kind: host_refused

Cheat Engine refused, or its Lua or Auto Assembler reported an error; `message` has CE's text. Fix the input; for a
script, [review it](review-aa-script.md). After `started` or `unknown`, inspect before any retry. The debugger's
precondition checks ("Debugger is not attached", "Debugger has no stopped context", a capture or trace started while the
target is stopped or another trace runs) report `started` although nothing changed: read `debugger_get_status()` and
`debugger_list_breakpoints()`, fix the precondition (attach, `debugger_continue()`, end the other trace), then call
again once.

### kind: memory_read_failed

The address is unreadable: unmapped, a guard or no-access page, a freed object or a broken pointer hop.
`memory_get_address_info(addresses=["<address>"])` shows the region; for `pointer_read_chain`, `details.hopIndex`
names the broken hop. Re-find the address, perhaps with a [pointer scan](pointer-scan.md).

### kind: memory_write_failed

A write was rejected; the host effect says whether any byte changed. Check `memory_get_address_info.region`
protection and read the bytes back with `memory_read(address="<address>", valueType="bytes", size=16)`. Change
protection with `memory_set_protection` only with consent, and restore its `previous` access afterwards. A batch
reports `details.completed` and `details.failedIndex`.

### kind: limit_exceeded

A size, count or result bound; `details.parameter` names it. Lower the limit or size, page with offset, or narrow the
range. Caps such as 32 named scanners, 16 snapshots, 4 pointer maps or 128 allocations free up when the user agrees to
delete unused ones.

### kind: partial_effect

Some steps applied, never a success. `details` says which: completed items, `failedIndex`, created and rolled-back
records, or a release with `isRetryable` and `requiresManualRecovery`; `runtime_release_resources` lists `released`,
`failed` and `remaining`. Repeat once only when retryable. Otherwise report what applied, inspect, then finish or undo
explicitly with consent. A leftover the user recovers by hand stays listed until the end of
[cleanup](cleanup-session.md): `runtime_release_resources(acknowledgeIds=["<id>"])` also releases everything else.

### kind: stopping

The plugin activation is stopping or has ended. Stop. Later `instance_list()` gives a new id; every old id is void,
and `runtime_list_resources()` may show `orphaned` entries. Orient again with
[attach and orient](attach-and-orient.md).

### kind: instance_unavailable

Gateway only: a stale instance id, or the instance could not be reached, verified or kept connected. `instance_list()`;
when `discoveryIncomplete` is true, call it again. Never move the work to another instance, even one with the same
name ([instance unavailable](../Documents/errors-and-recovery.md#instance-unavailable)).

### kind: internal

An unexpected fault or an indeterminate host result: never read it as absence. Report `operation` and `message`,
read the state, and give the user `details.errorId` (`error.data.errorId` for a resource read). Before any change,
they search that id in the plugin log of that Cheat Engine instance (`CheatEngine.Mcp.<CE pid>.log`, by default in
`%APPDATA%\CheatEngine.Mcp`); the entry names the tool or resource, the operation and the exception type.

### hostEffect: not_started

Nothing reached Cheat Engine; nothing changed. Fix the cause, then call again once.

### hostEffect: not_applied

Cheat Engine was asked but applied nothing. Fix the cause, then call again once.

### hostEffect: started

Work began and may be partly applied. Inspect (step 3), show the user, then finish or undo explicitly. Never repeat
the call blindly: only when the inspection shows nothing changed and the cause is fixed.

### hostEffect: completed

The host work finished and the failure came afterwards (checking or copying the result, a later step). A read
changed nothing: narrow it and read again. A change is applied: verify it with a read and do not repeat it.

### hostEffect: cleanup_unconfirmed

The work or its release ran, but the cleanup is unconfirmed: treat the resource as alive. `runtime_list_resources()`
shows `requiresManualRecovery` and `cleanupError`; the user undoes it in Cheat Engine. Acknowledge it only at the end
of [cleanup](cleanup-session.md), since `runtime_release_resources(acknowledgeIds=["<id>"])` releases the rest too.

### hostEffect: unknown

Whether anything ran is unknown (a gateway timeout, a lost connection, a fault). Assume it happened: read the state
before anything else, and never repeat a mutation.

### hostEffect: default

Take `hostEffect` from the failed result; when there is none, treat it as unknown.

## Pitfalls

- `retryable` true means the identical call may succeed later as it is (busy or cancelled with nothing applied, or a
  release that did nothing yet). False means fix or inspect first, not impossible.
- Some tools succeed yet report a failure in their result: check the error of each `memory_get_address_info.items`
  entry, `asm_release_patch.released`, and each `record_set_active` entry whose `active` differs from
  `requestedActive` (`failure.reason`).
- A resource or prompt read fails with a JSON-RPC error whose data carries the same `kind` and `hostEffect`, without
  `details`.
- A `busy` refused target change is solved by releasing the blockers, not by retrying.
- Never switch instance or target, and never loop, to make a failure go away.

## Report

The failure in one line (`{tool}`, `{kind}`, `{hostEffect}`), what the checks show changed, the next step and whether
the user agreed, and anything left for manual recovery. See [errors and recovery](../Documents/errors-and-recovery.md)
and [session rules](../Documents/workflows.md).
