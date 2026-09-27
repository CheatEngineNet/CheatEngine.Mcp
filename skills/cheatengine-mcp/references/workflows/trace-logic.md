# Trace the logic of an instruction

## Goal

Answer `{question}` by stepping through the code from `{address}` and finding the comparison and branch that decide the outcome.

## Steps

1. Read the code first: `code_disassemble(address="{address}", count=30)` and `code_get_function(address="{address}")`. Simple logic can often be answered without the debugger.
2. `debugger_get_status()`; when not attached, explain the effects, ask for consent, then `debugger_attach(interface=0)`.
3. `runtime_list_jobs()` and `debugger_list_breakpoints()`: stop other captures and traces with `runtime_stop_job(jobId=...)`, and ask before removing unrelated breakpoints; a trace needs the debugger to itself.
4. `debugger_start_trace(address="{address}", mode="over", maximumSteps=100, includeStack=false, lifetimeSeconds=60)`; keep the returned `jobId`. Use `mode="into"` to follow calls.
5. Ask the user to trigger the code path (jump on the platform, take the hit).
6. `debugger_poll_trace(jobId=..., afterSequence=0, limit=100)`, then pass back `nextAfterSequence` while `more` is true, until `completed`.
7. Find the `cmp`/`test` and the conditional jump that separate the outcomes; read compared memory with `memory_read` and compare registers across steps. `symbol_resolve` names the calls.
8. `runtime_stop_job(jobId=...)`. It does not resume the thread: check `debugger_get_status()` and call `debugger_continue()` while `broken` is true.
9. Explain the logic and propose a change (invert or remove the branch, change the compared value). Only with consent, continue with a [NOP patch](../workflows/nop-patch.md), `asm_apply_code_patch(address=..., instructions=[...])`, or an [AOB injection](../workflows/aob-injection.md).

## Decisions

- The trace never starts: the code did not run while armed, or another tool owns the breakpoint handler; confirm the address with [find what writes](../workflows/find-writer.md).
- The answer lies in a callee: trace again with `mode="into"` from the call target.
- 256 steps are not enough: start the trace later, closer to the branch.

## Pitfalls

- A completed trace leaves the game stopped at the last instruction; always continue it.
- Single-stepping is slow; online or timing-sensitive games may disconnect or desync.
- Traces expire after `lifetimeSeconds`; an expiring trace can leave the thread stopped.
- Active traces block target switching.
- An attach, continue or patch error whose `hostEffect` is not `not_started` or `not_applied`: check `debugger_get_status()` or `asm_list_patches()` before anything else.

## Report

The deciding instructions with addresses and module+offset, the register and memory values that drive the branch, the answer to `{question}`, the proposed patch, and the debugger state (attached, broken, traces stopped), with `debugger_detach()` if it is no longer needed. See [debugger](../debugger.md), [code analysis](../code-analysis.md) and [x64 injection](../x64-injection.md).
