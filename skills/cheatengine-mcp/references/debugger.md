# Debugger methods

Use the debugger to learn which instruction touches a value, to stop a thread and read its registers, or to trace the path that decides an outcome. Any debugger changes target timing and can be detected: attach only to a target the user is authorized to modify, and prefer a scan or static analysis when it answers the question.

## Tools at a glance

| Goal | Tools |
|---|---|
| Attach and inspect state | `debugger_attach`, `debugger_get_status`, `debugger_detach`, `runtime_get_overview` |
| Find what writes, accesses or executes | `debugger_start_capture` -> `debugger_poll_capture` -> `runtime_stop_job` |
| Manual breakpoints | `debugger_set_breakpoint`, `debugger_list_breakpoints`, `debugger_delete_breakpoint`, `debugger_run_to` |
| Work on a stopped thread | `debugger_get_context`, `debugger_set_register`, `debugger_step`, `debugger_continue`, `debugger_get_stack_trace` |
| Step trace | `debugger_start_trace` -> `debugger_poll_trace` -> `runtime_stop_job` |
| Threads | `process_list_threads`, `debugger_break_thread`, `debugger_set_thread_ignored` |

Through the gateway every call also carries the same `instanceId`; captures, traces and breakpoints belong to that instance only.

## Interfaces

