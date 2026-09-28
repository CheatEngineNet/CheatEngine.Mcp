# Force or invert a conditional branch

## Goal

Make the conditional jump at `{address}` behave as `{mode}`: `always` makes it unconditional, `never` removes it so
execution falls through, `invert` reverses its condition. A tracked code patch of the same length asserts the
original bytes; release undoes it. Find the jump first with [trace logic](trace-logic.md).

## Steps

1. `code_decode(address="{address}")`: keep its `instruction` `bytes`, `size` and `opcode`. The mnemonic must start
   with `j` and not be `jmp`; its operand is the absolute target. Short `7x rel8` is 2 bytes, near `0F 8x rel32` 6.
2. `code_disassemble(address="{address}", before=6, count=10)`: the `cmp` or `test` that sets the flags and both
   paths, target and fall-through. Tell the user which path gives which outcome.
3. `symbol_resolve(expressions=["{address}"])`: its `name` (module+offset) for the patch name and the report.
4. `asm_list_patches()`: release, with consent, any patch of this server on this code (see `name`); ask whether a
   Cheat Engine script patches it.
5. Build same-length `replacementBytes` from the original bytes:
   - `always`: short, `EB` and the same rel8; near, `90 E9` and the same rel32, so the `E9` ends where the jump ended.
   - `never`: a NOP of the same length, `66 90` (2 bytes) or `66 0F 1F 44 00 00` (6 bytes).
   - `invert`: flip the lowest bit of the condition byte, the first of `7x` or the second of `0F 8x`
     (`74` becomes `75`).

   Preview with `code_disassemble_bytes(hexadecimalBytes="<replacement>", origin="{address}")`: a jump must show the
   original target.
6. Explain the change, ask the user to save the game, then get explicit consent.
7. `asm_apply_code_patch(address="{address}", expectedBytes="<original bytes>", replacementBytes="<new bytes>", name="{mode} <module+offset>")`:
   `retained` must be true; keep `patchId`.
8. `code_disassemble(address="{address}", count=3)`: the new jump or NOP, then an intact next instruction.
9. Ask the user to test in game. To undo: `asm_release_patch(patchId=...)`, `released` true, then disassemble again.

## Decisions

- Prefer `always` or `never`, which fix the outcome. `invert` also flips the other case: an inverted death check
  kills the player whenever the check used to pass.
- Not a `jcc` (`jmp`, `call`, `loop`, `cmovcc`, `setcc`), or `invert` on `jrcxz` (`E3`, whose flipped bit gives
  `loop`): stop; use a [NOP patch](nop-patch.md) or an [AOB injection](aob-injection.md).
- `size` not 2 or 6: prefix bytes come first; keep them for `always` and `invert`, NOP them too for `never`.
- Enemies or other callers share this code: the patch changes them too ([filter shared code](shared-code-filter.md)).
- `capability_disabled`: the autoAssembler gate is off. Report it; never patch code with `memory_write`.
- `busy`: repeat once if `retryable`. `target_changed`: confirm the process and restart at step 1.
- To keep the change (an MCP patch is not saved): release it; `aob_generate_signature(address="{address}")` must
  give `unique` and `verified` ([make a signature](make-aob-signature.md)). Fill this script (`<o>`: `offset` in hex;
  bytes spaced, as in `7E 1A`; `site`: unique in the table), pass `asm_check`, then with consent
  `record_create(records=[{description="<name>", address="0", variableType=11, script="<script>"}])`; see
  [build a robust table](build-robust-table.md).

```
[ENABLE]
aobscanmodule(site_aob,<module>,<pattern>)
label(site)
registersymbol(site)
assert(site_aob+<o>,<original bytes>)
site_aob+<o>:
site:
db <new bytes>
[DISABLE]
site:
db <original bytes>
unregistersymbol(site)
```

## Pitfalls

- Never re-encode the jump (a near `jmp` is 5 bytes): the step 5 layouts end where the original ended, so the reused
  displacement keeps the target.
- `expectedBytes` are step 1's exact bytes; a mismatch fails the assert and writes nothing.
- The patch blocks a target switch and `debugger_detach` until released.
- `retained` false, or an error whose `hostEffect` is not `not_started` or `not_applied`: code may be written. Read
  `requiresManualRecovery`, check `asm_list_patches()` and the disassembly before trying again.

## Report

A table: address, module+offset, mode, original and new instruction and bytes, `patchId`, still applied or not; then
the undo call `asm_release_patch(patchId=...)`. Only a kept record stays, until `record_delete(ids=[...])`. Background:
[code analysis](../Documents/code-analysis.md), [x64 injection](../Documents/x64-injection.md) and
[Auto Assembler](../Documents/auto-assembler.md).
