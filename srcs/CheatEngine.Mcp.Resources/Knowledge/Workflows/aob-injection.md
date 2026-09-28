# Write an AOB code injection

## Goal

Change what the instruction at `{instructionAddress}` does to achieve `{goal}`, with a rollback-capable Auto Assembler
script that finds its code by signature, asserts the original bytes and disables cleanly. `{symbolName}` names the
injection point; if not given, pick one such as `damageHook`.

## Steps

1. `code_disassemble(address="{instructionAddress}", before=5, count=10)`: from the injection point, add up whole
   instructions until at least 5 bytes (at most 64); their `bytes` are the `expectedBytes`. Note RIP-relative operands
   and relative branches in that run.
2. `asm_list_patches()`; ask whether a Cheat Engine script patches this code: the signature needs original bytes.
3. `aob_generate_signature(address="{instructionAddress}", verify=true)`: `unique` and `verified` must be true; keep
   `pattern`, `module` and `offset`. Otherwise run [make a signature](make-aob-signature.md).
4. `asm_generate_injection(module="<module>", signature="<pattern>", expectedBytes="<bytes from step 1>", symbolName="{symbolName}", offset=<offset>)`:
   its `script`; nothing is applied.
5. Edit it for `{goal}`: new code goes between `newmem:` and `code:` and runs first; to drop or change the original,
   edit the `db` line under `code:`. There, write a RIP-relative or relative-branch instruction as its `opcode` text
   instead of bytes, its absolute address as module+offset (`symbol_resolve` gives the `name`). Change nothing else;
   show the user the result.
6. `asm_check(script="<script>")`: fix and check again until `accepted` is true.
7. Explain what the script changes, ask the user to save the game and to answer No to any "Nearby allocation error"
   dialog, then get explicit consent.
8. `asm_apply(script="<script>", name="{symbolName}")`: `retained` must be true; keep `patchId`, read `hostWarnings`.
9. `code_disassemble(address="{instructionAddress}", count=3)`: a 5-byte `jmp` (`E9`) to the new code, then any NOP
   padding. A 14-byte `FF25` jump overwrote unasserted bytes: release at once.
10. Ask the user to test `{goal}` in game.
11. Undo: `asm_release_patch(patchId=...)`, `released` true. To keep it as a cheat, release it, then with consent
    `record_create(records=[{description="{goal}", address="0", variableType=11, script="<checked script>"}])`;
    keep its `id`; see [build a robust table](build-robust-table.md).

## Decisions

- Only a removal or a same-length change: a [NOP patch](nop-patch.md) or a [branch patch](patch-branch.md) is simpler.
- The instruction also runs for enemies: filter first ([filter shared code](shared-code-filter.md)).
- Mono JIT code has no module: use the `aobscanregion` form of [Auto Assembler](../Documents/auto-assembler.md).
- `accepted` false: read `failedSection` and `hostMessages`; never apply a rejected script (a `disable` failure at
  the scaffold's own `dealloc(newmem)`: report it, keep the line).
- `unsupported` from `asm_check`: `{$lua}`, `globalalloc`, a non-hex `$` or another gated construct; keep the cave
  pure Auto Assembler.
- `capability_disabled`: autoAssembler is off, or at `asm_apply` the script reaches another gate; there any `name(`
  counts as a command, even in a comment. Report it and stop.
- `busy`: repeat once if `retryable`. `target_changed`: confirm the process and restart at step 1.

## Pitfalls

- Save the flags (`pushfq`/`popfq`) and every register you clobber; the new code runs on every thread that executes
  the instruction.
- No jump may land inside the overwritten run after its first byte.
- `asm_check` skips `assert` and the AOB scan and allocates nothing: wrong bytes or a lost signature fail only at
  apply.
- `retained` false, or an apply error whose `hostEffect` is not `not_started` or `not_applied`: inspect
  `asm_list_patches()` and the disassembly before any new attempt. The patch blocks a target switch and
  `debugger_detach` until released.

## Report

Patch id and name, injection address, module, signature and offset, what the new code does, whether it is still
applied, and the undo `asm_release_patch(patchId=...)`. A created record stays until `record_delete(ids=[...])`.
Background: [Auto Assembler](../Documents/auto-assembler.md), [x64 injection](../Documents/x64-injection.md) and
[AOB signatures](../Documents/aob-signatures.md).
