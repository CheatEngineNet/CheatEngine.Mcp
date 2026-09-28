# Review an Auto Assembler script before applying it

## Goal

Decide whether an Auto Assembler script is safe to apply or to enable: record `{recordId}` when given (ids start at
0), else the script in the conversation, checked against the injection address `{site}` when given. Nothing is applied.

## Steps

1. With a record, `record_get(ids=[{recordId}])`: `variableType` must be 11, `script` holds the text and `active`
   says whether it already runs. Otherwise ask the user to paste the whole script.
2. Read it against the checklist in Decisions. A script with a construct listed under Ask first, `globalalloc`, or
   `$` before anything but hex digits (`$myVar`) is reviewed by reading alone: `asm_check` refuses it.
3. `asm_check(script="<script>")`: `accepted` must be true; otherwise report `failedSection` and `hostMessages`.
   `unsupported` is no verdict: Cheat Engine never saw the script (step 2).
4. For each `aobscanmodule(name,module,pattern)` line, while the script is inactive:
   `aob_find(patterns=["<pattern>"], module="<module>", limit=2)`; `unique` must be true. Write `**`, `xx` and nibbles
   such as `5?` as `??`. Scope an `aobscanregion` line with `aob_find.startAddress` and `aob_find.endAddress`.
5. `code_disassemble(address="{site}", before=5, count=10)`, or at the match plus the script's offset: the `assert`
   bytes and the `[DISABLE]` restore bytes must equal the current `bytes` of whole instructions.
6. `module_find_patches(module="<module>")`: a range at the site means another script, trainer or mod patches it.
7. `symbol_list_registered(nameContains="<name>")` for each name the script registers: the exact name already listed
   belongs to another script.

## Decisions

Fail blocks the apply; warn needs the user's agreement.

- Fail: `accepted` is false; a pattern is missing or not unique; asserted or restored bytes differ from memory; the
  jump splits an instruction, or a branch seen in step 5 lands inside the overwritten bytes.
- Fail: `[DISABLE]` does not restore every overwritten byte, `dealloc` every `alloc` and `unregistersymbol` every
  registered name; or an `aobscan`, `assert` or `readmem` of the site sits above `[ENABLE]`, so disabling fails.
- Fail: originals copied into the cave as raw `db` or `readmem` although they hold a RIP-relative operand or a relative
  `call`, `jmp` or `jcc`; they must be instruction text or `reassemble`.
- Warn: `alloc` without the site as third argument (on x64 a far cave makes the `jmp` 14 bytes); sizes are decimal,
  so `alloc(newmem,1000)` is 1000 bytes and `$1000` is 4096.
- Warn: flags or clobbered registers not saved (`pushfq`/`popfq`; `pushad` is invalid on x64); a `call` without
  16-byte stack alignment and shadow space.
- Warn: a fixed address such as `game.exe+1234` instead of a signature, worse without `assert`; `globalalloc` (never
  freed).
- Ask first: `{$lua}` and `luacall` (unsafeLua gate), `createthread`, `loadlibrary`, `{$c}` and `{$ccode}`
  (targetCodeExecution), `kalloc` (kernelAccess); `{$luacode}`, `include`, `loadbinary` and unknown or Lua-registered
  commands (`USEMONO`) need unsafeLua and targetCodeExecution. While one is off, `asm_apply` answers
  `capability_disabled`; never change a setting. Enabling a record needs only the autoAssembler gate: explain first.
- All pass: apply with consent as [AOB injection](aob-injection.md) does, or enable the record with consent:
  `record_set_active(ids=[{recordId}], active=true)`; `active` false with a `failure` (such as `assert_failed`) fails.

## Pitfalls

- `accepted` proves the syntax only, not the `assert` and AOB scans (steps 4 and 5 do) nor a later apply or release.
- The check skips commands in `//` and `/* */` comments, not in `{ }`; directives and `USEMONO()` lines count even in
  comments. Names still resolve through Lua callbacks: the shipped luasymbols.lua runs an unknown name as Lua.
- `[DISABLE]` is checked without the symbols `[ENABLE]` registers: a bare `name:` line passes (unless `{$STRICT}`),
  `name+4` is rejected although a release would resolve it; register a label at the exact site instead.
- An active record has already patched its site: its patterns no longer match, and steps 6 and 7 show its own patch
  and names. With consent, disable it first: `record_set_active(ids=[{recordId}], active=false)`.
- Record ids expire when a table loads; find the record again with `record_list()`.

## Report

A table with the columns Check, Result (pass, warn or fail), Evidence and Fix. Then the script's source, the site as
module+offset, each pattern with its match count, the gates it needs and the verdict. Nothing is owned; name any
record you disabled. Background: [Auto Assembler](../Documents/auto-assembler.md),
[x64 injection](../Documents/x64-injection.md) and [AOB signatures](../Documents/aob-signatures.md).
