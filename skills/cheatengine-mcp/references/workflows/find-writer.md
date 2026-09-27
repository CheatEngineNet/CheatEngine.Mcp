# Find what writes or accesses an address

## Goal

List the instructions that write (`{trigger}` = write, the default) or read and write (`{trigger}` = access) the
`{size}`-byte value at `{address}` (size 1, 2, 4 or 8; default 4).

## Steps

1. `memory_get_address_info(addresses=["{address}"])`: the address must be readable and aligned to `{size}`; 8 bytes
   needs an x64 target.
2. `debugger_get_status()`. When no debugger is attached, explain the effects (anti-cheat can detect a debugger, and
   protected games can crash), ask for consent, then `debugger_attach(interface=0)`. Interface 0 uses CE's configured
   debugger; 2 (VEH) needs the targetCodeExecution gate and 3 (kernel) needs kernelAccess.
3. `debugger_list_breakpoints()`: the CPU has only four hardware breakpoint slots (DR0-DR3).
4.
`debugger_start_capture(address="{address}", trigger="{trigger}", size={size}, aggregateByInstruction=true, lifetimeSeconds=120)`;
keep the returned `jobId`.
5. Ask the user to make the value change in game (for access, just play for a few seconds).
6. `debugger_poll_capture(jobId=..., afterSequence=0, limit=100)`, then pass back `nextAfterSequence` while `more` is
   true. Each group has `instructionAddress`, `disassembly`, `hitCount` and registers.
7. For each group: `code_disassemble(address=<instructionAddress>, before=3, count=6)`. A data breakpoint traps after
   the access, so `instructionAddress` is a reconstruction (`isHeuristic`); confirm the instruction whose `[reg+offset]`
   really reaches `{address}`.
8. `symbol_resolve(expressions=[<instructionAddress>, ...])` for module+offset.
9. `runtime_stop_job(jobId=...)` removes the breakpoint.
10. Ask whether to keep the debugger; if not, `debugger_detach()`.

## Decisions

- No hits: the value did not change during the capture, the address is a display copy, or the game writes a wider field.
  Retry with `trigger="access"` or a larger `{size}`, or verify the address first.
- Several writers: damage, healing and resets usually differ (`sub` vs `mov`); ask the user which event matches each.
- The same instruction serves other entities: continue with [filter shared code](../workflows/shared-code-filter.md).
- Next steps: a [NOP patch](../workflows/nop-patch.md), an [AOB injection](../workflows/aob-injection.md), or the base
  register for a [manual pointer chain](../workflows/manual-pointer-chain.md).

## Pitfalls

- After a VEH attach the target must restart before another attach; never reattach to work around a failure.
- `debugger_get_status()` showing `broken=true` means a thread is stopped: `debugger_continue()`.
- Captures expire (`lifetimeSeconds`, at most 300 s). Polls do not consume hits, so polling again is safe.
- An active capture blocks target switching; stop it first.
- An attach error whose `hostEffect` is not `not_started` or `not_applied`: check `debugger_get_status()` before
  anything else.

## Report

A table of instruction address, module+offset, disassembly, hit count and write or read. State which captures were
stopped, whether the debugger is still attached (and its interface), and how to detach with `debugger_detach()`.
See [debugger](../debugger.md) and [code analysis](../code-analysis.md).
