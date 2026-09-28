# Disassembly and code analysis

Read the target's code to learn what a value means, where its base pointer comes from, which branch decides an outcome
and where a patch would go. Read this page before tracing logic, deriving a static base from an instruction or choosing
a patch site. The code tools need neither the debugger nor a gate.

## Which tool answers which question

| Question | Start with |
|---|---|
| What instructions are at this address? | `code_disassemble(address="game.exe+1A2B40", count=40)` |
| What exactly is this one instruction (bytes, length)? | `code_decode(address="game.exe+1A2B40")` |
| What do these bytes decode to? | `code_disassemble_bytes(hexadecimalBytes="48 8B 05 F9 1F 2F 00")` |
| Which function is this address in? | `code_get_function(address="...")` |
| Which paths run through a function, what does it call? | `code_get_function_graph(address="<entry>")` |
| Which instructions call, jump to or load this address? | a `code_start_dissect` job, then `code_find_references` |
| Which strings does the code use? | a `code_start_dissect` job, then `code_find_strings` |
| Which instructions contain this text? | a `code_start_search` job |
| Which API does the game import, through which slot? | `module_list_imports` |
| Where is a function by name? | `symbol_find`, `module_list_exports` |
| What is this address, which class is this object? | `symbol_resolve`, `memory_get_address_info` |
| Notes the user sees in CE's Memory View | `code_get_comments`, `code_set_comment` |

The live resource `cheatengine://instance/disassembly/7FF6A1B2C3D0?count=40` serves the same disassembly (count 20 by
default, 1 to 1024); through the gateway, `cheatengine://instances/{instanceId}/` replaces `cheatengine://instance/`.

Only three tools change anything, and only Cheat Engine (CE) state, never the target: `code_start_dissect` fills CE's
shared dissector data, `code_clear_dissect` wipes it and `code_set_comment` writes a Memory View comment. Addresses in
results are uppercase hex without `0x`.

## Reading instructions

