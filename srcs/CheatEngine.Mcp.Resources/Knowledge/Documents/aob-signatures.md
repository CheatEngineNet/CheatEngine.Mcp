# AOB signatures

An array-of-bytes (AOB) signature finds code or data by its bytes instead of its address, so a script keeps working
when a restart or an update moves the code. Read this page before you generate, prove, repair or script a signature,
or search memory for the exact bytes of one value.

The `aob_*` tools only read. Scripts built on a signature change the target: use them only on single-player or offline
software the user owns or may modify, never on online, competitive or anti-cheat-protected games, and get consent
before any apply ([safety](safety.md)).

## Tools at a glance

| Task | Call |
|---|---|
| Generate a signature for an instruction | `aob_generate_signature(address="game.exe+1A2B46")` |
| Prove patterns, compare variants | `aob_find(patterns=["8B 83 ?? ?? ?? ?? 2B C7 89 83"], module="game.exe", limit=2)` |
| Find the exact bytes of one typed value | `aob_find_value(valueType="wstring", value="Game Over", module="game.exe")` |
| Read bytes and instruction lengths | `code_disassemble(address="...", before=8, count=16)`, `code_decode(address="...")` |
| Turn a signature into an injection scaffold | `asm_generate_injection(module="game.exe", signature="...", expectedBytes="...")` |

The three `aob_*` tools block Cheat Engine (CE) while they scan (dispatch class `host_scan`) and need no gate.
`asm_generate_injection` only builds text: it calls nothing in CE and needs no gate.

## Pattern syntax

- Write hex byte pairs and `??` for a wildcard byte: `8B 83 ?? ?? ?? ?? 2B C7`. The tools also take a standalone `?`
  or `*` as one wildcard byte and split unspaced runs such as `488B??05` into pairs. Results show the normalized form:
  uppercase pairs and `??`, separated by single spaces.
- Refused with `invalid_argument`: nibble wildcards (`4?`, `?F`), `**`, `x` and `xx`, other odd-length tokens, and a
  pattern without a fixed byte. More than 4096 byte positions gives `limit_exceeded`.
- CE itself reads any pair that is not hex (`??`, `**`, `xx`) as a wildcard byte, and its scans, `aobscan*` lines
  included, read `4?` or `?F` as a nibble wildcard. Rewrite such bytes as `??` before you prove a script's pattern
  with the tools.
- Start and end the pattern on a fixed byte: leading and trailing wildcards add length, not uniqueness.
- Short patterns are often not unique in a large module: prove each pattern with `aob_find` instead of trusting its
  length.

## What to keep and what to mask

An x86-64 instruction is: prefixes (`66`, `F2`, `F3`, REX `40`-`4F`, or VEX `C4`/`C5` for AVX), opcode (1-3 bytes,
often `0F xx`), ModRM, optional SIB, optional displacement, optional immediate. Keep the bytes that say *what* the
instruction does; mask the bytes that say *where* or *how much*.

| Part | Example (masked part in brackets) | Rule |
|---|---|---|
| prefixes, REX, opcode | `48 8B`, `F3 0F 11`, `0F 2F` | keep |
| ModRM and SIB | `8B 83`, `8B 04 C8` | keep: they encode the registers and the addressing form |
| disp8 field offset | `89 43 [10]` | keep for uniqueness; mask if the structure layout changes between builds |
| disp32 field offset | `89 83 [C8 04 00 00]` | mask when the pattern must survive layout changes |
| RIP-relative disp32 | `48 8B 05 [xx xx xx xx]` | always mask |
| rel32 call or jump | `E8 [xx xx xx xx]`, `E9`, `0F 8x` | always mask |
| rel8 short jump | `EB [xx]`, `74 [xx]` | mask: it changes when nearby code changes |
| immediates | `B8 [64 00 00 00]`, `81 xx [imm32]` | mask tuned constants; keep a distinctive stable one |
| absolute addresses (32-bit) | `A1 [addr32]`, `68 [addr32]` | always mask: relocation changes them |

Worked example (x64), with the injection point at `sub eax,edi`:

```
8B 83 C8 04 00 00     mov eax,[rbx+000004C8]
2B C7                 sub eax,edi                <- injection point
89 83 C8 04 00 00     mov [rbx+000004C8],eax
```

