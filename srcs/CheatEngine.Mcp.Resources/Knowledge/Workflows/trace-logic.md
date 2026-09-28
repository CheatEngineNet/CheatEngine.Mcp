# Trace the logic of an instruction

## Goal

Answer `{question}` (if not given: what decides the outcome) by reading, then tracing, the code from `{address}` to
the comparison and conditional jump that decide it. Never on online or anti-cheat-protected games.

## Steps

1. Read first: `code_disassemble(address="{address}", count=30)`, `code_get_function(address="{address}")`, then
   `code_get_function_graph(address="<startAddress>")`: `conditional` blocks are the candidate decisions,
   `calls` names the callees. Simple logic needs no debugger.
2. `debugger_get_status()`; if `broken`, `debugger_continue()`: a trace starts only while the target runs. Not
   attached: explain that a stopped thread freezes the game, get consent, then `debugger_attach(interface="windows")`.
3. `runtime_list_jobs()` and `debugger_list_breakpoints()`: one trace runs at a time, and its entry takes one of the
   four hardware slots by default. Stop your finished jobs; ask before touching the user's breakpoints.
4. With consent, start it and keep `jobId`. It runs `maximumSteps` contexts (default 32, at most 256, the entry
   included), or ends after the first one matching `stopCondition`: pass `IP=` and the hex address of an instruction
   every path reaches after the decision, such as a `ret`:
   `debugger_start_trace(address="{address}", mode="over", maximumSteps=128, stopCondition="IP=<hex>")`.
5. Ask the user to trigger the code path.
6. `debugger_poll_trace(jobId=..., afterSequence=0)`, passing back `nextAfterSequence` while `more` is true. The
   trace ended when `job.state` is `completed`.
7. In `steps`, find the `cmp`, `test` or `comiss` and the conditional jump after it: `registers` hold the integer
   operands and `EFLAGS`, not XMM. Read a compared memory operand with
   `memory_read(address="<operand address>", valueType="<type>")`.
8. Once polled: `runtime_stop_job(jobId=...)`, which does not resume the thread, then `debugger_get_status()` and
   `debugger_continue()` while `broken`. Trace again for the other outcome: the first step where the two paths
   differ is the branch.
9. Explain the logic and propose a change: force or invert the jump ([patch a branch](patch-branch.md)), a
   [NOP patch](nop-patch.md) or an [AOB injection](aob-injection.md). Only with consent:
   `asm_apply_code_patch(address=..., expectedBytes=..., replacementBytes=...)`; keep `patchId`.

## Decisions

- `job.state` stays `running`: the code did not run while armed, or `{address}` is not on that path. Start earlier,
  or confirm the code with [find what writes](find-writer.md) on a value it touches.
- Refused for a `debugger_onBreakpoint` hook: stop that trace, or ask about the user's script.
- The answer lies in a callee: trace from its `calls` target, or without `stopCondition`:
  `debugger_start_trace(address="{address}", mode="into", maximumSteps=256)` follows calls, system code included,
  for all 256 steps.
- 256 steps are not enough: start later, closer to the branch.
- Both outcomes take the same path: the decision is in the caller or in data; find what writes the compared value.

## Pitfalls

- `stopCondition` compares hex numbers (leading zeros and `0x` ignored) and must name a register of the target's
  width: `EAX` on x64 is refused (`invalid_argument`); `IP` and `EFLAGS` work on both.
- Each step is a debug event that freezes every thread on the Windows interface; after about 5 s stopped, Windows
  marks the game "Not Responding": continue promptly.
- Traces expire (at most 300 s) and discard their steps; an active trace blocks target switching and detach.
- A precondition refusal (not attached, stopped, another hook) is `host_refused` with `hostEffect` `started`: check
  `debugger_get_status()`, fix it, then start again. After a `timeout` or `unknown`, check `debugger_get_status()`
  or `asm_list_patches()` before retrying.

## Report

The deciding instructions (address, module+offset, text), the values behind each outcome, the answer to
`{question}`, the proposed patch and, if applied, its `patchId` and the undo `asm_release_patch(patchId=...)`. Say
whether the debugger stays attached or a thread is stopped; `debugger_detach()` ends it, refused (`busy`) while the
patch is held. Background: [debugger](../Documents/debugger.md), [code analysis](../Documents/code-analysis.md),
[x64 injection](../Documents/x64-injection.md).
