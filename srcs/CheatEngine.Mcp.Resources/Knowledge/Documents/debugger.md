# Debugger methods

Use Cheat Engine's (CE) debugger to learn which instruction writes or reads a value, which addresses an instruction
touches, to stop a thread and read its registers, or to trace the path that decides an outcome. Read this page before
attaching and before you finish a task that used the debugger. Prefer a scan ([value scans](value-scans.md)) or plain
disassembly ([code analysis](code-analysis.md)) when they answer the question: they need no debugger.

Responsible use: attach only to single-player or offline software the user owns or may modify, never to online,
competitive or anti-cheat-protected titles ([safety](safety.md)). A debugger changes the target's timing, a stopped
thread freezes it, and a wrong breakpoint or register write can crash it and lose unsaved progress. Explain the effect
and get consent before the attach, before breakpoints that stop threads or write code bytes, and before register writes.

## Tools at a glance

| Goal | Tools |
|---|---|
| Attach, inspect, detach | `debugger_attach`, `debugger_get_status`, `debugger_detach` |
| Find what writes, reads or executes; which addresses an instruction accesses | `debugger_start_capture`, `debugger_poll_capture`, `runtime_stop_job` |
| Breakpoints | `debugger_set_breakpoint`, `debugger_run_to`, `debugger_list_breakpoints`, `debugger_delete_breakpoint` |
| Threads | `debugger_break_thread`, `debugger_set_thread_ignored`, `debugger_continue`, `debugger_step` |
| Stopped thread | `debugger_get_context`, `debugger_set_register`, `debugger_get_stack_trace` |
| Step trace | `debugger_start_trace`, `debugger_poll_trace`, `runtime_stop_job` |

Live resources mirror two tools: `cheatengine://instance/debugger` (`debugger_get_status`) and
`cheatengine://instance/debugger/breakpoints` (`debugger_list_breakpoints`); through the gateway they sit under
`cheatengine://instances/{instanceId}/debugger`. Jobs and breakpoints belong to one CE instance.
First call `debugger_list_breakpoints` explicitly; its breakpoint resource reuses that preparation for five seconds.
Refresh after breakpoint changes. Missing or expired preparation returns `invalid_state`.

## Interfaces and gates

| `interface` | What CE does | Gate | Notes |
|---|---|---|---|
| `default` | The interface selected in CE's settings (Windows unless the user changed it) | That interface's gate | MCP reads the setting first. Read `activeInterface`: it can be VEH or kernel. |
| `windows` | Windows debugging API | none | Most compatible. Every debug event suspends all threads until CE continues. Clean detach. |
| `veh` | Injects CE's VEH helper DLL into the target | `Mcp:EnableTargetCodeExecution` | The helper stays after detach: the target must restart before any new attach. |
| `kernel` | CE's DBK driver handles debug events | `Mcp:EnableKernelAccess` | Hardware breakpoints only (no int3, no page exceptions). Needs DBVM on 64-bit Windows. |

- Prefer `debugger_attach(interface="windows")` unless the user chose another interface. A disabled gate returns
  `capability_disabled` (`hostEffect` `not_started`) before anything runs.
- `default` always reads CE's setting first. It refuses with `unsupported` (`not_started`) when the setting is the DBVM
  debugger or cannot be identified (a GDB server, for example) or CE is connected to a ceserver, and with
  `capability_disabled` when the selected VEH or kernel interface needs a switch that is off. MCP never drives the
  DBVM debugger, whatever the switches.
- Any interface: while CE is connected to a ceserver the attach is refused (`unsupported`) before CE attaches. A
  debugger already attached through DBVM, a GDB server or ceserver is reported as `unsupported` (`not_started`), and
  `debugger_get_status()` shows `stateValid` false with an `error`. If CE's attach itself lands on such an interface,
  the error is `unsupported` with `hostEffect` `completed`: the debugger stays attached, so run `debugger_detach()`.
