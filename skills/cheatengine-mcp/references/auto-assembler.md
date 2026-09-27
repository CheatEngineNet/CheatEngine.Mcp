# Auto Assembler syntax and templates

Auto Assembler (AA) is CE's script language for patching target code: it allocates memory, writes instructions and
bytes, and registers symbols. An AA script is code execution in the target. Every tool that runs a script (`asm_check`,
`asm_apply`, `asm_apply_code_patch`, script records) needs `EnableAutoAssembler` (on by default); some directives need
more gates, listed below. The text generators, `asm_assemble`, `asm_list_patches` and `asm_release_patch` need no gate.
Gates are exposure switches, not a sandbox.

## Tools

| Tool                                                                                                          | Use                                                                                                                                                       |
|---------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `asm_generate_injection(kind, address, symbolName?, commentRadius?, farJump?, baseScript?)`                   | CE's own `code`, `aob` or `full` injection template, as text only.                                                                                        |
| `asm_generate_api_hook(address, hookTarget, originalCallSymbol?, nameExtension?)`                             | API-hook template (enable and disable sections), as text only.                                                                                            |
| `asm_assemble(address, instructions, preference?)`                                                            | Bytes and size of up to 64 lines at an address; never writes.                                                                                             |
| `asm_check(script, name?)`                                                                                    | CE syntax check of the `[ENABLE]` section. `accepted:false` with `messages` is a verdict, not an error; `requiredGates` lists the gates the script needs. |
| `asm_apply(script, name?)`                                                                                    | Runs `[ENABLE]` once and keeps CE's disable information in a patch lease; returns `patchId`.                                                              |
| `asm_apply_code_patch(address, bytes \| instructions \| nopInstructions, requireInstructionBoundary?, name?)` | Simple reversible patch: MCP builds the script with an `assert` on the original bytes and returns `originalBytes`/`newBytes`.                             |
| `asm_release_patch(patchId)`                                                                                  | Runs the script's `[DISABLE]` once.                                                                                                                       |
| `asm_list_patches`                                                                                            | Owned patches, with `canDisable` and `appliedAfterTargetChange`.                                                                                          |

The generators produce text only; nothing reaches the target until `asm_apply`.

## Syntax essentials

- Sections: `[ENABLE]` runs on apply, `[DISABLE]` on release. Lines before `[ENABLE]` run in both passes.
- Numbers are hexadecimal by default: `sub rsp,20` subtracts 32. Write decimal as `#100`; `$` is an explicit hex prefix
  (`$1000`).
- Floats: `(float)1.5` and `(double)1.5` convert a literal to its bit pattern, e.g. `dd (float)100` or
  `mov dword ptr [rbx+10],(float)1`. Use `dq` for doubles.
- Data: `db` bytes, `dw` 2 bytes, `dd` 4 bytes, `dq` 8 bytes.
- Addresses: module-relative (`"game.exe"+1A2B3C`), registered symbols, labels. `name:` moves assembly to that address
  or places a declared label there.
- Comments use `//` or `{ }`.

## Commands and directives

| Command                                                                                           | Meaning                                                                                                                                             | Extra gate                  |
|---------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------|
| `label(name)`                                                                                     | Declares a name placed later with `name:`                                                                                                           |                             |
| `alloc(name,size,near)`                                                                           | Allocates memory; the third argument asks for memory within +/-2 GB of `near` so a 5-byte jump reaches it                                           |                             |
| `dealloc(name)`                                                                                   | Frees an `alloc` block on disable                                                                                                                   |                             |
| `globalalloc(name,size)`                                                                          | Allocation registered as a symbol that persists after `[DISABLE]`; reuse it rather than leaking a new block per enable                              |                             |
| `registersymbol(name)` / `unregistersymbol(name)`                                                 | Adds/removes a user symbol visible to CE, records and later scripts                                                                                 |                             |
| `aobscan(name,bytes)`, `aobscanmodule(name,module,bytes)`, `aobscanregion(name,start,stop,bytes)` | Defines `name` at the first pattern match; fails the script when there is none                                                                      |                             |
| `define(name,text)`                                                                               | Text substitution                                                                                                                                   |                             |
| `assert(address,bytes)`                                                                           | Fails the script unless the bytes at `address` match: guards against wrong game versions and double application                                     |                             |
| `readmem(address,size)`                                                                           | Emits a raw copy of target bytes                                                                                                                    |                             |
| `reassemble(address)`                                                                             | Re-emits the instruction at `address` with its address operands recomputed for the new location; takes an address or registered symbol, not a label |                             |
| `fullaccess(address,size)`                                                                        | Makes a range readable, writable and executable                                                                                                     |                             |
| `include(file)`, `loadbinary(address,file)`                                                       | Pull in a file                                                                                                                                      |                             |
| `{$STRICT}`                                                                                       | Undeclared names become errors instead of lookups: always use it                                                                                    |                             |
| `{$TRY}` / `{$EXCEPT}`                                                                            | Exception guard around injected code                                                                                                                |                             |
| `{$LUA}` ... `{$ASM}`, `{$LUACODE}`, `luacall(...)`                                               | Runs Lua in CE (at check or apply time; `{$LUACODE}` when target code runs)                                                                         | `EnableUnsafeLua`           |
| `{$C}`, `{$CCODE}`, `loadlibrary(...)`, `createthread(...)`                                       | Compiles or loads code into the target, or starts a thread                                                                                          | `EnableTargetCodeExecution` |

