# Make a unique AOB signature

## Goal

Produce a byte pattern that finds the instruction at `{address}` exactly once in `{module}` (when not given, the module
that contains the address), with wildcards over the bytes that change between builds, and the offset from the match to
the instruction.

## Steps

1. `asm_list_patches()`, and ask whether a Cheat Engine script or trainer patches this code: a signature must be made
   from original bytes, so release (`asm_release_patch`) or disable such patches first.
2. `memory_get_address_info(addresses=["{address}"])`: `module` and `section` (normally .text) must be set. Then
   `module_find_patches(module="<module>")`: no reported range may overlap the bytes around `{address}`.
3. `code_disassemble(address="{address}", before=8, count=12)` and `code_decode(address="{address}")`: the address
   must start an instruction. Note each instruction's `bytes` and what its `opcode` holds: memory operands, branch
   targets, immediates.
4. `aob_generate_signature(address="{address}", module="{module}", verify=true)`, leaving `module` out when `{module}`
   is not given: `unique` and `verified` must be true; keep `generator`, `pattern`, `patternStart`, `offset` and
   `length`.
5. Lay the pattern over the disassembly from `patternStart` and write a wildcarded variant. Keep opcode and ModRM
   bytes; turn into `??` the 4-byte displacements of RIP-relative operands, the 4-byte targets of `call`, `jmp` and
   `jcc` (E8, E9, 0F 8x), absolute addresses in 32-bit code and constants likely to be tuned. Already wildcarded:
   - generator cheat_engine (modules up to 64 MiB): large 4-byte fields of the neighbouring instructions, never those
     of the instructions at `{address}`;
   - generator managed (larger modules): the pattern starts at `{address}` (`offset` 0), covers whole instructions and
     wildcards RIP-relative and absolute addresses, near branch targets and 4- or 8-byte values of 0x10000 or more.
6. Prove both:
   `aob_find(patterns=["<pattern>", "<wildcarded variant>"], module="<module>", executable="required", limit=2)`.
   Keep the variant only if its `unique` is true and its one match equals `patternStart`.
7. The instruction is at match + `offset`. Hand it on as
   `asm_generate_injection(module="<module>", signature="<variant>", expectedBytes="<whole instructions>", offset=<offset>)`,
   or in a script as `aobscanmodule(<name>,<module>,<variant>)` with a label at `<name>+<offset>`.
8. `module_get(module="<module>")`: keep `timeDateStamp` (under `pe`), which identifies the build the signature was
   proven on.

## Decisions

- `unique` false: with no `pattern`, `triedPattern` shows the last attempt, which matches more than once; with a
  `pattern`, the verification found a second match. Start from a neighbouring instruction with more distinctive bytes
  and add the distance to the offset, or build the pattern by hand from whole instructions of step 3 and prove it with
  `aob_find`.
- `host_refused` from the managed generator (the code changed while it was built): repeat once, then build by hand.
- The variant is not unique: extend it over more whole instructions before or after; do not restore a wildcarded
  displacement to make it unique.
- No `module` (code allocated at run time, JIT code of Mono or .NET): module signatures do not apply. Resolve the
  method again each session with [Unity recon](unity-mono-recon.md) or [.NET recon](dotnet-recon.md).

## Pitfalls

- A signature is proven only for this build; after an update see [repair after update](repair-after-update.md).
- An `aob_find.limit` of 1 cannot prove uniqueness; use 2 or more and check `exact`.
- With several matches, Cheat Engine's `aobscan` commands use the first one they find: a non-unique signature patches
  the wrong code without any error.
- Too many wildcards match elsewhere after an update; too few break on every patch.
- Once a script is applied, the site holds a `jmp` and the pattern no longer matches there. Its `[DISABLE]` must write
  back through a label registered at the site (as the `asm_generate_injection` scaffold does), never scan again.
- `module_find_patches` compares at most 64 MiB: in a larger module, `truncated` true can hide a patch at the site.
- `aob_generate_signature` and `aob_find` block Cheat Engine while they scan; keep `aob_find` scoped to the module.

## Report

Pattern and wildcarded variant, generator, module, `offset`, `length`, match counts, which bytes are wildcarded and
why, and the build (`timeDateStamp`). This workflow reads only and owns nothing; remind the user to re-enable any
patch or script disabled in step 1. Background: [AOB signatures](../Documents/aob-signatures.md).
