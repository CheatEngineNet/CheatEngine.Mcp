# Capture a base pointer by injection

## Goal

Copy the object base used by the instruction at `{instructionAddress}` into a registered symbol (`{symbolName}`, default playerBase), so records can use `[{symbolName}]+offset` instead of a fragile pointer path.

## Steps

1. `code_disassemble(address="{instructionAddress}", before=3, count=6)` and `code_decode(address="{instructionAddress}", count=1)`: find the base register of `[reg+offset]`.
2. Prove that the instruction only handles the player: with the debugger attached (consent first), `debugger_start_capture(address="{instructionAddress}", trigger="execute", lifetimeSeconds=60)` and keep the returned `jobId`, let the user play, then `debugger_poll_capture(jobId=..., afterSequence=0, limit=100)` and check that the base register always holds one value. Stop it with `runtime_stop_job(jobId=...)`. Several values: pick another instruction (HUD code often reads only the player) or [filter shared code](../workflows/shared-code-filter.md).
3. `asm_generate_injection(kind="aob", address="{instructionAddress}", symbolName="{symbolName}_inject")`.
4. Edit the script: add `globalalloc({symbolName},8)` and, at the start of the new code, `mov [{symbolName}],<base register>` (the full 64-bit register on x64); keep the original instructions after it.
5. `asm_check(script=...)` until `accepted` is true.
6. Ask the user to save the game and give consent, then `asm_apply(script=..., name="copy {symbolName}")`; keep `patchId`.
7. `memory_read(address="{symbolName}", valueType="pointer")`: it reads 0 until the code runs. Ask the user to trigger it (move, take damage), then read again.
8. Verify a field: `pointer_read_chain(base="{symbolName}", offsets=["<offset>"], valueType=...)` must show the known value.
9. With consent, `record_create(records=[{description, address:"{symbolName}", offsets:["<offset>"], valueType}])`.
10. When done, `asm_release_patch(patchId=...)`.

## Decisions

- The symbol keeps the last object seen; after a level reload it is stale until the code runs again.
- To keep the cheat, store the script in a script record and follow [robust table](../workflows/build-robust-table.md).
- `capability_disabled`: the autoAssembler gate is off; report it.

## Pitfalls

- Copy the register before the original instruction runs if that instruction overwrites it.
- The code runs for every caller; a rare second caller makes the symbol flicker.
- `globalalloc` memory and its symbol stay until CE closes, even after the patch is released.
- An apply error whose `hostEffect` is not `not_started` or `not_applied`: inspect `asm_list_patches()` and the disassembly before trying again.

## Report

Symbol name, patch id, instruction and module+offset, verified fields with offsets, record ids, and what is still owned: the patch (`asm_release_patch(patchId=...)`) and the global symbol memory, which only CE's exit frees. See [auto assembler](../auto-assembler.md) and [pointers](../pointers.md).
