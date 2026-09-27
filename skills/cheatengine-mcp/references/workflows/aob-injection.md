# Write an AOB code injection

## Goal

Change what the instruction at `{instructionAddress}` does to achieve `{goal}`, with a rollback-capable injection script
that finds its code by signature and disables cleanly. Use the requested `{template}` style only for the enabled section
you review and edit. `{symbolName}` names the injection point.

## Steps

1. `code_disassemble(address="{instructionAddress}", before=5, count=10)`: the jump needs at least 5 bytes of whole
   instructions; note RIP-relative operands and any jump target inside those bytes.
2. Signature: `aob_generate_signature(address="{instructionAddress}", verify=true)`, then
   `aob_find(patterns=["<pattern>"], module="<module>", limit=2)` must return exactly one match. Otherwise
   run [make a signature](../workflows/make-aob-signature.md).
3.
`asm_generate_injection(module="<module>", signature="<verified signature>", expectedBytes="<first whole instructions>", symbolName="{symbolName}")`:
generate the rollback-capable AOB scaffold; nothing is applied. Use the verified module signature and the original bytes
from steps 1 and 2.
4. Edit only the injected section for `{goal}` and keep the original code path. Show the user the final script.
5. `asm_check(script=...)`: fix every message until `accepted` is true.
6. Ask the user to save the game, then get explicit consent to apply.
7. `asm_apply(script=..., name="<goal>")`; keep `patchId`.
8. `code_disassemble(address="{instructionAddress}", count=3)`: expect a `jmp` to the new code.
9. Ask the user to test `{goal}` in game.
10. To keep it: `asm_release_patch(patchId=...)`, store the script with
    `record_create(records=[{kind:"script", description, script}])`, and
    follow [robust table](../workflows/build-robust-table.md). Otherwise release the patch when done.

## Decisions

- The generated scaffold uses a verified module signature, original-byte assertions and cleanup. Keep those parts intact
  when editing the enabled section.
- The instruction also runs for enemies: add a filter first ([filter shared code](../workflows/shared-code-filter.md)).
- `asm_check` rejects the script: read `messages`; never apply a rejected script.
- `capability_disabled`: the autoAssembler gate is off; report it and stop.

## Pitfalls

- Save the flags (`pushfq`/`popfq`) and every register you clobber; use `movss`/`movsd` for floats; the new code runs on
  every thread that executes the instruction.
- Keep `farJump=false` so CE allocates near the code and uses a 5-byte jump; a 14-byte jump overwrites more
  instructions.
- `[DISABLE]` must restore the original bytes, unregister symbols and dealloc.
- `asm_check` runs `{$lua}` blocks, which need the unsafeLua gate.
- While the patch is applied, the patched bytes no longer match the signature.
- An apply error whose `hostEffect` is not `not_started` or `not_applied`: inspect `asm_list_patches()` and the
  disassembly before any new attempt.

## Report

Patch id and name, injection address and module+offset, signature and match count, what the new code does, whether it is
still applied, and `asm_release_patch(patchId=...)` to undo it.
See [auto assembler](../auto-assembler.md), [x64 injection](../x64-injection.md), [AOB signatures](../aob-signatures.md)
and [safety](../safety.md).
