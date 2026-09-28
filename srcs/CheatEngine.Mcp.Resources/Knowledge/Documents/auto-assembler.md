# Auto Assembler syntax and templates

Auto Assembler (AA) is Cheat Engine's script language for patching a running target: it scans for code, allocates
memory, writes instructions and bytes, and registers symbols. Read this page before you write, check, apply or release a
script with the `asm_*` tools. [x64-injection](x64-injection.md) covers the machine-level rules and
[aob-signatures](aob-signatures.md) the patterns that locate code.

An applied script changes the target's code. Use it only on single-player or offline software the user owns or may
modify, never on online, competitive or anti-cheat-protected games. Explain what a script changes, get the user's
consent before each apply or record activation, and ask them to save first: a bad injection crashes the target
([safety](safety.md)).

## Tools

- `asm_generate_injection(module=..., signature=..., expectedBytes=..., symbolName=..., offset=...)`: this server's AOB
  injection scaffold, as text. It calls nothing in CE: it checks neither the module nor the signature's uniqueness.
- `asm_generate_api_hook(address=..., jumpTarget=..., newCallAddress=..., extension=..., targetSelf=false)`: CE's
  API-hook template, as text.
- `asm_assemble(address=..., instructions=[...], preference=0, skipRangeCheck=false)`: bytes and sizes of 1 to 128
  instructions at a real origin. Never writes.
- `asm_check(script=..., name=...)`: CE's syntax check of `[ENABLE]`, then of `[DISABLE]`. Applies nothing.
- `asm_apply(script=..., name=...)`: runs `[ENABLE]` once and keeps CE's disable information in a patch lease.
- `asm_apply_code_patch(address=..., expectedBytes=..., replacementBytes=..., name=...)`: builds and applies an
  assert-then-replace script for 1 to 64 bytes.
- `asm_release_patch(patchId=...)`: runs that lease's `[DISABLE]` once.
- `asm_list_patches()`: the leases of this plugin activation, also readable as `cheatengine://instance/patches`.

Details:

- Only `asm_check` and the two apply tools need `Mcp:EnableAutoAssembler`. The apply tools are `may_prompt`: CE can
  show a dialog, and the call then waits for the user.
- `script`: 1 to 1,048,576 characters with both `[ENABLE]` and `[DISABLE]` (any case), else `invalid_argument`;
  `asm_check` also refuses more than 1,048,576 UTF-8 bytes (`limit_exceeded`). `name` (up to 256 characters) only
  labels the patch.
- `asm_generate_injection`: `signature` and `offset` are the `pattern` and `offset` results of
  `aob_generate_signature` (nibble wildcards are refused); `expectedBytes`, the 5 to 64 original bytes of whole
  instructions at the injection point; `symbolName` (default `mcpInjection`), 1 to 64 letters, digits and
  underscores, no leading digit, unique.
- `asm_assemble.preference`: 0 none (CE chooses), 1 short, 2 long, 3 far. One instruction per entry, without labels;
  each starts where the previous one ended. `asm_apply_code_patch` accepts the unspaced `bytes` result.

## Gates: how a script is classified

Every script needs `Mcp:EnableAutoAssembler`. Before CE sees it, `asm_apply` (and `table_load`, for each script of a
table) scans the whole text, comments and strings included, and requires more switches for what it can reach:

- `EnableUnsafeLua`: a `{$lua}` block, `luacall`.
- `EnableTargetCodeExecution`: a `{$c}` or `{$ccode}` block, `loadlibrary`, `createthread`, `createthreadandwait`.
- Both of them: a `{$luacode}` block (Lua run from the target through an injected DLL), `include` and `loadbinary`
  (content of host files), any other word before `(` that is not a built-in, and any unknown `{$...}` directive.
- `EnableKernelAccess`: `kalloc`.
- All three: more overlapping `define` aliases than the scan can follow.

Built-ins need nothing more: `alloc`, `allocnx`, `allocxo`, `dealloc`, `globalalloc`, `label`, `define`,
`registersymbol`, `unregistersymbol`, `assert`, the `aobscan` family, `fullaccess`, `readmem`, `reassemble`, `hook`
(`hook5` too), `unhook`, `align`, `struct`, `endstruct`, `errordefine`, `db` to `dq`, and the directives `{$asm}`,
`{$strict}`, `{$try}`, `{$except}`, `{$ifdef}`, `{$ifndef}`, `{$endif}`, `{$define}`, `{$noprologue}`.