- Starting at the injection point gives `2B C7 89 83` plus wildcards: too short to be unique.
- Including the previous instruction gives `8B 83 ?? ?? ?? ?? 2B C7 89 83`. The injection point is 6 bytes after
  the match start, so the offset is 6.
- The bytes a 5-byte jump replaces are whole instructions: `2B C7` alone is too short, so the patch covers
  `2B C7 89 83 C8 04 00 00` (8 bytes: the jump plus 3 `nop`s).
- Do not span a function boundary: the `CC`/`90` padding between functions changes with each compile.

## Generate a signature

`aob_generate_signature(address="game.exe+1A2B46")` takes an address inside a loaded module. It picks a generator by
the module's size and names it in `generator`:

- `cheat_engine`, for a module of at most 64 MiB: CE's `getUniqueAOB` builds the pattern, then a separate scan of the
  module, limit 2, proves it, unless `aob_generate_signature.verify` is false.
- `managed`, for a larger module, such as many Unreal `-Win64-Shipping.exe` and IL2CPP `GameAssembly.dll` files.
  There `getUniqueAOB` would list and read every match of the instruction bytes on CE's main thread, so the server
  builds the pattern itself. Its own scans prove it, so it is always verified, whatever `verify` says.

What CE does (CE source, `GetUniqueAOB`):

- It takes the whole instructions that cover at least 5 bytes from the address, the bytes a jump would replace, and
  scans the module for them. With one match it returns them unmasked, offset 0. Otherwise it tries windows that end up
  to 20 bytes after those instructions or start up to 20 bytes before the address, and returns a short unique one. In
  the added bytes it masks only 4-byte fields (displacement, immediate or rel32) whose signed value is at least 0x10000
  in magnitude: most RIP-relative displacements, distant call and jump targets, and absolute addresses.
- CE never masks the instructions at the address itself, nor a disp8, a rel8, a small field offset, a small immediate
  or a call to nearby code. In the example, CE could return `2B C7 89 83 C8 04 00 00` with offset 0, keeping the
  `4C8` offset.

What the managed generator does:

- It starts at the address, so `offset` is always 0 and `patternStart` is the address. It appends whole decoded
  instructions and scans the module, limit 2, each time the pattern reaches 8, 16, 24, 32, 48 and 64 bytes: at most 6
  scans, never past 64 bytes or the module's end. The first length that matches only at the address is returned.
- It masks RIP-relative and absolute displacements, the rel16 or rel32 of every near `call`, `jmp` and `jcc`, `moffs`
  addresses (`A0`-`A3`), and any other 4- or 8-byte displacement or immediate of at least 0x10000 in magnitude. It
  keeps prefixes, opcodes, ModRM and SIB bytes, 1- and 2-byte displacements and immediates (disp8 field offsets, rel8
  short jumps) and small 4-byte values such as the `4C8` offset.
- The instruction at the address must decode. `host_refused` "The bounded scan of ... did not find the bytes just
  read" means the code changed during the call or CE does not scan that memory: repeat once, then build by hand.

Results and failures:

