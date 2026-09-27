# Make a unique AOB signature

## Goal

Produce a byte pattern that finds the instruction at `{address}` exactly once in `{module}` (default: the module that contains it), with wildcards over bytes that change between builds.

## Steps

1. `asm_list_patches()`, and ask whether a CE script patches this code: active patches change the bytes, so release or disable them first.
2. `memory_get_address_info(addresses=["{address}"])`: the address must lie inside a module.
3. `code_disassemble(address="{address}", before=8, count=12)` for context.
4. `code_decode(address="{address}", count=6)`: `wildcardMask`, `memoryOperand` and `immediate` show the displacement, relative-target and immediate bytes that should become wildcards.
5. `aob_generate_signature(address="{address}", module="{module}", verify=true)`: returns `pattern`, `patternStart`, `offset`, `unique` and `matchCount`.
6. Review the pattern: keep opcode and ModRM bytes; wildcard (`??`) 32-bit displacements, `call`/`jmp` relative targets, RIP-relative offsets and values likely to be tuned (damage constants).
7. Prove it: `aob_find(patterns=["<pattern>", "<more wildcarded variant>"], module="{module}", limit=2)`; the chosen pattern must return exactly one match at `patternStart`.
8. Record the offset: the instruction is at match + `offset`. In a script use `aobscanmodule(<name>,<module>,<pattern>)` and write at `<name>+<offset>` when the offset is not zero.

## Decisions

- Not unique: lengthen it (`codeSize`) or start earlier so it includes neighbouring instructions.
- Unique only thanks to a displacement byte: wildcard that byte and lengthen instead.
- Not in a module (JIT code of Mono or .NET): module signatures do not apply; resolve the method again each session with [Unity recon](../workflows/unity-mono-recon.md) or [.NET recon](../workflows/dotnet-recon.md).

## Pitfalls

- A signature is proven only for this build; after an update see [repair after update](../workflows/repair-after-update.md).
- `aob_find` with `limit=1` cannot prove uniqueness; always use at least 2.
- Too many wildcards match elsewhere after an update; too few break on every patch.
- `aob_generate_signature` scans the whole module and can take a moment on large modules.

## Report

Pattern, module, match count, offset to the instruction, which bytes are wildcarded and why, and the build it was proven on (timestamp from `module_get(module="{module}")`). This workflow reads only and owns nothing. See [AOB signatures](../aob-signatures.md).