Any command registered by Lua (for example CE's Mono `usemono`) is unknown to MCP and requires
`EnableTargetCodeExecution`; `requiredGates` in the `asm_check` result tells you what a script needs. Treat `asm_check`
as able to run `{$LUA}` blocks: it is not read-only. An accepted check does not prove the apply will succeed, and it
does not check `[DISABLE]` at all, so read that section yourself.

## Rules for `[DISABLE]`

- Restore every byte `[ENABLE]` changed, with exactly the original bytes (`db ...`, or the saved copy).
- `unregistersymbol` everything registered; `dealloc` everything allocated.
- Registering the injection symbol matters: once patched, the AOB no longer matches, so `[DISABLE]` finds the address
  through the symbol.
- Symbol names are global in CE: pick unique names (`INJECT_HP`, not `INJECT`) so two scripts do not collide.

## Templates

Prefer `asm_apply_code_patch(address, nopInstructions=...)` for a NOP. The manual form, with an assert on the 6-byte
`sub [rbx+000007A4],eax` (`29 83 A4 07 00 00`):

```
[ENABLE]
assert("game.exe"+1A2B3C,29 83 A4 07 00 00)
"game.exe"+1A2B3C:
  db 90 90 90 90 90 90

[DISABLE]
"game.exe"+1A2B3C:
  db 29 83 A4 07 00 00
```

Code injection at a fixed address (`kind="code"`), with the `full` template's `define`/`assert` guard:

```
{$STRICT}
define(address,"game.exe"+1A2B3C)
define(bytes,29 83 A4 07 00 00)

[ENABLE]
assert(address,bytes)
alloc(newmem,$1000,address)
label(code)
label(return)

newmem:
  // new code; registers and flags as they were at address
code:
  sub [rbx+000007A4],eax
  jmp return

address:
  jmp newmem
  nop
return:

[DISABLE]
address:
  db bytes
dealloc(newmem)
```

AOB injection (`kind="aob"`), which survives game updates that move code:

```
{$STRICT}
[ENABLE]
aobscanmodule(INJECT_HP,game.exe,29 83 A4 07 00 00 8B 83 ?? ?? ?? ??)
alloc(newmem,$1000,INJECT_HP)
label(code)
label(return)

newmem:
code:
  sub [rbx+000007A4],eax
  jmp return

INJECT_HP:
  jmp newmem
  nop
return:
registersymbol(INJECT_HP)

[DISABLE]
INJECT_HP:
  db 29 83 A4 07 00 00
unregistersymbol(INJECT_HP)
dealloc(newmem)
```

Build and prove the pattern first ([aob-signatures](aob-signatures.md)): it must match once, with patches disabled.

Injection copy: store the base register so records can use `[playerBase]+7A4`:

```
{$STRICT}
[ENABLE]
aobscanmodule(INJECT_PB,game.exe,8B 83 A4 07 00 00 85 C0)
alloc(newmem,$1000,INJECT_PB)
label(code)
label(return)
label(playerBase)
registersymbol(playerBase)

newmem:
  mov [playerBase],rbx
code:
  mov eax,[rbx+000007A4]
  jmp return
playerBase:
  dq 0

INJECT_PB:
  jmp newmem
  nop
return:
registersymbol(INJECT_PB)

[DISABLE]
INJECT_PB:
  db 8B 83 A4 07 00 00
unregistersymbol(INJECT_PB)
unregistersymbol(playerBase)
dealloc(newmem)
```

The stored value is 0 until the code runs, and holds whichever entity ran it last: pick an instruction only the player
executes. On shared code, filter first (`cmp` on a discriminator, then `jne code`; see [structures](structures.md)).

## Moving instructions

The template copies the overwritten instructions into `code:` as text. Instructions with RIP-relative operands or
relative jumps/calls encode distances from their own address, so a raw copy (`readmem`) breaks at a new location. Keep
them as text with absolute or symbolic operands, or use `reassemble`, and allocate near so a +/-2 GB displacement still
fits. Details in [x64-injection](x64-injection.md).

## Cycle

1. Find the instruction ([debugger](debugger.md), [code-analysis](code-analysis.md)); read it with `code_disassemble`.
2. `asm_generate_injection` (or write the script), then edit only the `newmem:`/`code:` part.
3. `asm_check`. Fix every message; read `requiredGates`.
4. Ask the user to save their game: a bad injection crashes the target.
5. `asm_apply(script, name)`. Keep `patchId` with the `instanceId`. Read `warnings`; `appliedAfterTargetChange` means it
   landed in another process than expected: release it.
6. Verify with `code_disassemble(address=<injection point>)`: expect `jmp` to the cave and correct NOP padding;
   disassemble the cave too.
7. The user tests in the game.
8. `asm_release_patch(patchId)`, then `code_disassemble` or `memory_read` to confirm the original bytes are back.
9. To keep the cheat, store the script in the address list (`record_create` with a script record, or
   `record_set_script`) and `table_save`. Record scripts are activated by CE and are not MCP patch leases.

## Failures and recovery

- `capability_disabled`: a gate is off. Report it; never rephrase the script through `lua_execute` to get around it.
- Release runs `[DISABLE]` once and is never retried by the Client. An incomplete release (`partial_effect`, or
  `hostEffect` `cleanup_unconfirmed`) means the patch may remain and cannot be disabled through MCP any more: report
  manual recovery (restart the target) instead of re-applying.
- Release every patch before `process_attach`. A patch applied in one process cannot be disabled after CE switches
  targets, even if you switch back.
- `canDisable:false` in `asm_list_patches` means CE's Lua state changed; the release will be refused and the patch
  stays.
- After a timeout or transport loss the apply may have happened: inspect with `code_disassemble` and `asm_list_patches`
  before any retry.

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Auto_Assembler
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:Commands
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:reassemble
- https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs
- https://forum.cheatengine.org/viewtopic.php?t=605605
- https://forum.cheatengine.org/viewtopic.php?t=572465
