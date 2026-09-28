# Find what writes or accesses an address

## Goal

List the instructions that write (`{trigger}` = `write`, the default) or read and write (`access`) the `{size}`-byte
value at `{address}` (1, 2, 4 or 8; default 4). Never on online or anti-cheat-protected games.

## Steps

1. `memory_get_address_info(addresses=["{address}"])`: readable; a module `section` means static data, a `private`
   region the heap. The address must be a multiple of `{size}`; 8 needs a 64-bit target (`pointerSize` 8 in
   `process_get_current()`).
2. `debugger_get_status()`. Attached and `broken`: `debugger_continue()`, since a capture starts only while the target
   runs. Not attached: explain that it can freeze or crash the game, get consent, then
   `debugger_attach(interface="windows")` (or `"default"`, CE's own setting); read `activeInterface`.
3. `debugger_list_breakpoints()`: the capture needs one of the four hardware slots (DR0-DR3), shared with the user's
   breakpoints and other jobs; when all are used the start is refused: ask which to free.
4. Explain the capture, get consent, start it and keep `jobId`:
   `debugger_start_capture(address="{address}", trigger="{trigger}", size={size}, aggregateByInstruction=true)`.
5. Ask the user to change the value in the game (for `access`, play a few seconds).
6. `debugger_poll_capture(jobId=..., afterSequence=0)`, passing back `nextAfterSequence` while `more` is true. Each
   item of `hits` is one instruction with `hitCount` and a `context` (`ip`, `instructionAddress`, `disassembly`,
   `registers` after the access). Items update in place: poll again from 0 to see which count rises as the user acts.
7. Per item, `code_disassemble(address="<instructionAddress>", count=2)`. A data hit traps after the access, so
   `instructionAddress` is decoded backwards (`isHeuristic`): it must end exactly at `ip`, where the second one
   starts, and its memory operand, computed from `registers`, must reach `{address}`. A `rep movs` or `rep stos` at
   `ip` itself may be the one, trapped mid-copy.
8. `symbol_resolve(expressions=["<instructionAddress>"])`: module+offset of each confirmed instruction.
9. `runtime_stop_job(jobId=...)` removes the breakpoint and discards the hits, as expiry does: poll first.
10. Ask whether the next step needs the debugger; otherwise `debugger_detach()`, refused (`busy`) while any MCP
    resource remains.

## Decisions

- No hits: the value did not change while armed, `{address}` is a display copy, or the game writes a wider field.
  Stop the job, then retry with `debugger_start_capture(address="{address}", trigger="access", size={size})`, or
  [identify the address](identify-address.md).
- `capability_disabled` or `unsupported` from `"default"`: Cheat Engine's setting selects VEH or kernel with its
  switch off, DBVM or an unknown debugger; use `"windows"`.
- Several writers (set, add, clamp, copy): the one whose count rises with the user's action holds the logic; one
  counting every frame is often a display copy, except for timers and counters.
- An instruction that overwrites its base (`mov rax,[rax+8]`) hides it:
  `debugger_start_capture(address="<instructionAddress>", trigger="execute")` shows the registers before it runs.
- Enemies run it too if, polled,
  `debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true)`
  shows several `effectiveAddress`: [filter shared code](shared-code-filter.md). Next: base plus offset leads to a
  [manual pointer chain](manual-pointer-chain.md); the instruction to a [NOP patch](nop-patch.md), an
  [AOB injection](aob-injection.md) or [trace the logic](trace-logic.md).

## Pitfalls

- Misaligned, or 8 bytes on a 32-bit target: watch an aligned piece every write touches, such as
  `debugger_start_capture(address="<first byte>", trigger="{trigger}", size=1)`.
- Each hit is a debug event that makes every thread wait on the Windows interface: keep captures on hot values short.
- An expired capture (at most 300 s) discards its hits; a running one blocks target switching until
  `runtime_stop_job` (`debugger_delete_breakpoint` cannot release it).
- After a VEH attach the process must restart before any new attach.
- A precondition refusal (not attached, stopped, misaligned) is `host_refused`, `hostEffect` `started`: after it, or
  any failure, check `debugger_get_status()` and `debugger_list_breakpoints()` before starting again.

## Report

A table of instruction address, module+offset, disassembly, hit count and write or read (a memory destination
writes). Say the capture is stopped, whether the debugger stays attached (interface) and that `debugger_detach()`
ends it. Background: [debugger](../Documents/debugger.md), [code analysis](../Documents/code-analysis.md).
