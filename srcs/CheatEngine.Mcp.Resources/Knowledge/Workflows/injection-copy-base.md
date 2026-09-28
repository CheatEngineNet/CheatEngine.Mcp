# Capture a base pointer by injection

## Goal

Copy the object base that the instruction at `{instructionAddress}` uses into the registered symbol `{symbolName}`
(default playerBase, at most 60 characters), so records can read `[{symbolName}]+offset` instead of a fragile pointer
path. Choose an instruction that handles only the player; HUD code is a common candidate.

## Steps

1. `code_disassemble(address="{instructionAddress}", count=4)`: in the first `opcode`, such as `mov eax,[rbx+000004C8]`,
   read the base register (RBX) and the displacement; the `bytes` from there on give `expectedBytes`.
2. Prove it handles only the player. `debugger_get_status()`; when not attached, ask for consent, then
   `debugger_attach(interface="windows")`.
   `debugger_start_capture(address="{instructionAddress}", trigger="execute", groupByEffectiveAddress=true, lifetimeSeconds=60)`,
   keep `jobId`, let the user play near enemies, then `debugger_poll_capture(jobId=..., afterSequence=0, limit=1000)`
   and `runtime_stop_job(jobId=...)`. One item (one `effectiveAddress`) means one object; otherwise pick another
   instruction or [filter shared code](shared-code-filter.md). Offer `debugger_detach()` now: it refuses once the
   patch exists.
3. `aob_generate_signature(address="{instructionAddress}", verify=true)`: `unique` and `verified` must be true; keep
   `pattern`, `module` and `offset`, or run [make a signature](make-aob-signature.md).
4. `asm_generate_injection(module="<module>", signature="<pattern>", expectedBytes="<whole instructions, 5 bytes or more>", symbolName="{symbolName}Hook", offset=<offset>)`.
5. Edit the `script`: declare `label({symbolName})` and `registersymbol({symbolName})` beside the other declarations;
   right after `newmem:` write `mov [{symbolName}],<base register>` with the full register (RBX, not EBX, on
   x64); after the cave's `jmp return` add the line `{symbolName}:` then `dq 0` (`dd 0` on x86); add
   `unregistersymbol({symbolName})` to `[DISABLE]`. Change nothing else.
6. `asm_check(script="<script>")` until `accepted` is true; otherwise read `failedSection` and `hostMessages`.
7. Explain the change, ask the user to save the game and give consent, then
   `asm_apply(script="<script>", name="copy {symbolName}")`: `retained` must be true; keep `patchId`.
8. `memory_read(address="{symbolName}", valueType="pointer")`: 0 until the code runs. Ask the user to trigger it
   (move, take damage), then read again; it must equal the captured base.
9. `pointer_read_chain(base="{symbolName}", offsets=["<field offset>"], valueType="int32")` must show a known value,
   such as the health on screen.
10. With consent, `record_create(records=[{description="HP", address="[{symbolName}]+<field offset>", variableType=2}])`
    (variableType 4 for a float); keep each record `id`.
11. To undo: `asm_release_patch(patchId=...)`, and `record_delete(ids=[<id>])` for records the user does not keep.

## Decisions

- The symbol keeps the last object seen: after a level reload it points at freed memory until the code runs again.
- To keep the cheat, release the patch, store the checked script in a script record
  (`record_create(records=[{description="copy {symbolName}", address="0", variableType=11, script="<checked script>"}])`)
  and follow [build a robust table](build-robust-table.md).
- No items in step 2: have the user trigger the code, then capture again.
- Step 2 is refused (no single memory operand): capture without `debugger_start_capture.groupByEffectiveAddress`; the
  base register in every hit's `context.registers` must hold one value.
- `capability_disabled`: the autoAssembler gate is off; report it and stop.

## Pitfalls

- A rare second caller makes the symbol flicker; let the capture of step 2 run long enough.
- If `expectedBytes` hold a RIP-relative operand or a relative branch, replace the `db` under `code:` with the
  instruction text of step 1 (in a kept script, write its absolute address as `<module>+<offset>`).
- The symbol lives in the cave: after the release, records that read `[{symbolName}]` no longer resolve.
- A "Nearby allocation error" dialog during the apply: ask the user to answer No.
- An apply error whose `hostEffect` is not `not_started` or `not_applied`: inspect `asm_list_patches()` and the
  disassembly before any retry. The patch blocks target switching until released.

## Report

Symbol name, patch id, instruction as module+offset, base register, verified fields with offsets and values, record
ids, and what is still owned: the patch (`asm_release_patch`), the records (`record_delete`) and an attached
debugger (`debugger_detach`). Background: [Auto Assembler](../Documents/auto-assembler.md) and
[pointers](../Documents/pointers.md).
