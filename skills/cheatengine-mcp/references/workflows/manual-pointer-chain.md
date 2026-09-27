# Build a pointer chain manually

## Goal

Find a static path to the dynamic value at `{address}` by walking back through the code, up to `{maxLevels}` levels (default 4).

## Steps

1. `process_get_current()`: note `pointerSize` (8 on x64, 4 on x86).
2. Run [find what writes](../workflows/find-writer.md) on `{address}` (write or access). In the instruction's `[reg+off]`, `off` is the offset and the register value in the hit is the object base. Check with `util_calculate(expression="<base>+<off>")` that it equals `{address}`.
3. `pointer_find_references(target="<base>", maxOffset=0, limit=100)`: addresses that hold the base. A small `maxOffset` also finds pointers to just below the base.
4. `memory_get_address_info(addresses=[...])` on the results: an address inside a module (for example the game's .data section) is a static root; heap addresses are the next level. Skip thread stacks.
5. Static root found: get module+offset with `symbol_resolve(expressions=[...])`, then check that `pointer_read_chain(base="game.exe+<offset>", offsets=["<off>"], valueType="<type>")` reaches `{address}`.
6. Otherwise take one heap candidate, run find what accesses on it (`trigger="access"`, size = pointer size), read the new `[reg2+off2]`, and repeat from step 3 with that base. Stop at `{maxLevels}`.
7. Verify the whole chain with `pointer_read_chain(base=..., offsets=[<first offset>, ..., <last offset>])`: offsets are signed hex in dereference order, the root's offset first.
8. Ask the user to restart the game or change level, find the value again, and confirm the chain still reaches it.
9. With consent, `record_create(records=[{description, address:<root>, offsets:[...], valueType}])`.
10. Stop any remaining capture with `runtime_stop_job(jobId=...)` and ask about `debugger_detach()`.

## Decisions

- `[reg+index*scale+off]`: an array; the base is `reg` and the index part changes per element.
- Several static roots: prefer the main module, then the fewest levels and the smallest offsets.
- More than `{maxLevels}` levels or no static root: switch to the [pointer scan](../workflows/pointer-scan.md) or an [injection copy](../workflows/injection-copy-base.md).

## Pitfalls

- A data breakpoint reports registers after the instruction ran: for `mov rax,[rax+10]` the base register was overwritten. Use an `execute` capture on that instruction to see the registers before it runs.
- CE's address-list window shows offsets bottom-up; MCP tools always use dereference order.
- Pointer values are hex addresses; x86 targets use 4-byte pointers.
- Each capture is a job; stop it before switching target.
- The record write is a mutation: after an error whose `hostEffect` is not `not_started` or `not_applied`, list the records before retrying.

## Report

The chain as `[[module+X]+a]+b`, offsets in dereference order, the number of levels, the restart check, the record id, and any capture or debugger still active with its release call. See [pointers](../pointers.md) and [debugger](../debugger.md).