- Already attached: requesting `default` or the active interface only reports it (`alreadyAttached` true); another
  interface is refused (`host_refused`). MCP never detaches and reattaches for you. `usedFallback` is true when CE
  activated another interface than the one you named.
- VEH guard: once a process incarnation used VEH (an explicit `veh` request, even a failed one, or a `default` attach
  that became VEH), MCP refuses every new attach to it, whatever the interface, until the target restarts. The marker
  lives in CE's Lua state, so it survives a plugin reload. Restart the target and discard old addresses; never restart
  CE to get around it.
- Kernel on 64-bit Windows: CE loads DBK without asking, then, if DBVM is not running, shows a DBVM warning a human
  must answer; DBVM can freeze or blue-screen the machine. On default Windows 11 memory integrity and the
  vulnerable-driver blocklist normally block both; never suggest disabling them ([kernel](kernel.md)). Afterwards CE's
  help says to run its kernel-module unloader or reboot before another interface works.
- The attach may wait on a CE dialog; after a `timeout` read `debugger_get_status()`, never attach again blindly. If
  the target exits, freezes or complains on attach, stop and report; never switch interfaces to get around it.

## Breakpoints: slots, methods and sizes

The CPU has four debug-register slots (DR0-DR3), shared by execute and data breakpoints on every CE interface.
Captures, run-to and trace entries, a step over a `call`, hardware breakpoints from `debugger_set_breakpoint` and the
user's own CE breakpoints all compete for them. Check `debugger_list_breakpoints()` (addresses, not methods) and
`runtime_list_jobs()` first.

| `debugger_set_breakpoint.method` | Effect |
|---|---|
| `default` | CE's preferred method, hardware unless the user changed it. Captures, traces, run-to and step-over always use it. |
| `hardware` | A debug-register slot; code bytes stay untouched. |
| `int3` | Execute only (a data trigger is `invalid_argument`), unlimited. Writes `CC` over the first byte while set: integrity checks, AOB scans and `memory_read` see it, and a mid-instruction address crashes the target, so take it from a capture, a trace or forward disassembly. |
| `page_exception` | Every access to anything on the page traps: very slow. For an execute breakpoint on a 32-bit target without DEP, CE shows a dialog asking to enable DEP. |

- With all four slots taken, CE silently turns a further `default` or `hardware` execute breakpoint into int3 on the
  Windows and VEH interfaces (the result still shows the method you asked for) and refuses a further data breakpoint
  (`host_refused`). The kernel interface has no int3, so it refuses the execute breakpoint too.
- Size is 1, 2, 4 or 8 (checked even for execute, which ignores it). A data address must be a multiple of the size,
  and 8 needs a 64-bit target. MCP refuses a misaligned range (`host_refused`) that CE's own dialogs would split across
  slots. Watch instead the first byte, which every full write touches
  (`debugger_start_capture(address="<value address>", size=1)`), an aligned 8-byte range holding the whole value on a
  64-bit target (it also reports neighbouring bytes), or several aligned pieces, one slot each.

| `trigger` | Fires on | Reported `ip` |
|---|---|---|
| `write` | Any write touching the range, even of the same value | The next instruction (trap) |
| `access` | Reads and writes: x86 has no read-only watch | The next instruction (trap) |
| `execute` | Before the instruction runs | The instruction itself (fault) |

## Data hits report the next instruction

A data breakpoint traps after the accessing instruction completed, so `ip` is the following instruction. MCP decodes
one instruction backwards for `instructionAddress` (omitted when CE cannot decode one) and sets `isHeuristic` to true
(false for execute hits).

- Confirm with `code_disassemble(address="<instructionAddress>", count=2)`: the second instruction must start exactly
  at `ip`, and the candidate needs a memory operand that, computed from `registers`, reaches the watched address. If
  `ip` is a jump target, the instruction before it in memory may not be the one that ran. A `rep movs` or `rep stos`
  at `ip` itself may be the accessor, trapped between iterations.
