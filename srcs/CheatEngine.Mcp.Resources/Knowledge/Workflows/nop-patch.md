# Replace code with NOPs

## Goal

Disable the instruction at `{instructionAddress}`, usually a writer found with [find what writes](find-writer.md), with
a reversible, tracked NOP patch of the same length that asserts the original bytes and is undone by release.

## Steps

1. `code_disassemble(address="{instructionAddress}", before=3, count=6)`: the context (the predecessors are
   estimates). `code_decode(address="{instructionAddress}")`: its `instruction` gives the exact `bytes` and `size`.
   The address must start an instruction, as a confirmed debugger hit or a disassembly line gives it; never compute
   it.
2. Weigh the effect with the user. NOPing `mov [..],reg` stops a store and NOPing `sub` stops a decrease, but NOPing a
   `cmp`, a jump, a `call`, a `push` or a `pop` changes control flow or leaves registers and the stack wrong. If the
   instruction also serves enemies, the patch affects them too; see [filter shared code](shared-code-filter.md).
3. `symbol_resolve(expressions=["{instructionAddress}"])`: its `name` (module+offset) for the patch name and report.
4. `asm_list_patches()`: release, with consent, any patch of this server on this code (see `name`). Ask whether a
   Cheat Engine script patches this code too: the bytes must be the originals.
5. Build `replacementBytes` of the same length: `90` for each byte, or one multi-byte NOP per original instruction
   (2 bytes `66 90`, 3 `0F 1F 00`, 4 `0F 1F 40 00`, 5 `0F 1F 44 00 00`, 6 `66 0F 1F 44 00 00`). Preview with
   `code_disassemble_bytes(hexadecimalBytes="<replacement>", origin="{instructionAddress}")`.
6. Explain the change, ask the user to save the game, then get explicit consent.
7. `asm_apply_code_patch(address="{instructionAddress}", expectedBytes="<original instruction bytes>", replacementBytes="<equal-length NOP bytes>", name="nop <module+offset>")`:
   `retained` must be true; keep `patchId`.
8. `code_disassemble(address="{instructionAddress}", count=20)`: expect NOPs over the whole instruction and an
   intact next instruction.
9. Ask the user to test in game.
10. To undo: `asm_release_patch(patchId=...)` (`released` true), then `code_disassemble` again to confirm the
    original is back.

## Decisions

- Several instructions: concatenate the bytes of whole consecutive instructions into `expectedBytes` (1 to 64 bytes)
  and NOP each of them at its own length.
- A conditional jump or its check decides the outcome: use [force or invert a branch](patch-branch.md) instead.
- The game misbehaves: release the patch; the instruction was needed. Change the value instead with an
  [AOB injection](aob-injection.md).
- The game crashed: the patch died with the process and cannot be released normally. Tell the user, then
  [clean up the session](cleanup-session.md).
- A permanent cheat: an MCP patch is not saved in a table. After the release, store the NOP bytes as a record with
  the script template of [force or invert a branch](patch-branch.md) (signature, `asm_check`, `record_create`), then
  [build a robust table](build-robust-table.md).
- `capability_disabled`: the autoAssembler gate is off. Report it; never patch code with `memory_write` or Lua instead.
- `busy`: repeat once if `retryable`. `target_changed`: confirm the process and restart at step 1.

## Pitfalls

- Never split an instruction: derive `expectedBytes` only from complete instructions. A mismatch fails the patch's
  assert and writes nothing.
- A jump from elsewhere may land inside a run of several instructions; one NOP per instruction, or `90` per byte,
  keeps those targets valid. One long NOP over several instructions does not.
- The patch blocks a target switch and `debugger_detach` until released.
- A game update moves the code; the address is valid for this build only.
- `retained` false, or an error whose `hostEffect` is not `not_started` or `not_applied`: code may be written. Read
  `requiresManualRecovery`, check `asm_list_patches()` and the disassembly before trying again.

## Report

Patch id, address and module+offset, original instruction and bytes, new bytes, whether the patch is still applied,
and the release call `asm_release_patch(patchId=...)`. Only a kept record stays, until `record_delete(ids=[...])`.
Background: [Auto Assembler](../Documents/auto-assembler.md) and [x64 injection](../Documents/x64-injection.md).
