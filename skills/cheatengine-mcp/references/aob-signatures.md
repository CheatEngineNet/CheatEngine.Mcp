# AOB signatures

An array-of-bytes (AOB) signature finds code by its bytes instead of its address, so a script keeps working when an
update moves the code. Scripts use it through `aobscanmodule` (see [auto-assembler](auto-assembler.md)); MCP proves it
with `aob_find`.

## Syntax

- Spaced hex bytes with `??` for a wildcard byte: `8B 83 ?? ?? ?? ?? 2B C7`.
- `aob_find` and `aob_generate_signature` use only hex bytes and `??`. Input is normalized to uppercase with single
  spaces.
- CE's Auto Assembler also accepts `?`, `x` and `*` wildcards, nibble forms such as `9*`, and unspaced patterns. Write
  `??` anyway, so the same pattern works in scripts and in MCP.
- A pattern of wildcards only, or one under about 8 fixed bytes, is almost never unique.

## What to keep and what to mask

An x86-64 instruction is: prefixes (`66`, `F2`, `F3`, REX `40`-`4F`), opcode (1-3 bytes, often `0F xx`), ModRM, optional
SIB, optional displacement, optional immediate. Keep the bytes that say *what* the instruction does; mask the bytes that
say *where* or *how much*.

| Part                     | Example (masked part in brackets)   | Rule                                                                       |
|--------------------------|-------------------------------------|----------------------------------------------------------------------------|
| prefixes, REX, opcode    | `48 8B`, `F3 0F 11`, `0F 2F`        | keep                                                                       |
| ModRM and SIB            | `8B 83`, `8B 04 C8`                 | keep (they encode the registers)                                           |
| disp8 field offset       | `89 43 [10]`                        | keep for uniqueness; mask if the structure layout changed between versions |
| disp32 field offset      | `89 83 [C8 04 00 00]`               | mask by default                                                            |
| RIP-relative disp32      | `48 8B 05 [xx xx xx xx]`            | always mask                                                                |
| rel32 call or jump       | `E8 [xx xx xx xx]`, `E9`, `0F 8x`   | always mask                                                                |
| rel8 short jump          | `EB [xx]`, `74 [xx]`                | mask (changes when nearby code changes)                                    |
| immediates               | `B8 [64 00 00 00]`, `81 xx [imm32]` | mask tuned constants; keep a distinctive stable constant                   |
| absolute addresses (x86) | `A1 [addr32]`, `68 [addr32]`        | always mask                                                                |

Worked example (x64):

```
8B 83 C8 04 00 00     mov eax,[rbx+000004C8]
2B C7                 sub eax,edi
89 83 C8 04 00 00     mov [rbx+000004C8],eax
E8 12 34 56 78        call ...
```

Signature `8B 83 ?? ?? ?? ?? 2B C7 89 83 ?? ?? ?? ??`. The injection point (`sub eax,edi`) is at offset `+6` from the
match. Trailing wildcards add nothing; end the pattern on a fixed byte.

- Avoid spanning a function boundary. The `CC`/`90` padding between functions changes with recompiles.
- Code must be captured unpatched and unhooked (see below).

## Generate a signature

- `aob_generate_signature(address, module?, codeSize?, verify=true)` runs CE's `getUniqueAOB` and then verifies the
  result with a separate bounded scan.
- The output holds `pattern`, `patternStart` (where the match begins), `offset` (distance from `patternStart` to
  `address`), `length`, `module`, `unique`, `matchCount` and `verified`. When CE finds nothing unique, `unique` is false
  and `triedPattern` shows its last attempt.
- The address must be inside a module. The tool refuses others, because CE would otherwise scan the whole address space
  on its main thread. Very large modules are capped; check the live schema.
- CE optimizes for uniqueness, not for update resilience. Review the pattern against the table above: mask any
  displacement, call target or immediate CE left fixed, then prove uniqueness again.
- `code_disassemble(address, before=8)` shows the surrounding instructions when you build a pattern by hand.

## Prove uniqueness

1. `aob_find(patterns=["8B 83 ?? ?? ?? ?? 2B C7"], module="game.exe", executable="required", limit=2)`.
2. Require `count` = 1 with `exact` = true, and the match address equal to the expected instruction (plus or minus the
   offset).
3. With `limit=1`, a count of 1 proves nothing. Always ask for at least 2.
4. If the script will use a whole-process `aobscan`, also prove it without `module`: the same bytes may exist in another
   module or in a copy.
5. `aob_find` takes up to 8 patterns per call; compare candidate patterns side by side.

- `scope` and `targetVerified` in the result state what was scanned and whether the target identity was confirmed.
  `elapsedMs` helps size later scans.
- `aob_find` blocks CE while it scans. Scope it with `module` or `startAddress`/`endAddress`, and use
  `executable="required"` for code.

## Module scoping and injection offset

- Always scan the module that owns the code: `aobscanmodule(INJECT,game.exe,8B 83 ?? ?? ?? ?? 2B C7)` in scripts,
  `module="game.exe"` in `aob_find`. It is faster and cannot match inside another DLL.
- Record the offset from the match to the patched instruction, and use `INJECT+6` in the script. Simpler: start the
  pattern at the instruction you patch, so the offset is 0.
- `asm_generate_injection(kind="aob", address, symbolName)` builds CE's AOB injection template with `aobscanmodule`,
  `registersymbol` and a matching `[DISABLE]` section. Check its `aobPattern` with `aob_find` too.
- A template's `[DISABLE]` restores the bytes seen when it was generated. If the overwritten instructions contain masked
  bytes, those bytes can differ in another version: save them at enable with `readmem` and restore from that copy
  instead of a fixed `db` line.
- Data signatures (AOB to data) work the same way on writable memory: `writable="required"`, `executable="excluded"`.
  Mask every byte that differs between samples of the structure.

## Scan with patches disabled

- An active patch replaces the first bytes with a `jmp` (`E9 ...`) or `NOP`s. A signature built on patched code will not
  match after a restart, and a script's own signature stops matching while it is enabled.
- Before generating: deactivate script records (`record_set_active(active=false)`), release MCP patches listed by
  `asm_list_patches` with `asm_release_patch`, and check `module_find_patches(module)` for leftover or third-party
  hooks.
- Some games unpack or JIT code at runtime. Generate the signature against memory, not the file on disk, and confirm it
  after a fresh start.

## Repair after a game update

1. Symptom: the script fails to enable (AOB not found), or `aob_find` returns 0 or more than 1 match.
2. `module_get(module)` shows the PE timestamp; confirm the game really changed.
3. Find the value again ([value-scans](value-scans.md)) and its writer ([debugger](debugger.md)).
4. `code_disassemble` the new code and compare it with the old pattern: usually a displacement, a register choice or the
   instruction order changed.
5. Build or generate a new pattern, mask the parts that changed, and prove uniqueness.
6. Update the script (`record_set_script`), run `asm_check`, enable and disable it twice, then `table_save`
   (see [cheat-tables](cheat-tables.md)).
7. Keep the old pattern and game version in a comment inside the script.

## Sources

- https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:aobScanModule
- https://fearlessrevolution.com/viewtopic.php?t=15547
- https://fearlessrevolution.com/viewtopic.php?t=2166
- https://forum.cheatengine.org/viewtopic.php?t=572465
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt
- https://learn.microsoft.com/windows-hardware/drivers/debugger/x64-architecture
- https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html
