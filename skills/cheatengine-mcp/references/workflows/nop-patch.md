# Replace code with NOPs

## Goal

Disable the instruction at `{instructionAddress}`, usually a writer found
with [find what writes](../workflows/find-writer.md), with a reversible, tracked NOP patch.

## Steps

1. `code_disassemble(address="{instructionAddress}", before=3, count=6)`: confirm that the address starts an instruction
   and read its size, bytes and purpose.
2. Weigh the effect: NOPing `mov [..],reg` stops a store; NOPing `sub` stops a decrease; NOPing a `cmp`, a jump or a
   `call` changes control flow or leaves registers and the stack wrong. If the instruction also serves enemies, the
   patch affects them too; see [filter shared code](../workflows/shared-code-filter.md).
3. `symbol_resolve(expressions=["{instructionAddress}"])`: module+offset for the report.
4. `asm_list_patches()`: make sure no MCP patch already covers this address.
5. Ask the user to save the game, then get explicit consent.
6. From the complete instruction bytes in step 1, prepare an equal-length NOP sequence.
   `asm_apply_code_patch(address="{instructionAddress}", expectedBytes="<original instruction bytes>", replacementBytes="<equal-length NOP bytes>", name="nop <purpose>")`;
   keep `patchId` and `originalBytes`. The patch asserts the original bytes and can be undone.
7. `code_disassemble(address="{instructionAddress}", count=4)`: expect NOPs over the whole instruction.
8. Ask the user to test in game.
9. To undo: `asm_release_patch(patchId=...)`, then `code_disassemble` to confirm the original instruction is back.

## Decisions

- Several instructions: combine only whole consecutive instruction bytes into `expectedBytes` and use an equal-length
  NOP sequence for `replacementBytes`.
- The game crashes or misbehaves: release the patch; the instruction was needed. Use
  an [AOB injection](../workflows/aob-injection.md) that changes the value instead.
- A permanent cheat: build an AOB-based script record
  ([make a signature](../workflows/make-aob-signature.md), [robust table](../workflows/build-robust-table.md)); an MCP
  patch is not saved in a table.
- `capability_disabled`: the autoAssembler gate is off. Report it; never patch code with `memory_write` or Lua instead.

## Pitfalls

- Never split an instruction; derive `expectedBytes` only from complete instructions.
- The patch is an owned resource: it blocks target switching until released, and `runtime_release_resources()` also
  releases it.
- A game update moves the code; the address is valid for this build only.
- An error whose `hostEffect` is not `not_started` or `not_applied` may have written code: check `asm_list_patches()`
  and the disassembly before trying again.

## Report

Patch id, address and module+offset, original and new bytes, whether the patch is still applied, and the release call
`asm_release_patch(patchId=...)`. See [auto assembler](../auto-assembler.md) and [x64 injection](../x64-injection.md).