| `interface` | Method | Gate | Trade-offs |
|---|---|---|---|
| `0` | CE's configured default | resolved to the real interface first, then gated as that one | Check `activeInterface` in the result. |
| `1` | Windows debugging API | none | Most compatible. Visible to ordinary anti-debug checks; CE can only hide the `IsDebuggerPresent` API. |
| `2` | VEH debugger | `EnableTargetCodeExecution` (injects CE's VEH DLL into the target) | Avoids some debugger detection. A target that ever used VEH must be restarted before any new attach. |
| `3` | Kernel debugger (DBK) | `EnableKernelAccess` | For stubborn anti-debug. Needs CE's driver; after using it, CE's kernel module unloader or a reboot is needed before another interface works. See [kernel](kernel.md). |

- `debugger_attach(interface)` is idempotent for the active interface (`alreadyAttached:true`). A different interface needs an explicit `debugger_detach` first; MCP never detaches and reattaches for you.
- Read `activeInterface` and `usedFallback`: CE may not use the interface you asked for.
- Never retry a failed or timed-out attach blindly. Read `debugger_get_status` and report `error.kind`/`hostEffect`.
- VEH guard: MCP keeps a marker keyed by PID and process start time in CE's Lua state (it survives plugin reload) and refuses a second VEH-era attach. Restart the target, reattach to the new process and discard all old addresses. Never restart CE to get around it.
- If the target exits, freezes or complains on attach, it probably detects debuggers. Stop and report; switching to VEH or kernel is the user's decision.

## Breakpoint methods and slots

- **Hardware (debug registers).** The x86 CPU has four address slots (DR0-DR3), so at most four hardware breakpoints exist at once. Captures, manual breakpoints, `debugger_run_to` and trace entries all compete for them; a fifth needs another method. Default, most compatible, leaves code bytes untouched.
- **int3.** Unlimited, execute only. Writes `CC` into the code, so integrity checks, AOB scans and `memory_read` of that code see the change while it is set.
- **Page exception.** Uses page protection, so every access to the page traps: slow. Use only when hardware slots are exhausted.

Pass `method` only to override the default; take the exact spellings from the live schema.

## Size, alignment and triggers

- Data watch sizes are 1, 2, 4 or 8 bytes, and the address must be a multiple of the size (the CPU masks the low bits, silently watching a different range). 8 needs a 64-bit target. Watch a 4-byte int with `size=4` at its own address; for an unaligned field use a smaller size that covers its first bytes.
- `execute` ignores `size`.

| `trigger` | Fires on | Reported IP |
|---|---|---|
| `write` | Any write to the range, even of the same value | Next instruction (trap) |
| `access` | Reads and writes (x86 has no read-only data watch) | Next instruction (trap) |
| `execute` | Before the instruction runs | The instruction itself (fault) |

## The trap IP is the next instruction

A data breakpoint is a trap: the CPU reports it after the accessing instruction completed, so `ip` points at the following instruction. MCP derives `instructionAddress` by stepping back one instruction and flags the hit `isHeuristic:true`.

- Confirm with `code_disassemble(address=<ip>, before=1, count=2)`: the candidate must end exactly at `ip` and have a memory operand that can reach the watched address.
- If `ip` is a jump target, the instruction before it in memory may not be the one that ran. When unsure, run an `execute` capture on the candidate and compute its effective address from the registers.
- Registers in a data hit are the state after the instruction. For `mov [rcx+10],eax` the base `RCX` is intact, so base = `RCX`, offset = `10`. For `mov rax,[rax+8]` the base was overwritten; use an `execute` capture on that instruction (registers before it runs).

## Captures ("find what writes/accesses")

1. `debugger_attach`, then `debugger_start_capture(address, trigger, size, aggregateByInstruction, maximumHits, lifetimeSeconds)`. Keep the returned `jobId`.
2. Ask the user to trigger the change (take damage, spend money).
3. `debugger_poll_capture(jobId, afterSequence, limit)`. Polls are read-only and idempotent: pass back `nextAfterSequence`, poll again while `more` is true. `dropped > 0` means the bounded buffer evicted its oldest hits.
4. `runtime_stop_job(jobId)` removes the breakpoint, the hook and the results.

- Use `aggregateByInstruction=true` for values touched every frame: you get `groups` (`instructionAddress`, `disassembly`, `hitCount`, first and last context) instead of thousands of hits. Use per-hit mode for ordering or per-thread questions.
- Capture callbacks continue the thread automatically; the game keeps running.
- The writer is usually the group whose `hitCount` rises exactly when the user acts. Several writers are common (set, add, clamp, copy); read each with [code-analysis](code-analysis.md).
- Next steps: the base register gives a pointer lead ([pointers](pointers.md)); the instruction is an injection point ([auto-assembler](auto-assembler.md)); an `execute` capture on shared code yields one base per entity to compare ([structures](structures.md)).

## Stopping a thread

- `debugger_set_breakpoint(address, size, trigger, method?, threadId?, oneShot?)` creates a breaking breakpoint: the thread stops and waits. It persists until `debugger_delete_breakpoint(address)` or detach, and MCP tracks the ones it sets as owned resources.
- After a hit, `debugger_get_status` reports `broken:true`. Read `debugger_get_context(include="gp")`, or include FPU/XMM when floats matter. Register values are uppercase hex.
- A stopped thread stays stopped until `debugger_continue` or `debugger_step(mode="into"|"over")`. Meanwhile the game can freeze, its watchdogs can kill it, and Mono and remote-call tools refuse (`busy`). Never end a turn with a thread stopped unless the user asked for it.
- `debugger_run_to(address)` arms a one-shot execute breakpoint and continues; it removes itself when hit.
- `debugger_break_thread(threadId)` is asynchronous; poll `debugger_get_status`.
- `debugger_set_thread_ignored(threadId, ignored=true)` silences a noisy thread; undo it when done.
- `debugger_list_breakpoints` also shows breakpoints the user set in CE. Do not delete those unless asked.

## Registers

- `debugger_set_register(register, value)` works only while broken, on general-purpose registers and EFLAGS, and verifies by readback (`verified`). It takes effect when the thread continues.
- On x64, a 32-bit name such as `EAX` writes the whole `RAX`, zero-extended.
- Changing `RIP` redirects execution: only ever to a verified instruction boundary.
- FPU and XMM registers are readable through `debugger_get_context`, not writable.

## Step traces

- `debugger_start_trace(address, mode, maximumSteps, stopCondition?, includeStack, lifetimeSeconds)` returns a `jobId`: an entry breakpoint arms the trace; once hit, only that thread is single-stepped, up to 256 steps. `stopCondition` uses a fixed grammar (see the schema).
- `mode="over"` treats a call as one step; `"into"` follows calls and spends the budget in callees and system code. Start with `over`, then trace into the call that matters.
- The target must be running and reach the entry before the TTL; otherwise nothing is recorded.
- `debugger_poll_trace(jobId, afterSequence, limit)` returns `steps` (`ip`, `disassembly`, `registers`, `stack?`), `active` and `completed`.
- Completion leaves the thread stopped at the last step. `runtime_stop_job` does not resume it: stop the job, then `debugger_continue`.
- Look for the flag-setting instruction (`cmp`, `test`, `comiss`) followed by the conditional jump that selects the outcome, and compare register values across runs.

## Stack traces are heuristic

`debugger_get_stack_trace(depth)` (depth up to 128) scans the stack from `RSP` for values that point just after a `call`. Every frame is `isHeuristic:true`: stale stack data and stored code pointers produce false frames, and tail calls leave none. Verify a caller with `code_disassemble(address=<value>, before=1)` and expect a `call`.

## Jobs, cleanup and switching targets

- Captures and traces are jobs with a TTL (`lifetimeSeconds`, default 120 s, never above 300 s). A finished job stays pollable until its TTL, but `runtime_stop_job` or expiry discards its results: poll first. `runtime_list_jobs` shows them.
- `runtime_stop_job` is safe to repeat on a stopped job; an unknown id is `not_found`, never success.
- Before `process_attach` or ending the session: stop captures and traces, delete MCP breakpoints, continue stopped threads, then `debugger_detach` if nothing else needs it. `runtime_list_resources` shows what remains, entries from an earlier plugin activation included (`orphaned:true`). `runtime_release_resources` releases newest first and stops at the first incomplete release.
- Breakpoint callbacks are CE-owned Lua and can outlive a plugin disable until their TTL.
- Typical refusals: `busy` (another job owns the breakpoint hook or the slots), `invalid_state` (not attached or not broken), `capability_disabled` (interface 2 or 3 with its gate off). After `timeout` or `hostEffect:"unknown"`, inspect `debugger_get_status` and `debugger_list_breakpoints` before any retry.

## Sources

- https://wiki.cheatengine.org/index.php?title=Help_File:Debugger_options
- https://wiki.cheatengine.org/index.php?title=Help_File:Find_out_what_writes/accesses_this_address
- https://wiki.cheatengine.org/index.php?title=Help_File:Find_out_what_addresses_this_instruction_accesses
- https://en.wikipedia.org/wiki/X86_debug_register
- https://pdos.csail.mit.edu/6.828/2004/readings/i386/s12_02.htm
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