- Data-hit registers are the state after the instruction. For `mov [rcx+10],eax` the base survives: `RCX`, offset
  `10`. For `mov rax,[rax+8]` it was overwritten: capture that instruction with an `execute` trigger, grouped by
  address (see below), to get the state before.

## Captures: find what writes, reads or executes

A capture's breakpoint records each hit and lets the thread continue at once, so the game keeps running. Start it
while no thread is stopped.

1. `debugger_start_capture(address="game.exe+1A2B40", trigger="write", size=4, aggregateByInstruction=true)`; keep
   `jobId`. Defaults: `write`, size 4, `maximumHits` 256 (1 to 1024, at most `Mcp:Execution:JobBufferLimit`, so a
   lower limit needs an explicit value), and a lifetime of `Mcp:Execution:JobDefaultTtlSeconds` (120 s; at most
   `Mcp:Execution:JobMaxTtlSeconds`, 300 s).
2. Ask the user to make the value change in the game.
3. `debugger_poll_capture(jobId="...", afterSequence=0)`, then pass `nextAfterSequence` back while `more` is true.
   Polls consume nothing; `dropped` counts the oldest items the bounded buffer evicted.
4. `runtime_stop_job(jobId="...")` removes the breakpoint and discards the results, as TTL expiry does: poll first.

- Each item of `hits` has `hitCount` and a `context`: `ip`, `threadId`, `instructionAddress`, `isHeuristic`,
  `disassembly`, `stackPointer` and `registers` (general-purpose registers and `EFLAGS` only, no XMM).
- With `debugger_start_capture.aggregateByInstruction` true, an item is one instruction with `firstContext`,
  `lastContext` and a growing `hitCount`, and `maximumHits` bounds the instructions. Items update in place: polling
  again with `debugger_poll_capture(jobId="...", afterSequence=0)` reads current counts; `job.progressDone` counts all
  hits. Use per-hit mode when order or thread matters.
- The writer is usually the item whose count rises when the user acts; several writers (set, add, clamp, copy) are
  common. Every hit is a debug event that makes all threads wait on the Windows interface: on values touched every
  frame keep captures short and aggregated.
- Capture, trace and run-to breakpoints show in `debugger_list_breakpoints` with `owned` false: release them with
  `runtime_stop_job`, not `debugger_delete_breakpoint`.
- Next: the base register leads to a pointer ([pointers](pointers.md)); the instruction is an injection point
  ([x64 injection](x64-injection.md), [auto-assembler](auto-assembler.md)); grouping an execute capture by address
  gives one base per entity ([structures](structures.md)). Guided: [find a writer](../Workflows/find-writer.md),
  [filter shared code](../Workflows/shared-code-filter.md),
  [manual pointer chain](../Workflows/manual-pointer-chain.md).

## Addresses one instruction accesses

CE's "Find out what addresses this instruction accesses" is an execute capture grouped by the address that the
instruction's memory operand reaches: it splits shared code by entity, even when the instruction overwrites its base
register. Take the instruction from a confirmed `instructionAddress` or from disassembly, run
`debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true, maximumHits=64)`,
let the game run briefly, poll as above, then `runtime_stop_job`.

- Each item is one accessed address: `effectiveAddress` (uppercase hex without `0x`), `hitCount`, `firstContext`,
  `lastContext` and `context`, plus `operandSize` in bytes when CE's disassembly states it (`dword ptr` gives 4).
  `debugger_start_capture.maximumHits` caps the addresses: a new one evicts the oldest (counted in `dropped`).
- MCP computes the address from the registers before the instruction runs. It wraps to 32 bits on a 32-bit target,
  for 32-bit registers and after a `67` address-size prefix. For `mov eax,[rcx+10]`, each entity's base is
  `effectiveAddress` minus `10`.