Every decoded instruction has `address`, `addressText` (CE's address column), `opcode` (mnemonic and operands), `extra`
(CE's annotation, such as the value at an absolute operand; often empty), `text` (`opcode` plus `extra`),
`bytes` (hex without separators) and `size`.

- `code_disassemble` decodes `count` instructions forward (1 to 1024, default 20). `code_disassemble.before` (0 to 1024)
  prepends CE's estimate of the preceding instructions; both together are at most 1024. The output `address` is the
  one you asked for; the list starts at the earliest predecessor. To continue, call again at the last instruction's
  `address` plus its `size`.
- x86 instructions have variable length, so a backward walk is a guess: it can land inside an instruction and show
  plausible garbage. Anchor on a known boundary instead: `code_get_function.startAddress`, a `fromAddress` from
  `code_find_references`, a call target or the instruction a debugger capture reported.
- `code_decode` returns one `instruction` and its `length`. At an address that is not an instruction start it silently
  decodes a different instruction.
- Count patch and signature bytes from `bytes` and `size`: they are read from the target in the same call.
- The tools use CE's Lua `disassemble`, which shows no module or symbol names: RIP-relative operands and branch
  targets appear as the computed absolute address, a 1-byte displacement as 2 hex digits (`[rbx+48]`) and a 4-byte
  one, which offsets from 80h up need, as 8. With game.exe at 7FF6A1B00000, `mov rax,[7FF6A1DF1A30]` reads
  game.exe+2F1A30, `call 7FF6A1B04C10` calls game.exe+4C10 and `mov ecx,[rax+000007A4]` reads offset 7A4. Name an
  address with `symbol_resolve(expressions=["7FF6A1DF1A30"])` (its `name`), and search operands in this printed form
  (a search for `game.exe+` never matches); CE's Memory View may show module names instead.
- `code_disassemble_bytes(hexadecimalBytes="48 8B 05 F9 1F 2F 00", origin="game.exe+1A2B40")` decodes bytes you supply
  (a dump, an `asm_assemble` result) without reading the target; `origin` is the address relative operands are
  computed from (0 without it). CE 7.7's `disassembleBytes` decodes only the first instruction, so send one
  instruction at a time. `text` is CE's raw line: address, bytes and instruction, separated by ` - `.
- Check that `opcode` holds a mnemonic: CE 7.7's `splitDisassembledString` returns extra, opcode, bytes, address,
  not the celua.txt order. If `opcode` shows hex bytes and `extra` an address, the host swapped the columns (also in
  `text`, which `code_start_search` matches): tell the user and decode with `code_disassemble_bytes`, passing `bytes`
  and the `address` as `origin`.

## Functions and control flow

`code_get_function(address="...", jumpRange=4096)` returns CE's guess of the enclosing function: `found`,
`startAddress`, `endAddress` (exclusive), `size`, and `addressIsJumpDestination` (whether CE sees a jump to the
address within `jumpRange` bytes). The bounds are a heuristic, not symbols, and can be wrong: disassemble at
`startAddress` to confirm the entry. Check `addressIsJumpDestination` before patching near an address: a patch that
swallows a jump target breaks the code ([x64 injection](x64-injection.md)).

`code_get_function_graph(address="<entry>", maxInstructions=1024, maxBytes=65536, includeInstructions=false)` builds
the control-flow graph of the function that starts at `address`:

- Start at a real entry: a direct call target, an export, a symbol from `symbol_find`, or a confirmed `startAddress`.
  Direct branches are followed only inside the window from `address` to `address` plus `maxBytes`, so a branch to
  code below the entry counts as outside it.
- `blocks` come in address order with `start`, `end` (exclusive), `size`, `instructionCount`, `terminator` and
  `successors` (block starts inside the window, the taken target first); `code_get_function_graph.includeInstructions`
  adds each block's instructions.
- `calls` lists every call site with `from`, `indirect`, `text` and, for a direct call, `target` and `symbol`. Calls
  are listed, not entered: graph a callee separately. The walk assumes every call returns, so after a call that never
  does it decodes whatever follows. For an indirect call through one fixed memory slot (an import slot, a
  function-pointer global), `slot` is that slot: `memory_read(address="<slot>", valueType="pointer")` gives the
  current target. `slot` is omitted for a register or computed target and for an FS- or GS-relative slot, such as a
  call through the TEB, whose segment base the instruction does not hold.
- `truncated` is true when `maxInstructions` (up to 4096) or the window (`maxBytes` up to 1048576) stopped the walk:
  raise them or graph again from a successor. `undecodable` lists addresses that could not be decoded.
- It decodes in batches of 256 per dispatch and fails with `target_changed` if CE selects another process in between.
  It decodes x86 and x64 only: a target that reports another instruction set, or a ceserver or file target that
  reports none, is refused as `unsupported`.

| `terminator` | How the block ends |
|---|---|
| `return` | a `ret` |
| `jump` | an unconditional direct jump inside the window |
| `conditional` | `jcc`, `loop`, `jrcxz`: the taken target first, then the next instruction (or `externalTargets`) |
| `fallthrough` | runs into the next block, which starts at a branch target or a join |
| `indirect` | a register or memory target, not followed (`jmp rax`, a jump table): `hasIndirectSuccessor` |
| `trap` | `int3`, `int 29h` (fail fast), `ud0`, `ud1`, `ud2` or `hlt`: never continues |
| `external` | a direct jump out of the window, listed in `externalTargets` (often a tail call) |
| `limit` | no successors: the window or `maxInstructions` stopped the walk here, the next code was not decoded |

A `conditional` block whose two successors lead to different outcomes (death, purchase, cooldown) is where
[patch a branch](../Workflows/patch-branch.md) applies; [trace the logic](../Workflows/trace-logic.md) combines the
graph with debugger hits.

## Dissect jobs: references, strings and functions

CE's code dissector is one database per CE instance, shared with scripts and with CE's Dissect Code window, which
clears it when the user starts a dissect there. A pass records an instruction only when its last or only operand is a
plain address: calls, jumps and other uses such as loads and `lea`. A `call` or `jmp` through a memory slot is recorded
against the address the slot held at that moment. Not recorded: an address in the first of two operands (a store such
as `mov [7FF6A1DF1A30],eax`), mnemonics starting with `c` other than `call` (`cmp`, `cmovcc`, `comiss`, `cvtsi2ss`)
and `[register+offset]` operands. Data accumulates across passes until `code_clear_dissect` runs or CE closes; it
holds absolute addresses, so it goes stale when the game restarts or updates.

1. Pick the range: `module_get(module="game.exe")` lists `sections` with `address`, `size` and `executable`. Dissect
   code sections in windows of at most 1 MiB, the window most likely to hold the code first.
2. `code_start_dissect(address="<section address>", size=1048576)` returns a `jobId` at once. The pass itself is one
   native CE call on CE's main thread: CE's window freezes and other tool calls queue behind it. Only one pass runs
   at a time; a second start is `busy`.
3. `code_poll_job(jobId="...", afterSequence=0)` until `job.state` is `completed` (or `failed`, `target_changed`;
   `not_found`: the job expired, was stopped or ended with the plugin). A dissect job emits one item: kind `dissect`,
   the dissected `address` and `size`. Then `runtime_stop_job(jobId="...")`. By default at most 16 jobs are retained
   and a job lives 120 s (`code_start_dissect.lifetimeSeconds` accepts up to 300); an expired job discards its events,
   not CE's data.
4. Read the results in pages of up to 1000, following `nextOffset`:
   - `code_find_references(address="game.exe+2F1A30", offset=0, limit=100)`: `references` with `fromAddress`,
     `toAddress` and `kind`, CE's jump type as text: `0` call, `1` unconditional jump, `2` conditional jump, `3` memory
     (data) reference. Sorted by ascending numeric `fromAddress`; `exact` is false beyond 100000 references.
   - `code_find_strings(textContains="game over", offset=0, limit=100)`: `strings` with `address` and `text`, matched
     case-insensitively, in ascending address order. CE counts an address as a string when it starts with 5 printable
     ASCII or 4 printable UTF-16 characters, and reads at most 512 bytes of it, so very short literals are missing.
     `exact` is false beyond 100000 dissector entries, and `total` is then a lower bound.
   - `code_list_functions(offset=0, limit=100)`: the call targets the dissector saw, in CE's order. It is not a symbol
     list.
5. An empty result is not proof that nothing uses the address: besides the operand forms above, calls through
   vtables or function pointers, undissected windows and JIT code are missing. Pages are not snapshots: another pass
   or a clear between pages shifts them. A `timeout` while reading means the dissector holds too much: narrow the
   filter, or clear and dissect less.
6. `code_clear_dissect()` only when the user asks: it also erases dissect data the user made in CE, and it is `busy`
   while an MCP dissect job runs. Comments are not affected.

## Searching instruction text

`code_start_search(address="<function start>", size=65536, textContains="+000007A4]", lifetimeSeconds=300)` sweeps
forward from `address` and emits an item (kind `search`, `address`, `instruction`) for each instruction whose `text`
contains `textContains`, case-insensitively. Poll it with `code_poll_job` and stop it with `runtime_stop_job`; while it
runs it blocks a target switch.

- Useful queries: a field offset as CE prints it (`+000007A4]`, or `+48]` below 80h), to find every instruction that
  touches it, stores included; a global by its absolute address (`7FF6A1DF1A30`); a mnemonic (`cvttss2si`, `comiss`).
- It is a linear sweep: start at an instruction boundary such as a function or section start. Data inside code
  desynchronizes the decode for a few instructions. Unreadable memory in the range ends the job as `failed`; its
  matches stay pollable until the job expires.
- It decodes one instruction per dispatch, so it is slow. Keep the range to what you need, raise
  `code_start_search.lifetimeSeconds` (up to 300) and read the matches before the job expires: an expired job discards
  them all. `job.progressDone` counts bytes.
- `code_start_search.maximumResults` is the buffer size, not a stop condition: once the buffer is full the oldest
  matches are evicted and counted in `dropped`. Narrow the range or the text instead.

## Imports, exports and symbols

- `module_list_imports(module="game.exe", nameContains="QueryPerformance")` lists imported functions: `dll`,
  `slotAddress` (the IAT slot the code calls through), `delayLoaded`, `value` (the pointer the slot holds now), `name`
  or `ordinal`, `targetModule` and `targetSymbol`. After a dissect, `code_find_references(address="<value>")` lists
  the calls through the slot (kind `0`), since CE records them against the slot's content; without one,
  `code_start_search(textContains="<slotAddress>")` over the code finds the calls that name the slot. Timer, input,
  file and random-number imports lead to game logic quickly.
- A delay-loaded slot holds the module's loader stub until the first call; a `value` outside the named DLL is not
  proof of a hook (`api-ms-win-*` sets resolve into `kernelbase.dll` and others).
- `module_list_exports(module="kernel32.dll", nameContains="Sleep")` lists `ordinal`, `name` and `address`, or the
  `forwarder` of a forwarded export. An export's `address` is a function entry, a good start for a graph.
- `symbol_find(nameContains="Health", module="game.exe")` searches the registered symbols, the registered symbol
  lists (`{$C}` functions, IL2CPP methods as CE enumerates them) and CE's main list (exports, PDB symbols); .NET,
  Mono JIT and module names are not in it. `symbolsLoaded` false means CE is still loading symbols or IL2CPP methods:
  retry before concluding a name is absent. Each call copies all these lists on CE's main thread, which can block CE
  for a second or more once Windows PDB symbols or IL2CPP methods are loaded.
- `symbol_resolve(expressions=["7FF6A1B2C3D0"])` names an address; a module+offset name means it is inside an image.
  `memory_get_address_info(addresses=["..."], includeRtti=true)` gives the `section` (`.text`, `.rdata`, `.data`), the
  `region` with its protection and, for an MSVC C++ object with a vtable, its RTTI class name.
- When `module_get` reports a `pe.pdb` the user has, `symbol_add_module(path="C:/Games/Game/game.pdb",
  baseAddress="game.exe")` adds function names to `symbol_resolve`, `symbol_find` and graph call symbols; the
  disassembly text stays hex. A large PDB blocks CE for seconds.

## RIP-relative globals: static bases from code

x64 code usually reaches the globals and statics of its own image through a RIP-relative operand, `[rip+disp32]`,
whose signed 32-bit displacement counts from the end of the instruction: the printed target is the instruction's
`address` plus its `size` plus the displacement, the 4 bytes after the ModRM byte in `code_decode`'s bytes. The
examples assume game.exe at 7FF6A1B00000, so 7FF6A1DF1A30 is game.exe+2F1A30.

| Code | What is static |
|---|---|
| `mov rax,[7FF6A1DF1A30]` then `mov ecx,[rax+000007A4]` | a pointer: the value is at `[game.exe+2F1A30]+7A4` |
| `lea rcx,[7FF6A1DF1A30]` then `mov eax,[rcx+000007A4]` | the object itself: the value is at `game.exe+2F1A30+7A4` |
| `mov eax,[7FF6A1DF1A30]` | the value itself is the global |

- Confirm with `memory_read(address="[game.exe+2F1A30]+7A4", valueType="int32")` against what the game shows, and again
  after a restart: ASLR moves the module, the module+offset stays.
- Managers and singletons (`GameManager`, `LocalPlayer`) often sit in such globals, so a RIP-relative load is often
  the fastest way to a base, before any pointer scan ([pointers](pointers.md)). After a dissect,
  `code_find_references(address="game.exe+2F1A30")` lists the loads and `lea`s of it (kind `3`); stores to it need a
  `code_start_search` for `7FF6A1DF1A30`.
- A game update changes the module offset and every displacement. Re-find the instruction with an AOB whose
  displacement bytes are wildcarded, then read the new target from its disassembly
  ([AOB signatures](aob-signatures.md), [make an AOB signature](../Workflows/make-aob-signature.md),
  [repair after an update](../Workflows/repair-after-update.md)).
- 32-bit code has no RIP-relative form: a global is an absolute address in the instruction bytes, rewritten by the
  loader's relocations. CE prints it as 8 hex digits; wildcard it in AOBs too.
- Never copy RIP-relative instructions or relative branches as bytes: their displacement is only valid at the original
  address. Re-encode them from the `opcode` text ([x64 injection](x64-injection.md)), and in a script kept across
  restarts write the operand as `game.exe+2F1A30`, because the absolute address changes with ASLR
  ([Auto Assembler](auto-assembler.md)).

## Reading what a value means from code

The instructions that read or write a value, from a `debugger_start_capture(address="...", trigger="write")` job (with
the user's consent, see [debugger](debugger.md)) or from `code_find_references`, tell its type, its meaning and the
object that holds it. The patterns are written as CE prints them.

| Pattern | Meaning |
|---|---|
| `mov [rbx+000007A4],eax` | 4-byte value at offset `7A4` of the object in `RBX` |
| `movss [rbx+48],xmm0` / `movsd` | 32-bit float / 64-bit double |
| `sub [rbx+000007A4],eax`, `add`, `dec`, `inc` | damage, spending or gain; the other operand is the amount |
| `imul eax,[rbx+10]` | a multiplier (level, stack count) |
| `cvtsi2ss xmm0,eax` / `cvttss2si eax,xmm0` | int to float / float to int: stored and shown types may differ |
| `cmp dword ptr [rbx+000007A4],00` + `jle` | signed test against zero: a death or empty check |
| `comiss xmm0,[rbx+48]` + `jbe` | float comparison (after `comiss`, the `ja`/`jb` family compares floats) |
| `test byte ptr [rbx+30],01` + `je` | a flag bit |
| `movzx eax,byte ptr [rbx+31]` + `test al,al` | a 1-byte boolean |
| `mov eax,[rbx+rcx*4+10]` | an array of 4-byte elements at `+10`, index in `RCX` |
| `lea rcx,[rbx+20]` | an address, no memory access: an embedded sub-object |
| `mov rax,[rcx]` + `call qword ptr [rax+18]` | a C++ virtual call (slot 3): the object's first qword is its vtable |
| `call qword ptr [7FF6A1E0A000]` | an import call through the IAT slot at that address |

- Signedness: `jl`, `jg`, `jnl` (CE's name for `jge`), `jle`/`jng`, `movsx`, `idiv` and `sar` mean signed; `ja`,
  `jb`, `jae`, `jbe`/`jna`, `movzx`, `div` and `shr` mean unsigned or a pointer. CE names `jle` and `jbe` differently
  in their short (`jle`, `jna`) and near (`jng`, `jbe`) encodings, so search for both.
- Microsoft x64 calling convention: integer and pointer arguments in `RCX`, `RDX`, `R8`, `R9`, float arguments in
  `XMM0` to `XMM3` by position, the result in `RAX` or `XMM0`. A C++ method receives `this` in `RCX`, so at a function
  entry `RCX` is usually the object. 32-bit `thiscall` code passes `this` in `ECX`.
- Several instructions often write one value: one sets it, one adds, one clamps it to a maximum. Patch the one that
  matches the intent.
- Identify a class with `memory_get_address_info(addresses=["<object>"], includeRtti=true)` (MSVC C++),
  `mono_get_object(address="<address>")` (Mono or IL2CPP, collector attached, from any address inside the object) or
  `dotnet_get_object(address="<object>")` (.NET), then lay out its fields with [structures](structures.md).
- While the target is stopped at a breakpoint, `debugger_get_context(includeExtraRegisters=true)` adds XMM values.

## From a text to the code that uses it

The [find the code that uses a text](../Workflows/find-code-by-string.md) workflow does this in full. The core:

1. `aob_find_value(valueType="string", value="Game Over", module="game.exe")` finds the UTF-8 literal (`wstring` for
   UTF-16).
2. After a dissect, `code_find_references(address="<literal address>")` gives the instructions that use it, usually a
   `lea rcx,[<literal address>]` before a call; without one, a `code_start_search` for the literal's address.
3. A literal reached through a table of string pointers has no direct reference:
   `pointer_find_references(target="<literal address>", module="game.exe", writableOnly=false)` finds the table slot
   (such tables often sit in read-only data, which the default skips); then look for references to the slot.
4. `code_get_function(address="<fromAddress>")`, graph the function from its start and read the conditional branch
   that leads to the reference.

No literal in the image: the text comes from data files (localization), see [find text](../Workflows/find-text.md);
managed games keep literals in metadata ([Mono and .NET](mono-and-dotnet.md)).

## Comments

`code_get_comments(addresses=["game.exe+1A2B40"])` returns one `comment` per address, in request order; CE 7.7 gives an
empty string where none is set (the schema allows null): treat both as no comment.
`code_set_comment(address="game.exe+1A2B40", comment="MCP: applies damage")` replaces the comment (up to 4096
characters) and returns the new text, so read the old one first; an empty `comment` deletes it. Comments are CE state
the user sees, saved with the cheat table: write them only when the user wants notes kept, prefixed (`MCP:`).

## Pitfalls

- Mono and .NET JIT code lives in private memory, is compiled at run time and moves between runs: read it here, but
  anchor anything kept on the runtime's names ([Mono and .NET](mono-and-dotnet.md)). IL2CPP code is native code in
  `GameAssembly.dll` ([Unity IL2CPP](unity-il2cpp.md)).
- One instruction often serves the player and every enemy: [filter shared code](../Workflows/shared-code-filter.md)
  before patching it. With consent,
  `debugger_start_capture(address="<instruction>", trigger="execute", groupByEffectiveAddress=true)` lists each
  address it touches (CE's "Find out what addresses this instruction accesses").
- Code that unpacks or decrypts itself, detects debuggers or ships with anti-cheat: stop and tell the user. Reading
  code leads to patches, and patches belong only in single-player or offline software the user owns or may modify,
  never in online or competitive games; explain the change and get consent first ([safety](safety.md)).

## Related

- Documents: [memory model](memory-model.md) (sections and regions), [address expressions](address-expressions.md).
- Workflows: [find what writes a value](../Workflows/find-writer.md), [NOP an instruction](../Workflows/nop-patch.md),
  [explain an address](../Workflows/identify-address.md), [trace the logic](../Workflows/trace-logic.md).

## Sources

- Cheat Engine 7.7 `celua.txt` (local): the disassembler, `DissectCode` and comment functions; `defines.lua`:
  `jtCall` 0 to `jtMemory` 3; `autorun/pseudocodediagram.lua` takes the third `splitDisassembledString` result as bytes.
- CE sources under https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine: `LuaHandler.pas`
  (`disassemble` without symbols, `splitDisassembledString` order, `disassembleBytes`, `getComment`),
  `disassembler.pas` (operand text, mnemonics), `DissectCodeThread.pas` and `LuaDissectCode.pas` (what a pass
  records), `disassemblerComments.pas` (empty comments).
- https://wiki.cheatengine.org/index.php?title=Help_File:Memory_view
- https://learn.microsoft.com/cpp/build/x64-calling-convention
- https://learn.microsoft.com/windows/win32/debug/pe-format
- https://www.felixcloutier.com/x86/jcc