- Output: `pattern` (uppercase pairs and `??`; CE's `*` is rewritten), `patternStart` (where a match begins), `offset`
  (from `patternStart` to the address: inject at match + offset), `length` in bytes, `unique`, `verified` and
  `matchCount` (2 means at least 2).
- `unique` is proven only when `verified` is true. Without verification, CE's pattern comes back with `unique` true
  and `verified` false: prove it with `aob_find`. `unique` false with a `pattern` means the verification scan did not
  find exactly one match at `patternStart`: extend the pattern.
- No signature is still a success: `unique` and `verified` are false and `pattern` is omitted. `triedPattern`, when
  present, is the last pattern that matched more than once: the code CE 7.7 names in its "Could not find unique AOB,
  tried code ..." text, or the managed generator's longest pattern. Start from a neighbouring instruction or build the
  pattern by hand.
- `host_refused` (hostEffect `completed`): `getUniqueAOB` raised an error, or returned an invalid pattern or offset.
- Refusals before any scan: an address outside every loaded module of known size (`invalid_argument`), an unknown
  `module` (`not_found`), a `module` that does not contain the address (`invalid_argument`) or whose size CE does not
  report (`unsupported`). Outside a module, CE's search would cover the whole address space on its main thread.
  `target_changed` means a scan could not stay bounded in one confirmed process: check the attach and repeat.
- Always review the result against the table: mask the displacements the generator left fixed, then prove it again
  with `aob_find`. When masking makes it non-unique, add neighbouring instructions instead of unmasking.

## Build a signature by hand

For a failed generation, code outside a module, or a pattern you want more resilient:

1. `code_disassemble(address="<injection point>", before=8, count=16)`. Each instruction has `bytes` (hex without
   separators; the tools accept it unspaced) and `size`. `before` only estimates earlier boundaries: start the pattern
   at the injection point or at another known instruction start.
2. `code_decode(address="<instruction>")` returns one instruction and its exact `length`.
3. Mask by the table, start and end on a fixed byte, and count the offset from the first pattern byte to the injection
   point.
4. Prove it with `aob_find` (next section).

Code outside any module (JIT code of Mono or .NET methods, unpacked allocations) has no module signature. Prove it in
its region with `aob_find(patterns=["..."], startAddress="<region start>", endAddress="<region end>", limit=2)` or in
all executable memory with `aob_find(patterns=["..."], executable="required", limit=2)`, and resolve the code again
each session ([Mono and .NET](mono-and-dotnet.md)).

- A Mono method's code starts at `mono_compile_method.nativeAddress` (target code execution gate), and
  `code_get_function(address="<nativeAddress>")` estimates its `size`. Prove the pattern in that range.
- While CE's Mono collector is attached, CE resolves `Namespace.Class:Method` by JIT-compiling the method in the game,
  so a script can scan only that method: `aobscanregion(INJECT_HP,Player:TakeDamage,Player:TakeDamage+400,<pattern>)`,
  with a hex end offset that covers the method. Without the collector the name does not resolve and the script fails
  to enable.
- IL2CPP code lives in the `GameAssembly.dll` module: sign it like any other module.

## Prove uniqueness

1. `aob_find(patterns=["8B 83 ?? ?? ?? ?? 2B C7 89 83"], module="game.exe", executable="required", limit=2)`.
2. Accept only `unique` true (that is, `exact` and `count` 1) with the single entry of `matches` equal to the expected
   `patternStart`, the injection point minus the offset.
3. With `limit=1` a count of 1 proves nothing: always ask for at least 2. The default limit is 1000, at most 10000.
4. `exact` false with matches: the limit cut the list. `count` 0 with `exact` false: CE could not tell "no match"
   from a failure (seen on whole-target scans); scope the scan and retry before concluding that the pattern is gone.
5. One call takes 1 to 8 patterns, each its own blocking scan: compare masking variants side by side.
6. A script that uses a whole-process `aobscan` needs a proof without `module` too: the same bytes can exist in another
   module or in a copy of the code.

- Scope: `module`, or `startAddress` with `endAddress` (both or neither; the end must not be below the start). Without
  either, CE scans the whole target. The result's `scope` reports `bounded_scan`, `global_scan`,
  `filtered_global_scan` (whole target, then filtered) or `unknown`; `elapsedMs` helps size later scans, and
  `targetVerified` says whether one confirmed target incarnation covered the whole scan.
- Mapped memory (MEM_MAPPED: file views and shared sections, such as an emulator's guest RAM) is skipped unless CE's
  scan settings include it or `aob_find.includeMapped` is true (`aob_find_value` takes it too). It cannot be combined
  with `module`, whose image is never mapped. CE's region filter is global: the call turns CE's MEM_MAPPED override on
  for its own scan, then clears every scan-region override, including one that a table or `lua_execute` set earlier
  ([emulators](emulators.md)). If that clear fails (`host_refused`, hostEffect `cleanup_unconfirmed`), repeat the
  call, which clears it again.
- Filters: `writable`, `executable` and `copyOnWrite` take `required`, `excluded` or `any`. `alignment` (1 to 65536) and
  `lastDigits` (1 to 16 hex digits) cannot be combined.
- Data signatures work the same way on writable memory: `aob_find(patterns=["..."], writable="required",
  executable="excluded", limit=2)` scans the whole target. Mask every byte that differs between samples of the
  structure.

## Find the bytes of one value

`aob_find_value` encodes one typed value as the little-endian bytes the target holds and finds them with one scan,
without scanner state. Its result holds `valueType`, `value` and `result`, which has the same fields as one `aob_find`
entry; `result.pattern` shows the encoded bytes.

- Types: `int8` to `uint64` (two's complement), `float` and `double` (exact bits), `pointer` (the target's pointer
  size), `string` (UTF-8) and `wstring` (UTF-16), both without a terminator. `bytes` is refused: use `aob_find`.
- Value text: decimal or `0x` hex integers (`0xFF` as `int8` is -1), invariant floats including `NaN` and `Infinity`,
  a hex pointer, or the text itself; at most 4096 encoded bytes.
- Exact bits only: a float shown as 100 may hold 99.99999. For rounded, approximate or changing values use `scan_first`
  ([value scans](value-scans.md), [value types](value-types.md)).
- Default alignment: 2 for 16-bit types; 4 for 32-bit and 64-bit numbers, `float`, `double` and `pointer`; no
  alignment filter (any address) for 8-bit types and strings. An explicit `alignment` goes from 1 to 65536; there is
  no `lastDigits`. The default limit is 100.

Uses:

- A text anchor: `aob_find_value(valueType="wstring", value="Game Over", module="game.exe", writable="excluded",
  limit=10)`, then the code that references it ([find code by string](../Workflows/find-code-by-string.md)). The match
  is case-sensitive and has no terminator, so `Health` also matches inside `HealthMax`; try `string` and `wstring`.
- Slots that hold a pointer: `aob_find_value(valueType="pointer", value="<object address>", writable="required",
  alignment=8, limit=200)` lists every 8-byte-aligned slot holding exactly that address (use 4 on a 32-bit target).
  It scans the whole target. A pointer to a field inside the object does not match; for chains use a pointer scan
  ([pointers](pointers.md)).
- A constant: `aob_find_value(valueType="float", value="100", module="game.exe", writable="excluded", alignment=1,
  limit=50)`. With `alignment=1` it also finds the value as an immediate inside an instruction, which the default
  alignment of 4 can miss.
- A data signature: copy `result.pattern`, add neighbouring bytes and `??`, and prove it with `aob_find`.

## Use a signature in a script

- Scope scripts to the module: `aobscanmodule(INJECT_HP,game.exe,8B 83 ?? ?? ?? ?? 2B C7 89 83)` is faster than
  `aobscan` (all readable memory) and cannot match inside another DLL. `aobscanregion(name,start,stop,pattern)` scans
  an exact range; `aobscanex(name,pattern)` scans only executable memory.
- No match fails the activation: CE's message starts "Error while scanning for AOB's" or reads "The array of byte
  named ... could not be found". `record_set_active` then leaves the record inactive, with a `failure` whose `reason`
  (such as `aob_not_found`) and `text` say why. Several matches raise no error: CE silently uses one of them, which is
  why uniqueness must be proven.
- CE resolves every `aobscan*` line before any `define`, so a define name inside a pattern is not replaced. Keep scans
  inside `[ENABLE]`, never above it: at disable time the bytes are already patched and the scan would fail.
- In `[DISABLE]`, address the patch through its registered symbol, since the pattern no longer matches while patched.
  Write it as a bare `INJECT_HP:` line: `asm_check` checks `[DISABLE]` without the symbols `[ENABLE]` registers and
  rejects a form such as `INJECT_HP+6:` (a bare line too, under a `{$STRICT}` placed above `[ENABLE]`).

`asm_generate_injection` turns a proven signature into a rollback-capable scaffold without applying anything:

`asm_generate_injection(module="game.exe", signature="8B 83 ?? ?? ?? ?? 2B C7 89 83", expectedBytes="2B C7 89 83 C8 04 00 00", symbolName="INJECT_HP", offset=6)`

- `offset` is the `aob_generate_signature` offset (0 to the signature length minus 1). With 0, the scan name is the
  registered symbol. Above 0, the scan result is the script-local `INJECT_HP_aob`, and `INJECT_HP` is a label at
  `INJECT_HP_aob+6` (hex), registered at the injection point.
- `expectedBytes` are the exact whole instructions at the injection point (`bytes` from `code_disassemble`), 5 to 64
  bytes, without wildcards. `[ENABLE]` asserts them before writing the jump and `[DISABLE]` writes them back. A build
  whose bytes differ there fails the assert instead of patching the wrong code.
- The scaffold allocates its cave near the match, writes a 5-byte `jmp` plus `nop`s over `expectedBytes`, and repeats
  them in the cave as a `db` line. If one of those instructions is RIP-relative or a relative branch, replace its bytes
  with its instruction text, its target written as `module+offset`: the absolute address that `code_disassemble`
  prints changes after a restart. [x64 injection](x64-injection.md) covers both, and what to do when CE finds no free
  memory within 2 GB.
- Add the custom code, run `asm_check(script="...")` (Auto Assembler gate), and apply only with consent
  ([AOB injection](../Workflows/aob-injection.md), [review an AA script](../Workflows/review-aa-script.md),
  [auto assembler](auto-assembler.md)). `asm_check` refuses as `unsupported`, before CE sees it, a script that needs
  another gate, uses `globalalloc` or writes `$` before anything but hex digits: review such a script by reading it.
- A hand-written script whose overwritten bytes contain wildcards and no `assert` cannot restore them with a fixed
  `db`: save them at enable with `readmem` into a registered label and restore that copy at disable, exactly the
  overwritten length. Never execute a `readmem` copy of a RIP-relative instruction in the cave.

## Scan with patches disabled

- An active patch replaces the first bytes with a jump (`E9 ...`) and `nop`s. A signature built on patched code fails
  after a restart, and a script's own pattern stops matching while it is enabled: 0 matches then is not a break.
- Before generating or proving, list MCP patches with `asm_list_patches()` (or `cheatengine://instance/patches`) and
  release them with `asm_release_patch(patchId="<patchId>")`. Disable table scripts, with consent, through
  `record_set_active(ids=[<id>], active=false)`, which needs the Auto Assembler gate for script records and for a
  group that passes the change on to one.
- `module_find_patches(module="game.exe")` compares the module's executable sections with its file on disk and lists
  the hooks and patches it finds, including third-party ones; `fileBytes` shows the original bytes (the first 64) of
  each range.
- Code that is unpacked or generated at run time must be signed from memory after it exists; confirm the signature
  after a fresh start.

## Repair after a game update

1. Symptom: the script fails to enable (`record_set_active` reports a `failure` such as `aob_not_found` or
   `assert_failed`), or `aob_find` returns 0 or several matches.
2. `module_get(module="game.exe")`: compare `pe.timeDateStamp` with the one kept for the signature to confirm the
   build changed.
3. Relax the old pattern (mask displacements, immediates, rel8 bytes) and compare variants in one
   `aob_find(patterns=["<old>", "<relaxed>"], module="game.exe", executable="required", limit=2)`. With one match,
   compare `code_disassemble` at match + offset with the old code kept in the script's comment.
4. No match: find the value again ([value scans](value-scans.md)) and its writer ([debugger](debugger.md),
   [find what writes](../Workflows/find-writer.md)), then compare the new instruction with the old one.
5. Build and prove a new pattern ([make an AOB signature](../Workflows/make-aob-signature.md)). Update the pattern,
   the offset, `expectedBytes` and any shifted field offset, then `asm_check(script="...")`.
6. With consent, and with the record inactive (`record_set_script` refuses an active one),
   `record_set_script(id=<id>, script="...")`, enable and disable it twice with `record_set_active`, and
   `table_save(path="...", overwrite=false)` ([cheat tables](cheat-tables.md)). Each disable must restore the code:
   `memory_hash(address="<injection point>", size=64)` then equals its value before the first enable.
7. Keep the old pattern and the build timestamp in a comment inside the script.

The full procedure is the [repair after update](../Workflows/repair-after-update.md) workflow.

## Sources

- https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:aobScanModule
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/frmautoinjectunit.pas (`GetUniqueAOB`)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/autoassembler.pas (`aobscans`)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/parsers.pas (`ConvertStringToBytes`)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/bin/autorun/monoscript.lua
  (`mono_symbolLookupCallback`)
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt (`getUniqueAOB`,
  `AOBScan`, `setSpecialScanOptionsOverride`)
- https://fearlessrevolution.com/viewtopic.php?t=2166
- https://forum.cheatengine.org/viewtopic.php?t=572465
- https://learn.microsoft.com/windows-hardware/drivers/debugger/x64-architecture
- https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html