- A switch that is off gives `capability_disabled` naming `Mcp:<Setting>`, with `hostEffect` `not_started`: nothing
  ran. `runtime_get_info.gates` shows the switches; only the user changes them ([configuration](configuration.md)).
  Never rephrase the script through `lua_execute` or another tool to get around a switch.
- The scan is lexical and errs on the safe side: every word directly before `(` counts, `define` aliases and comments
  included, so `// damage (float)` makes `damage` an unknown command (`dd (float)100` is fine). So do Lua calls such
  as `print(` in a `{$lua}` block, and Lua-registered commands such as `usemono` from CE's autorun scripts, which start
  data collectors or compile C in the target ([mono-and-dotnet](mono-and-dotnet.md)).
- No tool but `asm_check` screens expressions: CE runs a `$` token not followed by hex digits as Lua, and the shipped
  `luasymbols.lua` evaluates an unknown name as Lua, whatever the switches say. Keep Lua out of addresses.
- Record scripts are not classified: `record_create` with a script and `record_set_active` need only
  `EnableAutoAssembler`, also when a record's `moActivateChildrenAsWell` or `moDeactivateChildrenAsWell` option
  reaches a nested script. [Review](../Workflows/review-aa-script.md) a record's script before activating it.
- The switches are on by default. They are exposure switches, not a sandbox.

## Script structure and processing order

- One `[ENABLE]` and one `[DISABLE]` section; everything after `[DISABLE]` belongs to it.
- Lines above `[ENABLE]` belong to both runs, enable and release. Keep only `define` lines there: an `aobscan*`,
  `assert` or `readmem` of the patched site above `[ENABLE]` fails at release time, when the bytes are already patched.
- `[64-BIT]` ... `[/64-BIT]` and `[32-BIT]` ... `[/32-BIT]` blocks are dropped when they do not match the target.
- One run, in CE's source order: Lua prologues and `{$lua}` blocks run (a returned string replaces the block);
  comments are stripped; every `aobscan*` is resolved, before any `define` is expanded, so a define inside a pattern
  does not work; pass 1 (`loadlibrary` injects, `luacall` runs, `globalalloc` allocates), where a syntax check stops;
  `alloc` memory is allocated; pass 2 assembles and resolves labels; the bytes are written; then `dealloc` frees,
  symbols are unregistered and registered, and `createthread` threads start.
- Nothing is written when a line fails to assemble, but the writes are not atomic: the target keeps running. For code
  that runs constantly, `process_set_paused(paused=true)` before the apply or release and
  `process_set_paused(paused=false)` after. MCP tracks its pause as a resource that blocks a target change; both
  calls are refused while the debugger is stopped.

## Numbers, names and data

- Hex by default: operands, addresses, `db`/`dw`/`dd`/`dq` values, `nop N`, `align N`. `sub rsp,20` subtracts 32.
  Write decimal as `#100`; `$1000` is explicit hex.
- Decimal by default: the sizes of `alloc`, `globalalloc`, `kalloc`, `fullaccess` and `readmem`, and the
  `createthreadandwait` timeout. `alloc(newmem,1000)` is 1000 bytes, `alloc(newmem,$1000)` is 4096.
- Casts give a literal's bit pattern: `dd (float)100`, `dq (double)1.5`, `mov dword ptr [rbx+10],(float)1`.
- Data: `db` bytes (`??` keeps the byte in memory, `'text'` gives the text's bytes), `dw` 2 bytes (`dw 'text'` is
  UTF-16), `dd` 4, `dq` 8. Comments: `//` to the end of the line, `/* */`, or a `{ }` block.
- Addresses: `"game.exe"+1A2B3C`, registered symbols, labels. A `name:` line moves the output to that address or
  places a declared label; each `label(x)` needs exactly one `x:` line.