- It needs the `execute` trigger and excludes `debugger_start_capture.aggregateByInstruction` (`invalid_argument`).
  Refused before any breakpoint (`not_started`): an undecodable instruction, `lea`, `nop` or no explicit `[...]`
  operand (`invalid_argument`); several memory operands, `fs:` or `gs:`, vector-indexed (VSIB) operands, a symbol CE
  cannot resolve, 16-bit addressing on a 32-bit target (`unsupported`). A hit lacking an operand register fails the
  job.

## Stopping a thread

- `debugger_set_breakpoint(address="game.exe+1A2B40", trigger="execute", oneShot=true)` stops the first thread that
  hits it, then removes itself. Otherwise every hit stops until `debugger_delete_breakpoint`, so on hot code a continue
  runs straight into it again; `debugger_set_breakpoint.threadId` limits it to one thread. MCP tracks it as a
  `breakpoint` resource (`resourceId`).
- `debugger_delete_breakpoint(address="...")` removes only MCP-owned breakpoints (a user's returns `not_found`).
  `debugger_list_breakpoints()` marks them with `owned` and `resourceId`; leave the user's alone.
- After a hit `debugger_get_status()` reports `broken` true, proven by reading the context (`reportedBroken` is CE's
  own flag). `stateValid` false comes with an `error` text.
- `debugger_run_to(address="...")` needs a stopped thread: it arms a one-shot execute breakpoint for all threads and
  continues, and the first thread to reach the address stops. It is a `debugrunto` job without a poll tool:
  `runtime_list_jobs()` shows it `completed` once hit; `runtime_stop_job` releases it and a breakpoint never hit.
- `debugger_break_thread(threadId=...)` only submits a request (ids from `process_list_threads`; `requested` is true
  even for an unknown id); a waiting thread breaks only after it wakes. Poll `debugger_get_status()`.
- `debugger_set_thread_ignored(threadId=..., ignored=true)` makes CE ignore breakpoints on a noisy thread; undo it
  with `debugger_set_thread_ignored(threadId=..., ignored=false)`.
- A stopped thread usually freezes the game: after about five seconds without message processing Windows marks the
  window "Not Responding" and game watchdogs may fire; exclusive fullscreen hides CE, so ask for windowed mode. Until
  you continue, Mono collector tools (`mono_attach`, `mono_get_object` and the others), a `speedhack_set_speed`
  change and `process_set_paused` refuse with `invalid_state`, and `exec_call_remote`, `exec_call_method` and
  `structure_fill_from_dotnet` with `busy`; they test CE's own flag (`reportedBroken`).

## Registers and context

- `debugger_get_context()` maps CE's register names, uppercased, to uppercase hex without `0x`; `is64Bit` tells the
  target's width. `debugger_get_context(includeExtraRegisters=true)` adds FP0-FP7 (10 bytes) and XMM0-XMM15 (16 bytes;
  XMM0-XMM7 on 32-bit) where CE supplies them, as spaced hex bytes, least significant first. Decode a 4-byte float
  lane with `util_convert_value(value="00 00 80 3F", sourceType="bytes", targetType="float")` (gives 1).
- x64 Windows calls pass the first four arguments by position in RCX, RDX, R8, R9 (`this` in RCX) or XMM0-XMM3
  (floats) and return in RAX or XMM0; 32-bit code mostly uses the stack (`this` in ECX for MSVC methods). Read
  arguments with an execute breakpoint on the function's first instruction.
- `debugger_set_register(register="RAX", value=<decimal>)` writes a general-purpose register (`EAX`...`EIP`,
  `RAX`...`RIP`, `R8`...`R15`) or `EFLAGS` of the stopped thread and reads it back (`verified`). The value is a signed
  decimal integer; -1 sets all bits. On x64 a 32-bit name writes the whole register, zero-extended; R names are
  refused on 32-bit targets. No tool writes FPU or XMM registers.
