# Build a pointer chain manually

## Goal

Find a static path to the dynamic value at `{address}` by walking back through the code that uses it, at most
`{maxLevels}` levels (default 4), and prove that it survives a restart. Single-player or offline software the user may
modify only.

## Steps

1. `process_get_current()`: note `pointerSize` (8 on x64, 4 on x86) and `processName`.
   `memory_get_address_info(addresses=["{address}"])`: an address with a `module` is already static; stop.
2. Explain the debugger (games can detect it or crash), ask for consent, then `debugger_attach(interface="windows")`.
3. Level 1: `debugger_start_capture(address="{address}", trigger="access", size=4, aggregateByInstruction=true)`
   (size = the value's width; the address must be aligned to it); keep the `jobId`. The user makes the game use the
   value; read the hits with `debugger_poll_capture(jobId=..., afterSequence=0)`, then `runtime_stop_job(jobId=...)`.
4. Pick a hit whose `disassembly` has a `[reg+off]` operand, such as `mov [rbx+000004C8],eax`: `4C8` is this
   level's offset; the watched address minus it is the object base, whatever the registers show.
   `pointer_get_access_info` takes supplied instruction/register facts and explicit symbols; use `contextPhase="post_execution"` for data hits and check uncertainty.
5. `pointer_find_references(target="<address>-4C8", maxOffset=0)`: `target` is the base, `references` its holders.
   One with a `symbol` (such as `game.exe+1A2B30`) lies in a module image, a static root: go to step 7.
6. Only heap holders: capture one that a second search still returns:
   `debugger_start_capture(address="<holder>", trigger="access", size=8, aggregateByInstruction=true)` (4 on x86).
   `mov rbx,[rsi+18]` gives the offset `18`; the base is `<holder>-18`. Repeat 4-6, at most `{maxLevels}` levels.
7. Verify: `pointer_read_chain(base="game.exe+1A2B30", offsets=["18", "4C8"], valueType="int32")`: hex offsets in
   dereference order, the one found last first; `address` must equal `{address}`.
   For several candidates, use `pointer_read_chains` with `{id,base,offsets}`, optional `target` and `valueType`; inspect all three outcome statuses.
8. Stop every capture with `runtime_stop_job(jobId=...)`, then `debugger_detach()`.
9. The user restarts the game; `process_attach(process="<processName>")`, find the value again and repeat step 7: the
   new `address` must hold it.
10. With consent:
    `record_create(records=[{description="...", address="game.exe+1A2B30", variableType=2, offsets=["18", "4C8"]}])`:
    step 7's offsets; `variableType` 2 int32, 4 float, 5 double; without a `value` nothing is written. Keep the
    `id`; `currentAddress` must be the value's address.

## Decisions

- No hits: the user repeats the action within the capture's `lifetimeSeconds`, or you start a new capture.
- Hits with different offsets: try each base; the one with holders is the object start.
- `[reg+index*scale+off]` is an array element: the chain holds for that element only
  ([find the entity list](find-entity-list.md)).
- No holder: the register pointed inside the object (`lea rcx,[rbx+20]`):
  `pointer_find_references(target="<base>", maxOffset=4096)`, and add the reference's `offset` to this level's.
- Several static roots: prefer the main module, fewer levels, small offsets.
- Beyond `{maxLevels}` levels or no holder at all: the [pointer scan](pointer-scan.md) or an
  [injection copy](injection-copy-base.md).

## Pitfalls

- Data breakpoints report registers after the instruction ran (`mov rax,[rax+10]` loses the base), hence step 4.
  `debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true)` shows
  them before it runs, one group per `effectiveAddress`: keep the watched address's group.
- `isHeuristic`: `instructionAddress` is a guess; in `code_disassemble(address="<instructionAddress>", count=1)` its
  `address` plus `size` must equal `ip`.
- A holder gone on a second search was a stack slot or a temporary.
- `memory_read_failed` (with `hopIndex`) from `pointer_read_chain`: that object does not exist in this game state yet;
  retry later.
- `busy` from `process_attach` or `debugger_detach`: a capture or another resource is still held
  (`runtime_list_resources()`). After an error whose `hostEffect` is started or unknown, check `debugger_get_status()`
  or `record_find(address="game.exe+1A2B30")` before retrying.

## Report

Per level: instruction (module+offset), offset, base, holder, static or heap. The chain as `[[game.exe+X]+a]+b`, the
restart result and the record id. Still active: captures (`runtime_stop_job`), the debugger (`debugger_detach`) and
the record (`record_delete(ids=[<id>])`). See [pointers](../Documents/pointers.md) and
[debugger](../Documents/debugger.md).
