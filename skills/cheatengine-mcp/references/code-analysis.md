# Disassembly and code analysis

Read code to learn what a value means, where its base comes from, and where to patch. All tools here are read-only
except the dissect jobs (which fill CE's shared dissect data), `code_clear_dissect` and `code_set_comment`. Addresses
are uppercase hex without `0x`.

## Tools

| Tool                                                                                            | Use                                                                                                          |
|-------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------|
| `code_disassemble(address, count, before?, format?)`                                            | Instructions with `address`, `size`, `bytes`, `text`, `symbol?`, `comment?`, plus `nextAddress` (up to 512). |
| `code_decode(address, count)`                                                                   | Structured fields for up to 64 instructions.                                                                 |
| `code_disassemble_bytes(bytes, origin?, maxInstructions?)`                                      | Disassemble bytes you supply (up to 16 KiB) without reading the target.                                      |
| `code_get_function(address)`                                                                    | CE's estimated function `start`/`end`, `symbol?`, `addressIsJumpDestination`; always `estimated:true`.       |
| `code_start_dissect`, `code_start_search` -> `code_poll_job(jobId, afterSequence, limit)`       | Module-scale analysis as jobs: each start returns a `jobId`; stop with `runtime_stop_job(jobId)`.            |
| `code_find_references(address)`, `code_find_strings(contains?)`, `code_list_functions(module?)` | Paged reads of CE's dissect data.                                                                            |
| `code_clear_dissect`                                                                            | Clears CE's dissect data (refused as `busy` while a dissect job runs).                                       |
| `code_get_comments(addresses)`, `code_set_comment(address, comment?, header?)`                  | CE's Memory View comments and headers.                                                                       |
| `symbol_resolve`, `memory_get_address_info(includeRtti)`                                        | Names for addresses, module+offset, RTTI class names.                                                        |

The disassembly is also readable as the live resource `disassembly/{address}{?count}` under the instance URI.

## `code_disassemble`

- Typed `bytes` and `size` are authoritative: use them to count bytes for patches and signatures.
- The display columns are already corrected. The pinned Client/SDK disassembly columns are misordered on CE 7.7, so MCP
  reads the text through CE's actual `extra, opcode, bytes, address` order. Never re-parse `disassemble()` strings in
  your own Lua on the assumption that celua.txt's documented order is right.
- `before=N` (up to 64) walks backwards first. x86 instructions have variable length, so a backward walk is a guess
  (`backwardEstimated:true`): it can land mid-instruction and show plausible garbage. Anchor on a known boundary
  instead: start at `code_get_function(...).start` or at a capture's confirmed instruction and walk forwards.
- Page with `nextAddress`. `count` is capped at 512.
- Reading code does not need the debugger and does not disturb the target.

## `code_decode`

Use it when you need facts rather than text: `prefix`, `mnemonic`, `operands`, `isJump`, `isCall`, `isRet`,
`isConditionalJump`, `isRep`, `memoryOperand?`, `immediate?` and `wildcardMask`.

- `isJump` is true for anything that can change RIP except `ret`, so calls are included; combine it with `isCall` and
  `isConditionalJump`.
- `wildcardMask` marks the bytes CE considers unstable (displacements, relative targets): the starting point of an AOB
  signature ([aob-signatures](aob-signatures.md)).
- `memoryOperand` and `immediate` give branch targets and referenced addresses without parsing text.

## Function bounds

`code_get_function` returns CE's guess of the enclosing function. It can be wrong for code without symbols, with tail
calls or with jump tables. Treat `start`/`end` as a hint, confirm the prologue by disassembling at `start`, and check
`addressIsJumpDestination` before patching near a branch target.

## Dissect jobs and references

CE's code dissector is one global database per CE instance, shared with its UI ("Dissect code"). It maps who calls,
jumps to or reads what.

1. `code_start_dissect(module=...)` (or `address`+`size`, up to 1 GiB) starts a job in time slices. Poll
   `code_poll_job(jobId, afterSequence, limit)` until done; stop early with `runtime_stop_job`.
2. `code_find_references(address)` lists `from` addresses and reference types for a function or data address. Only
   dissected code is covered: no hit is not proof of no caller.
3. `code_find_strings(contains, includeReferences)` finds strings the code references: search a UI or log message ("Game
   Over", "ammo") to reach the logic that uses it.
4. `code_list_functions(module)` lists referenced function entry points.
5. `code_start_search(module, mode, ...)` streams hits: `mode="text"` matches disassembly text or a `mnemonic`,
   `mode="references"` finds instructions that use `referencesAddress`, `mode="rip_relative"` lists RIP-relative
   instructions (`includeJumpsAndCalls` to add branches). Hits are capped at 65,536.
6. `code_clear_dissect` only when the user wants to reset CE's database; it also affects CE's UI.

Dissect data survives until cleared or CE closes, and goes stale after the game updates.

## RIP-relative globals as static bases

In x64, globals are reached with RIP-relative operands, displayed as `[game.exe+2F1A30]`. A static
`mov rax,[game.exe+2F1A30]` followed by `mov ecx,[rax+7A4]` gives a one-level pointer `[game.exe+2F1A30]+7A4` without
any pointer scan.

- The address is next instruction + signed 32-bit displacement. After a game update the displacement changes: re-derive
  it with an AOB on the instruction (displacement bytes wildcarded) and recompute, or read it from an AA script
  ([pointers](pointers.md)).
- `code_start_search(mode="rip_relative")` over the main module lists candidate globals; `code_find_references` on a
  global shows every function using it.
- Singletons and managers (`GameManager`, `LocalPlayer`) usually live in such globals.

## Reading value semantics from code

| Pattern                                     | Meaning                                                                                    |
|---------------------------------------------|--------------------------------------------------------------------------------------------|
| `mov [rbx+7A4],eax`                         | 4-byte integer at offset `7A4` of the object in `RBX`                                      |
| `movss [rbx+48],xmm0` / `movsd`             | 32-bit float / 64-bit double                                                               |
| `sub [rbx+7A4],eax`, `dec`, `add`           | Damage, spending or gain; the other operand is the amount                                  |
| `imul eax,[rbx+10]`                         | Multiplier (level, stack count)                                                            |
| `cvtsi2ss xmm0,eax` / `cvttss2si`           | The game converts between int and float: the stored type may differ from the displayed one |
| `cmp dword ptr [rbx+7A4],0` + `jle`         | Death or empty check                                                                       |
| `test byte ptr [rbx+30],1` + `jz`           | Boolean flag or bit field                                                                  |
| `mov eax,[rbx+rcx*4+10]`                    | Array of 4-byte elements at `+10`, index in `RCX`                                          |
| `lea rcx,[rbx+20]`                          | Address computation, no memory access: an embedded sub-object                              |
| `mov rax,[rcx]` + `call qword ptr [rax+18]` | C++ virtual call: the first qword of the object is its vtable                              |
| `movzx` / `movsx`                           | Unsigned / signed small value                                                              |

- Several instructions often write one value: one sets it, one adds, one clamps to a maximum. Patch the one that matches
  the intent.
- Identify classes with `memory_get_address_info(includeRtti=true)` on an object or vtable address, then lay out fields
  with [structures](structures.md).
- Floats in `XMM` registers appear in `debugger_get_context(include=...)` when you need live values.

## Comments and headers

`code_set_comment(address, comment, header)` writes CE's Memory View annotations: `""` clears a field, omitting it keeps
it. They are CE-owned, visible to the user and outlive the plugin. Annotate only when the user wants notes kept, prefix
them (for example `MCP:`), and read existing ones with `code_get_comments` before overwriting.

## Sources

- https://wiki.cheatengine.org/index.php?title=Help_File:Memory_view
- https://wiki.cheatengine.org/index.php?title=Tutorials:AOBs
- https://learn.microsoft.com/windows-hardware/drivers/debugger/x64-architecture
- https://learn.microsoft.com/cpp/build/x64-software-conventions
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