- Names that read as hex (`add`, `dead`, `beef`, `c1`) are refused as label or alloc names ("... is not a valid
  identifier"); avoid register and mnemonic names too. Registered symbols are global to the CE session: use unique
  names such as `INJECT_HP`, never a bare `INJECT`.

## Commands and directives

- `alloc(name,size,near)`: executable memory (`allocnx` read-write, `allocxo` read-execute). `near` asks for a block
  within +/-2 GB so a 5-byte `jmp` reaches it.
- `dealloc(name)`: frees on release, after the writes, and only when every `alloc` of the enable run is listed; a
  partial list frees nothing. `globalalloc(name,size)` memory is registered once and never freed.
- `label`, `registersymbol`, `unregistersymbol`: declare a name; publish it to CE, records and other scripts; remove
  it (silent when missing). `define(name,text)`: text substitution; never define a name twice.
- `assert(address,bytes)`: fails the script before any write unless the bytes match (wildcards allowed).
- `aobscanmodule(name,module,bytes)`: defines `name` at a match inside `module` (`aobscan`: all readable memory,
  `aobscanregion(name,start,stop,bytes)`: a range, `aobscanex`: executable memory). No match aborts the script; with
  several, CE takes whichever it finds first, so prove uniqueness beforehand.
- `readmem(address,size)`: copies target bytes raw at assembly time, without re-encoding them.
- `reassemble(address)`: re-emits the instruction at an address or registered symbol (not a label) as text, so its
  relative operands are re-encoded for the new place.
- `createthread(address)` runs code in a new target thread after the writes; `createthreadandwait(address,ms)` starts
  one as soon as the lines before it are written, and waits. On x64 wrap the code in `sub rsp,28` and `add rsp,28`,
  then `ret`. No timeout, or 0, waits forever and blocks CE.
- `loadlibrary(file)` injects a DLL and `luacall(statement)` runs Lua in CE, both during CE's check too.
  `kalloc(name,size)`: kernel memory through CE's driver ([kernel](kernel.md)).
- `{$STRICT}`: undeclared names become errors instead of implicit labels. Put it on the line after `[ENABLE]`.
- `{$LUA}` ... `{$ASM}`, each on its own line: Lua run in CE before parsing, during CE's checks too; a returned string
  becomes AA text. `syntaxcheck` is true during a check: return early then ([lua](lua.md)).
- `{$LUACODE ...}` ... `{$ASM}` runs Lua when a target thread reaches it; `{$C}` and `{$CCODE ...}` compile C into the
  target. `{$TRY}` ... `{$EXCEPT}` guards injected code; `jmp` past the except part at the end of the try part.

## What asm_check proves

- CE runs parts of a script while checking it. So before CE sees the script, `asm_check` refuses, as `unsupported`
  with `hostEffect` `not_started`, one that needs a switch beyond `EnableAutoAssembler`, uses `globalalloc` (it
  allocates even during a check) or writes `$` before anything but hex digits, whatever the switches say. That is no
  rejection by CE: review such a script by reading it ([review an AA script](../Workflows/review-aa-script.md)).
- This screen skips `//` and `/* */` comments, which CE removes first; `{ }` comments still count, and so do
  `{$...}` directives, `USEMONO()` lines and lines starting `PREPARECHEADER(` in any comment.
- CE checks `[ENABLE]` and, only when it accepts it, `[DISABLE]`. `accepted` is true only when both pass. On a
  rejection `failedSection` is `enable` or `disable`, and `hostMessages` holds CE's unparsed text, usually
  "Error in line N (line text) :reason" (`hostMessagesTruncated` when cut). A rejection is a result: fix, check again.
- `[DISABLE]` is checked alone, without anything the enable run registers or allocates:
  - Without `{$STRICT}` in `[DISABLE]`, a bare `INJECT_HP:` line passes as a label. `INJECT_HP+6:`, or a bare line
    under a `{$STRICT}` above `[ENABLE]`, is rejected ("This address specifier is not valid") unless the symbol is
    already registered, although a release would resolve it: place a registered label where `[DISABLE]` restores.
  - Not verified on 7.7: CE's public source reads missing disable information for `dealloc(name)` in this check. If
    `hostMessages` points at the `dealloc` line of an unchanged scaffold, report it instead of rewriting the line.
- It proves little: the symbol handler still resolves addresses (Lua callbacks such as `luasymbols.lua` included),
  `assert`, `aobscan*` and `readmem` reads are skipped and `alloc` allocates nothing, so wrong bytes, a lost signature
  or a failed nearby allocation show only at apply time.
- When CE raises during the `[DISABLE]` check, the call fails with `host_refused` and `hostEffect` `unknown`.

## The generated AOB scaffold

This call returns the `script` below (`29 83 A4 07 00 00` is `sub [rbx+000007A4],eax`):
`asm_generate_injection(module="game.exe", signature="29 83 A4 07 00 00 8B 83 ?? ?? ?? ??", expectedBytes="29 83 A4 07 00 00", symbolName="INJECT_HP")`

```
[ENABLE]
aobscanmodule(INJECT_HP,game.exe,29 83 A4 07 00 00 8B 83 ?? ?? ?? ??)
assert(INJECT_HP,29 83 A4 07 00 00)
alloc(newmem,2048,INJECT_HP)
label(code)
label(return)
registersymbol(INJECT_HP)

newmem:
// Add reviewed custom code above the original bytes.
code:
db 29 83 A4 07 00 00
jmp return

INJECT_HP:
jmp newmem
nop
return:

[DISABLE]
INJECT_HP:
db 29 83 A4 07 00 00
unregistersymbol(INJECT_HP)
dealloc(newmem)
```

- Your code goes between `newmem:` and `code:` and runs before the original instruction, with its registers and flags.
  To drop the original (no damage at all), delete the `db` line under `code:` but keep the `code:` line.
- `code:` copies the originals as raw bytes, which is only right when none is RIP-relative or a relative `jmp`, `call`
  or conditional jump. Otherwise replace the `db` line with the instructions as text (the `opcode` of
  `code_disassemble`, which shows absolute operands) or with `reassemble`, and keep the cave near
  ([x64-injection](x64-injection.md)).
- `jmp newmem` takes 5 bytes; one `nop` per extra byte of `expectedBytes` keeps `return:` on the next instruction.
- With `offset` above 0, the scan result is the local `INJECT_HP_aob`. `assert` and an address line use
  `INJECT_HP_aob+<offset in hex>`, where the registered label `INJECT_HP:` sits, and `[DISABLE]` restores through a
  bare `INJECT_HP:` line.
- `newmem`, `code` and `return` are fixed names: rename them before merging two scaffolds into one script. Add
  `{$STRICT}` on the line after `[ENABLE]`.
- Prove the signature first, with no patch active: `aob_find(patterns=["29 83 A4 07 00 00 8B 83 ?? ?? ?? ??"],
  module="game.exe", limit=2)` must report `unique` true ([make a signature](../Workflows/make-aob-signature.md)).
- JIT code (a Mono method) has no module. While `mono_attach` holds the collector, CE resolves `Class:Method` by
  JIT-compiling it in the game, so a script can scan its range:
  `aobscanregion(INJECT_HP,PlayerHealth:TakeDamage,PlayerHealth:TakeDamage+200,<pattern>)`
  ([mono-and-dotnet](mono-and-dotnet.md)).

## API hooks

`asm_generate_api_hook(address="game.exe+4C10", jumpTarget="myHook", newCallAddress="pOriginal")` returns CE's hook
template as `script` (LF line breaks, empty `expectedBytes`). CE resolves both addresses while generating: `myHook`,
your replacement code, must already exist.

- `[ENABLE]` allocates `originalcall` (near `address` on x64), copies the overwritten whole instructions there as text
  with a `jmp` back, stores its address at `newCallAddress` (`dq` on x64, `dd` on x86) and writes the jump to
  `jumpTarget` at `address` (on x64 through `jumptrampoline`, a block near `address`), padded with `nop`. `[DISABLE]`
  restores the bytes and frees what `[ENABLE]` allocated; `newCallAddress` keeps its value.
- `myHook` runs instead of the hooked code and can run the original with `call qword ptr [pOriginal]` on x64.
- `extension` suffixes `originalcall`, `returnhere` and `jumptrampoline`, so two hooks can share a script. Keep
  `targetSelf` false (true hooks CE itself).
- `asm_apply` reads the x64 comment `//special jump trampoline in the current region (64-bit)` as the unknown command
  `region`: it needs `EnableUnsafeLua` and `EnableTargetCodeExecution` unless you delete that comment. Not verified
  live on 7.7: that the template passes `asm_check` and releases cleanly.

## Code patches without a script

This call resolves the address once (here to `7FF6A1B32B3C`) and applies the script below:
`asm_apply_code_patch(address="game.exe+1A2B3C", expectedBytes="29 83 A4 07 00 00", replacementBytes="90 90 90 90 90 90", name="no damage")`

```
[ENABLE]
assert(7FF6A1B32B3C,29 83 A4 07 00 00)
7FF6A1B32B3C:
db 90 90 90 90 90 90

[DISABLE]
7FF6A1B32B3C:
db 29 83 A4 07 00 00
```

- Cover whole instructions: add up `size` and copy `bytes` from `code_disassemble`. Wrong `expectedBytes` fail the
  `assert`, and nothing is written. The patch is bound to that absolute address: to keep a cheat across game
  versions, store an AOB script in a record.
- One multi-byte NOP can replace six single ones: `66 0F 1F 44 00 00` ([NOP patch](../Workflows/nop-patch.md)).
- `asm_assemble(address="game.exe+1A2B3C", instructions=["jmp game.exe+1A2B50"], preference=1)` gives the 2-byte
  `jmp` for a short `jcc`. A near `jcc` (6 bytes) becomes `90` plus the 5-byte `E9` that `asm_assemble.preference` 2
  encodes at the next byte ([conditional branches](x64-injection.md#conditional-branches),
  [patch a branch](../Workflows/patch-branch.md)).

## Jumps and nearby allocation

- The jump to the cave is a 5-byte `E9 rel32` that reaches +/-2 GB. On a 64-bit target, when the cave is farther or
  `far` is written, CE silently emits 14 bytes (`FF 25 00000000` plus the address), overwriting instructions the
  scaffold never copied. CE's own Code injection template (Template menu) uses it when no near block is free.
- With nothing free within reach, CE shows the modal "Nearby allocation error" dialog and `asm_apply` waits. Ask the
  user to answer No ("Failure allocating memory near ..."); Yes, or an earlier "Yes to All", allocates anywhere and
  gives the 14-byte jump ([x64-injection](x64-injection.md)). Allocations with the same `near` share one block;
  injection points far apart each need their own `alloc`.

## Rules for [DISABLE]

- Restore exactly the overwritten range (jump plus padding) through the registered symbol, never by scanning: once
  patched, the signature no longer matches.
- `unregistersymbol` every registered name and `dealloc` every `alloc`. Keep the `assert` in `[ENABLE]`: on a version
  whose bytes differ, the apply fails instead of patching bytes the fixed `db` restore does not match.

## Injection copy: keep a base pointer

To let records follow the object the code touches ([injection copy](../Workflows/injection-copy-base.md)), give a
scaffold for `mov eax,[rbx+000007A4]` a `label(pPlayer)` and `registersymbol(pPlayer)`, an `unregistersymbol(pPlayer)`
in `[DISABLE]`, and this cave:

```
newmem:
  mov [pPlayer],rbx
code:
  db 8B 83 A4 07 00 00
  jmp return
pPlayer:
  dq 0
```

Then `record_create(records=[{description="HP", address="pPlayer", variableType=2, offsets=["7A4"]}])` reads
`[pPlayer]+7A4`. `pPlayer` stays 0 until the game runs the instruction, then holds whichever object ran it last: pick
an instruction only the player executes, or filter ([shared code](../Workflows/shared-code-filter.md)).

## Cycle: check, apply, verify, release

1. Read the site: `code_disassemble(address="game.exe+1A2B3C", count=8)`. Note each `size`, RIP-relative operands and
   relative branches. For each overwritten instruction after the first, `code_get_function(address=...)` must report
   `addressIsJumpDestination` false.
2. Generate the scaffold (or take the user's script) and edit only the cave
   ([AOB injection](../Workflows/aob-injection.md)).
3. `asm_check(script=..., name="infinite HP")` until `accepted` is true (after `unsupported`, review by reading).
4. Explain the change, get consent, and ask the user to save.
5. `asm_apply(script=..., name="infinite HP")`. Keep `patchId` (with its `instanceId` at the gateway) and read
   `hostWarnings`. If `retained` is false, the target changed during the apply: `patchId` is only an attempt id, and
   `requiresManualRecovery` tells whether the patch may remain in the old target.
6. Verify: `code_disassemble(address="INJECT_HP", count=3)` must show `bytes` starting with `E9` (5 bytes), then the
   `nop` padding; `FF25` means the 14-byte fallback, so release at once. Note its absolute `address`: the symbol
   disappears on release.
7. The user tests in the game.
8. `asm_release_patch(patchId=...)`, then `memory_read(address="7FF6A1B32B3C", valueType="bytes", size=6)` must
   return the original bytes.
9. To keep the cheat, release the MCP patch and store the script as a record:
   `record_create(records=[{description="Infinite HP", address="0", variableType=11, script="..."}])`, activate it
   with `record_set_active(ids=[...], active=true)` and save with `table_save(path=...)`. CE runs record scripts
   itself: they are not MCP leases and never appear in `asm_list_patches` ([cheat-tables](cheat-tables.md)).
   - A refused record keeps its state without failing the call: its entry's `failure` gives a `reason` such as
     `aob_not_found`, `assert_failed` or `allocation_failed`, and CE's `text`. With `pending` true, read the record
     later with `record_get`.
   - `record_set_script` refuses an active or activating record (`invalid_state`): deactivate it first.

## Failures and recovery

- `invalid_argument` (nothing ran): a missing section, a script too long, byte strings of different lengths, an
  `offset` outside the signature, a `preference` outside 0 to 3. `limit_exceeded`: at most 128 retained patches.
- A script CE rejects during `asm_apply` fails with `host_refused`, CE's message and `hostEffect` `unknown`: an
  allocation, a `{$lua}` effect or part of the bytes may remain without a lease (so may a `cleanup_unconfirmed` patch).
  Inspect first (`code_disassemble`, `module_find_patches(module="game.exe")`); never re-apply to retry.
- "The bytes at ... are not what was expected": another version, or already patched (check `asm_list_patches` and
  active records). "Error while scanning for AOB's" ("Not all results found") or "The array of byte named ... could
  not be found": the signature broke ([repair after an update](../Workflows/repair-after-update.md)).
- `asm_release_patch` reports a failed release as a result: read `released`. With `retryable` true the lease stays;
  try again later. With `retryable` false it leaves `asm_list_patches` whatever happened; `requiresManualRecovery`
  true means the patch may remain for good: the user restores the original bytes or restarts the target. `release`
  and `hostEffect` use the Client's PascalCase names here (`PartiallyReleased`, `Completed`).
- `canDisable` false in `asm_list_patches`: CE's Lua state was detached or replaced, and a release will be refused.
- Release every patch before `process_attach`: a retained patch makes a process change fail with `busy`, and a patch
  left in a process CE switched away from cannot be disabled, even after switching back. `runtime_release_resources`
  releases every owned resource, patches included: use it only at the end, with consent
  ([clean up a session](../Workflows/cleanup-session.md)).
- After a timeout or a lost connection the apply may have happened: read `asm_list_patches` and disassemble the site
  before any retry. More in [errors-and-recovery](errors-and-recovery.md).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Auto_Assembler
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:Commands
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:reassemble
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:LUA_ASM
- https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine (public source of about 7.5):
  `autoassembler.pas` (processing order, syntax check, comment removal, decimal sizes, `dealloc`, `globalalloc`, the
  nearby allocation dialog, error texts), `Assemblerunit.pas` (far jumps), `globals.pas` (nearby allocation answers),
  `frmautoinjectunit.pas` (templates, API hook)
- https://fearlessrevolution.com/viewtopic.php?t=18846 (several injection points near one block)
- Cheat Engine 7.7: `celua.txt` (`autoAssemble`, `autoAssembleCheck`, `generateAPIHookScript`,
  `registerAutoAssemblerCommand`), `LuaHandler.pas` (`generateAPIHookScript` returns two texts), the autorun files
  `newdefaulttemplates.lua`, `luasymbols.lua` and `monoscript.lua`, and the error texts in `cheatengine-x86_64.exe`