- A new `RIP` must be a verified instruction boundary. Flipping a flag at a stopped conditional jump (CF 0x1, ZF 0x40,
  SF 0x80, OF 0x800) tests a branch hypothesis for one run without patching:
  `util_calculate(expression="0x<EFLAGS> ^ 0x40")`, then pass its `signedValue` to
  `debugger_set_register(register="EFLAGS", value=<signedValue>)`.
- `debugger_get_stack_trace(depth=32)` (1 to 128 slots) keeps stack values that follow a `call`. Every frame is
  heuristic (stale data, stored code pointers, tail calls); one is likely real when its `callInstruction` directly
  calls the `startAddress` of `code_get_function(address="<address inside the callee>")`.

## Stepping and traces

- `debugger_step(mode="over")` treats a `call` or `rep` instruction as one step (a temporary thread-only execute
  breakpoint after it, a hardware slot by default); `debugger_step(mode="into")`, the default, follows calls. A callee
  that never returns, or another breakpoint inside it, keeps the step from ending there.
- `debugger_step` and `debugger_continue()` return once CE accepted the request. After a step, poll
  `debugger_get_status()` until `broken` is true before reading the context again.
- `debugger_start_trace(address="game.exe+1A2B40", mode="over", maximumSteps=64, stopCondition="IP=7FF6A1B2C3F0")`
  arms an execute breakpoint (a hardware slot by default, until hit), then single-steps only the thread that hits it,
  recording one context per step, entry included, up to `debugger_start_trace.maximumSteps` (default 32, at most 256).
  Start it while the target runs; the code must reach the address before the TTL. `debugger_start_trace.includeStack`
  adds `stackPointer`.
- One trace at a time: once its entry is hit, a trace owns CE's global `debugger_onBreakpoint` hook. A start is
  refused (`host_refused`) while any such hook exists, including one set with `lua_execute` ([Lua](lua.md)), and a
  trace whose entry is hit while another hook is installed fails.
- `debugger_start_trace.stopCondition` is optional: without it the trace runs all its steps; with it, the trace also
  ends after the first matching context, the entry included. Grammar: `REGISTER=HEX` (general-purpose register or
  `EFLAGS`) or `IP=HEX`, 1 to 16 hex digits, optional `0x`, at most 64 characters, compared as a number (`RAX=001A`
  matches 1A). Name a register of the target's width, `RAX`-`R15` or `RIP` on x64 and `EAX`-`EIP` on x86 (`IP` and
  `EFLAGS` fit both); another width is refused before any breakpoint (`invalid_argument`). Aim `IP=` at the
  instruction where the trace should end (the conditional jump, the `ret`) as an absolute address
  (`symbol_resolve(expressions=["game.exe+..."])`).
- `debugger_poll_trace(jobId="...", afterSequence=0)` returns `steps` (capture context fields, `isHeuristic` false).
  The trace ended when `job.state` leaves `running`: `completed`, or `failed` with `job.error`.
- Completion leaves the traced thread stopped, possibly one step past the last recorded context (read in CE's source,
  not tested live). In every job state clean up in this order: poll everything and read `debugger_get_context()` if the
  position matters; `runtime_stop_job(jobId="...")`, which removes the hook and the entry breakpoint but does not
  resume the thread; then `debugger_get_status()` and, if `broken` is true, `debugger_continue()`. A trace that is
  still stepping keeps its hook, so stop it before you continue.
- Trace `over` first, then into the call that matters: `into` spends the budget in callees and system code. Look for
  the flag-setting instruction (`cmp`, `test`, `comiss`) before the deciding jump and compare registers across runs.
  Guided: [trace logic](../Workflows/trace-logic.md).

## Jobs, errors and recovery

- Debugger jobs are `debugcapture`, `debugtrace` and `debugrunto`. An activation retains at most
  `Mcp:Execution:MaxJobs` jobs of any kind (16 by default); a further start fails with `busy`, so stop a job first. A
  finished job stays pollable until its TTL; after `runtime_stop_job` or expiry a poll returns `not_found`. A page
  holds 1 to 1000 items.
- Preconditions are checked inside CE and fail as `host_refused` with `hostEffect` `started` and `retryable` false,
  the check as message: "Debugger is not attached", "Debugger has no stopped context", "Continue the stopped context
  before starting a capture" (or trace), "Another debugger_onBreakpoint hook is installed", an address that "could not
  be resolved" or must be "aligned to its size", "A different debugger interface is already attached", the VEH guard.
  Nothing changed yet, but inspect first: read `debugger_get_status()` (and `debugger_list_breakpoints()` for
  breakpoint tools), fix the precondition (attach, `debugger_continue()`, stop the other trace), then call again. After
  any other message, a `timeout` or `hostEffect` `unknown`, inspect the same way and never repeat a change blindly.
- Out-of-range arguments fail as `invalid_argument` or `limit_exceeded` before anything runs. `debugger_attach` also
  reports `unsupported` when CE did not report the process start time that the VEH guard needs.
- An unconfirmed `debugger_delete_breakpoint` is `partial_effect` (`cleanup_unconfirmed`): check
  `debugger_list_breakpoints()` and, with the user, remove a leftover in CE. The entry stays in
  `runtime_list_resources()`. Acknowledge it only at the end of cleanup and with consent, because
  `runtime_release_resources(acknowledgeIds=["<resourceId>"])` also releases everything else MCP holds.
- A plugin disable runs each job's stop hook once. MCP-set breakpoints and jobs whose cleanup failed stay in CE and
  show as orphans in `runtime_list_resources()` after re-enable; with consent, release them with
  `runtime_release_resources(includeOrphans=true)`, which releases everything else too. See
  [errors and recovery](errors-and-recovery.md).

## Leave the target running

Before you end a turn, switch process or finish the task:

1. Poll what you still need, then `runtime_stop_job` every capture, trace and run-to job (`runtime_list_jobs()`).
2. `debugger_delete_breakpoint` each MCP-owned breakpoint; undo `debugger_set_thread_ignored`. Delete before you
   continue: an owned breakpoint on hot code would stop the thread again.
3. `debugger_get_status()`: if `broken` is true, `debugger_continue()`. Never end a turn with a stopped thread unless
   the user asked for it.
4. `debugger_detach()` when nothing else needs the debugger. It resumes a stopped thread and unpauses the target, but
   refuses with `busy`, naming the resource, while anything this MCP activation tracks still holds target state
   (breakpoint, job, patch, allocation, named scan, symbol, speedhack, MCP's pause, Mono attachment). Ended jobs and
   breakpoints no longer block. To keep a patch, leave the debugger attached and say so.
5. Do not close CE while breakpoints are armed: a debug register or `CC` byte left behind crashes the target at its
   next hit.

Report the interface, what stays attached or active, and how to undo it. See also
[session cleanup](../Workflows/cleanup-session.md) and [glossary](glossary.md).

## Sources

- C:/Program Files/Cheat Engine/celua.txt (debugProcess, debug_setBreakpoint, debug_getContext, debug_breakThread,
  debugger_onBreakpoint, createDisassembler)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/debughelper.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/debugeventhandler.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/KernelDebuggerInterface.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/VEHDebugger.pas
- https://wiki.cheatengine.org/index.php?title=Help_File:Debugger_options
- https://wiki.cheatengine.org/index.php?title=Help_File:Find_out_what_writes/accesses_this_address
- https://wiki.cheatengine.org/index.php?title=Help_File:Find_out_what_addresses_this_instruction_accesses
- https://en.wikipedia.org/wiki/X86_debug_register
- https://learn.microsoft.com/windows/win32/debug/debugging-events
- https://learn.microsoft.com/cpp/build/x64-calling-convention
- https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-ishungappwindow
