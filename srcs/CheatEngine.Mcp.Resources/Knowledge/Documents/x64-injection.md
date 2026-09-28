# x64 code injection essentials

Injected code runs in the middle of someone else's function, on every thread that reaches the patched instruction. It
must leave registers, flags and the stack exactly as the original code expects. Read this page before you write a code
cave, a byte patch or a branch patch. [Auto Assembler](auto-assembler.md) covers the script language and templates, and
[code analysis](code-analysis.md) covers reading the code first. Auto Assembler (AA) numbers are hex: `sub rsp,28` is
40 bytes. The size in `alloc` is the exception: it is decimal unless written with `$`, so `alloc(newmem,$1000)` is
4096 bytes.

Patch only single-player or offline software that the user owns or may modify. Explain each change and get consent
before you apply it; see [safety](safety.md). `asm_check`, `asm_apply` and `asm_apply_code_patch` need
`Mcp:EnableAutoAssembler`; `runtime_get_info()` reports the switches in `gates`.

## Registers

| 64-bit | 32-bit | 16-bit | 8-bit |
|---|---|---|---|
| `RAX` `RBX` `RCX` `RDX` | `EAX` `EBX` `ECX` `EDX` | `AX` `BX` `CX` `DX` | `AL` `BL` `CL` `DL` |
| `RSI` `RDI` `RBP` `RSP` | `ESI` `EDI` `EBP` `ESP` | `SI` `DI` `BP` `SP` | `SIL` `DIL` `BPL` `SPL` |
| `R8`-`R15` | `R8D`-`R15D` | `R8W`-`R15W` | `R8B`-`R15B` |

- Writing a 32-bit register zeroes the upper half of the 64-bit register: `mov eax,1` clears the top of `RAX`.
  Writing an 8- or 16-bit part leaves the rest unchanged.
- A REX prefix (one byte, `40`-`4F`) encodes `R8`-`R15`, `SIL`/`DIL`/`BPL`/`SPL` and 64-bit operand size. Changing a
  register can change an instruction's length, so reassemble and recount after every edit. The legacy high bytes `AH`,
  `BH`, `CH` and `DH` cannot be encoded in an instruction that has a REX prefix.
- `XMM0`-`XMM15` hold floats and doubles, scalar in the low 32 or 64 bits.
- The operand size gives the value size: `byte ptr` is 1 byte, `word ptr` 2, `dword ptr` 4 and `qword ptr` 8. `movzx`
  suggests unsigned; `movsx` and `movsxd` suggest signed.
- `pushad`/`popad` are invalid in 64-bit code. Push registers one at a time.
- No instruction stores a 64-bit immediate to memory: `mov qword ptr [rbx+10],imm` sign-extends a 32-bit immediate.
  Only `mov r64,imm64` takes a 64-bit constant, so load it into a saved register and store that.

## Which registers you may clobber

At an arbitrary injection point, every register may be live. The Microsoft x64 ABI only says what a function that you
call may destroy:

| Class | Registers |
|---|---|
| Volatile (a callee may destroy) | `RAX`, `RCX`, `RDX`, `R8`-`R11`, `XMM0`-`XMM5`, upper halves of `YMM` |
| Nonvolatile (a callee preserves) | `RBX`, `RBP`, `RDI`, `RSI`, `RSP`, `R12`-`R15`, `XMM6`-`XMM15` |

- Save and restore every register you modify, unless you proved it dead: the original code after your `return:` label
  overwrites it before it reads it. Read those instructions with `code_disassemble` first.
- Windows x64 has no red zone: memory below `RSP` can be overwritten at any time. Move `RSP` down before you store on
  the stack.
- XMM registers cannot be pushed. Use `lea rsp,[rsp-10]` and `movdqu [rsp],xmm0`, then `movdqu xmm0,[rsp]` and
  `lea rsp,[rsp+10]` to restore.
- The MXCSR control bits (rounding mode, exception masks) are nonvolatile. Leave them unchanged.

## Flags

- Most arithmetic and logic changes the flags: `cmp`, `test`, `add`, `sub`, `and`, `or`, `xor`, shifts, and also
  `add rsp,N` and `sub rsp,N`. `inc` and `dec` change every status flag except CF.
- These leave the flags unchanged: `mov`, `lea`, `push`, `pop` (except `popfq`), `jmp`, `call`, `movss`, `movd` and
  `movdqu`.
- The flags are live when, after the injection point, a `jcc`, `cmovcc`, `setcc`, `adc`, `sbb` or `pushfq` reads them
  before another instruction sets them; for example, when your jump sits between a `cmp` and its branch. Then wrap the
  cave in `pushfq` / `popfq`, or use only flag-neutral instructions such as `lea rsp,[rsp-N]`.
- A discriminator filter in the cave (a `cmp`, then `jne` back to the original code) changes the flags too.
- Leave the direction flag clear: the ABI expects it clear on function exit and on entry to C runtime and Windows
  calls.

## Calling a function from injected code

1. `pushfq`. Then save every volatile register that is live: `RAX`, `RCX`, `RDX`, `R8`-`R11`, and `XMM0`-`XMM5`
   unless you proved them dead. The callee may destroy all of them.
2. Align the stack and reserve the shadow space. At the `call`, `RSP` must be a multiple of 16 (`10` hex), with 32
   bytes (`20` hex) reserved above it for the callee:

   ```
   push rbp
   mov rbp,rsp
   and rsp,-10       // clear the low four bits
   sub rsp,20        // shadow space; add 8 per argument beyond four, rounded up to 10
   call <function>
   mov rsp,rbp
   pop rbp
   ```

3. Pass integer and pointer arguments in `RCX`, `RDX`, `R8` and `R9`, and float or double arguments in `XMM0`-`XMM3`,
   by position: `f(int a, double b)` takes `a` in `ECX` and `b` in `XMM1`. The fifth argument goes at `[rsp+20]`, just
   above the shadow space. A struct that is not 1, 2, 4 or 8 bytes long is passed by pointer. A free or static
   function that returns a larger struct takes a hidden pointer to the result in `RCX`, which shifts the other
   arguments one place right. When in doubt, copy what the game's own call sites load before the `call`.
4. Read the result from `RAX` or `XMM0`. Restore everything in reverse order and finish with `popfq`.

For C++ methods, `this` is the first argument (`RCX`). Calling into the game from a hook is risky (reentrancy, locks,
thread affinity).

## Calling a game function once

`exec_call_remote` calls a target function once, without a hook, and `exec_call_method` an instance method. Both need
`Mcp:EnableTargetCodeExecution` and run game code that MCP cannot undo: get consent for each call, never loop. For
Unity (Mono) use `mono_invoke_method` ([Unity and .NET](mono-and-dotnet.md)).

1. Find the signature at a call site. After a dissect pass ([code analysis](code-analysis.md)),
   `code_find_references(address="<function>")` gives the callers, and
   `code_disassemble(address="<call site>", before=12, count=2)` shows what they load before the `call`: registers as
   above, or pushes on x86, where an `add esp,N` after the call means `cdecl`.
2. Each argument is `{type="integer", value="0x1F4"}` in parameter order, up to 16: `integer` (also pointers), `float`
   or `double`. The value is always a JSON string (`"10"`, not `10`): decimal, `0x` hex, or hex when it holds A-F, so
   prefix addresses with `0x`. `exec_call_remote.callingConvention` matters only on x86.
3. Text or a buffer: `memory_allocate(name="callArg", size=256)`,
   `memory_write(address="<address>", valueType="wstring", value="Sword", nullTerminate=true)`, then pass its address
   as an `integer`. An out-parameter or a struct result works the same; read it back with `memory_read`. An engine
   string object (`FString`, `std::string`) is a structure, not characters. Free the buffer only once the call returned.
4. Resume first (`process_set_paused(paused=false)`, `debugger_continue()`): a paused or debugger-stopped target is
   refused (`busy`). A breakpoint that the function reaches stops its thread, and the call times out.
5. `exec_call_remote(functionAddress="game.exe+4C10", arguments=[{type="integer", value="0x1F4"}])`, then read the
   effect.

With the default `exec_call_method.classRegister` 1,
`exec_call_method(functionAddress="<method>", classInstance="<object>")` is the x64 member call: `this` in `RCX`,
arguments from `RDX`/`XMM1` on. On x86 `this` goes in `ECX` and the arguments on the stack. Change the register only
when the code loads `this` elsewhere; one that an argument would overwrite is refused.

- CE runs the function on a new thread in the target while the game keeps running. Code that needs its own thread
  (rendering, engine APIs), thread-local state or a lock the game holds can deadlock or crash, and code that a game
  thread is running is reentered. Prefer small functions that ordinary game code calls.
- CE's main thread and other tool calls wait for it, at most `exec_call_remote.timeoutMilliseconds` (1 to 10000,
  default 10000).
- `exec_call_remote.returnValue` is `RAX` or `EAX` in decimal. Only the return type's bits count: the low 32 for an
  `int` (4294967295 is -1), the low byte for a `bool`. `util_calculate(expression="<returnValue>")` gives a
  pointer's `hex`. A `float` or `double` result cannot be read.
- A timeout or later host failure is `host_refused` with `hostEffect` `unknown`: the thread may still run, and the
  function may have run fully, partly or not yet. Never repeat the call: read what it should change, keep the buffers
  and decide with the user ([host effects](errors-and-recovery.md#host-effects)).
- `exec_call_local(localAddress="<address>", parameter=0)` runs a one-parameter `stdcall` function in CE's own
  process, such as `exec_compile_c(source="...", targetSelf=true)` output, with no timeout: a hang blocks CE, and a
  fault can crash it.

## SSE float operations

| Instruction | Meaning |
|---|---|
| `movss xmm0,[rbx+48]` / `movss [rbx+48],xmm0` | Load or store a 32-bit float (`movsd` for a 64-bit double) |
| `addss`, `subss`, `mulss`, `divss` | Scalar float arithmetic (`addsd` and so on for doubles) |
| `comiss xmm0,xmm1` / `ucomiss` | Compare; sets ZF, PF and CF like an unsigned compare |
| `cvtsi2ss xmm0,eax` | Integer to float |
| `cvttss2si eax,xmm0` / `cvtss2si` | Float to integer: truncating / rounding by the current MXCSR mode |
| `cvtss2sd`, `cvtsd2ss` | Float to double and back |
| `xorps xmm0,xmm0` | Zero a register |

- After `comiss`, branch with `ja`, `jae`, `jb` or `jbe`, never `jg` or `jl`. An unordered compare (a NaN operand) sets
  ZF, PF and CF, so `je`, `jb` and `jbe` are also taken on NaN. Test `jp` first when NaN is possible.
- A load `movss xmm0,[mem]` zeroes bits 32-127 of `XMM0`. A register-to-register `movss`, `addss` and `cvtsi2ss`
  keep them.
- There is no float immediate for XMM registers. Store the constant in your cave (`myConst: dd (float)100`) and load it
  with `movss xmm0,[myConst]`. A plain 4-byte write can use a cast: `mov dword ptr [rbx+48],(float)100`. A double needs
  `dq (double)100` in the cave, because memory takes no 64-bit immediate.
- Bit patterns: 1.0f is `3F800000`, 100.0f `42C80000`, -1.0f `BF800000`, 1.0 (double) `3FF0000000000000`. A float holds
  integers exactly only up to 16,777,216 (2^24).

## Conditional branches

A conditional jump (`jcc`) reads the flags that the preceding `cmp` or `test` set. It has a short form, `7x rel8`
(2 bytes), and a near form, `0F 8x rel32` (6 bytes). The displacement counts from the end of the instruction. The two
opcodes of each pair below differ only in their lowest bit, so flipping that bit inverts the condition and keeps the
length and the target.

| Taken when (after `cmp a,b`) | Pair | Short | Near |
|---|---|---|---|
| a = b / a ≠ b (ZF) | `je` / `jne` | `74` / `75` | `0F 84` / `0F 85` |
| unsigned a < b / a ≥ b (CF) | `jb` / `jae` | `72` / `73` | `0F 82` / `0F 83` |
| unsigned a ≤ b / a > b | `jbe` / `ja` | `76` / `77` | `0F 86` / `0F 87` |
| signed a < b / a ≥ b | `jl` / `jge` | `7C` / `7D` | `0F 8C` / `0F 8D` |
| signed a ≤ b / a > b | `jle` / `jg` | `7E` / `7F` | `0F 8E` / `0F 8F` |
| negative / not negative (SF) | `js` / `jns` | `78` / `79` | `0F 88` / `0F 89` |
| overflow / no overflow (OF) | `jo` / `jno` | `70` / `71` | `0F 80` / `0F 81` |
| parity (NaN after `comiss`) / not | `jp` / `jnp` | `7A` / `7B` | `0F 8A` / `0F 8B` |

The branch also hints at the type: `jl`, `jg` (with `movsx`, `idiv`, `sar`) suggest signed; `jb`, `ja` (with `movzx`,
`div`, `shr`) suggest unsigned or a pointer; `ja` or `jb` after `comiss` means a float.

Replacements of the same length:

| Goal | Short `jcc` (2 bytes) | Near `jcc` (6 bytes) |
|---|---|---|
| Always taken | `EB` and the same rel8 | `90 E9` and the same rel32: the `E9` ends where `jcc` ended |
| Never taken | `66 90` | `66 0F 1F 44 00 00` |
| Inverted | flip the low bit: `74` and `75` | flip the low bit of the second byte: `0F 84` and `0F 85` |

1. `code_decode(address="<jcc address>")`: its `instruction` holds the exact `bytes` and `size`.
2. Build the replacement from the table and preview it with
   `code_disassemble_bytes(hexadecimalBytes="<replacement>", origin="<jcc address>")`: a jump must still show the
   original target. To let CE encode the jump instead, set `asm_assemble.preference` (0 Cheat Engine chooses, 1 short,
   2 long, 3 far). `asm_assemble(address="<jcc address>", instructions=["jmp <target>"], preference=1)` returns 2
   bytes without checking the rel8 reach, so preview them too;
   `asm_assemble(address="<jcc address>+1", instructions=["jmp <target>"], preference=2)` gives the 5-byte `E9` that
   follows the `90` of a near one.
3. With consent:
   `asm_apply_code_patch(address="game.exe+1A2B3C", expectedBytes="7E 1A", replacementBytes="EB 1A", name="always take jle")`.
   Undo it with `asm_release_patch(patchId=...)`.

- `jrcxz` (`E3`) and `loop`/`loope`/`loopne` (`E0`-`E2`) exist only in the short form and have no inverted twin
  (flipping the low bit of `E3` gives `loop`): invert them with an injection.
- "Always" makes the fall-through path dead, and "never" the target path, for every caller of shared code. Read both
  paths first ([trace logic](../Workflows/trace-logic.md)). The guided version is
  [force or invert a branch](../Workflows/patch-branch.md).

## Jumps and distance

| Encoding | Size | Reach |
|---|---|---|
| `EB rel8` (`jmp short`) | 2 bytes | -128 to +127 bytes |
| `E9 rel32` (`jmp`), `E8 rel32` (`call`) | 5 bytes | +/-2 GB from the next instruction |
| `FF 15 disp32` (`call [rip+disp]`) | 6 bytes | Anywhere, through an 8-byte slot (imports) |
| `FF 25 00000000` + 8-byte address (`jmp far`) | 14 bytes | Anywhere |
| `FF 15 02000000`, `EB 08`, 8-byte address | 16 bytes | Anywhere (`call far`) |
| `jcc +2`, `jmp short +0E`, then a `jmp far` | 18 bytes | Anywhere (far `jcc`) |

- x64 has no 64-bit relative jump. On a 64-bit target, CE's assembler silently turns a `jmp`, `call` or `jcc` whose
  target is more than 2 GB away into the far form, which overwrites more bytes than the script planned. In a script,
  `short`, `long` and `far` force a form.
- Keep the cave near: `alloc(newmem,$1000,<injection point>)`. The `asm_generate_injection` scaffold always writes
  `alloc(newmem,2048,...)` near the signature match and a plain `jmp newmem`, padded with `nop` lines to the
  `expectedBytes` length. It has no far-jump option.
- When nothing within reach is free, CE searches for up to about 10 seconds, then shows the modal "Nearby allocation
  error" dialog (Yes, No, Yes to All, No to All) and waits (`asm_apply` has the `may_prompt` dispatch class). No fails
  the apply with "Failure allocating memory near ...". Yes allocates anywhere, so `jmp newmem` becomes the 14-byte far
  form: it overwrites 9 bytes past the run that the scaffold's `assert` never checked and its `[DISABLE]` never
  restores, and the target will probably crash. Before the apply, tell the user to answer No if it appears.
- CE remembers a "to All" answer until it restarts: after Yes to All it allocates far without asking, after No to All
  every later failure fails silently. Always check the jump size after an apply.
- Two injection sites in one script need their own labels (`return1`, `return2`), each placed after its own run. CE
  rejects a declared label placed twice ("label return is being defined more than once"), and a jump back to another
  site's label resumes the wrong code.

## RIP-relative operands

In x64 code, `mov eax,[rip+disp32]` addresses memory relative to the next instruction. The tools print the target as
an absolute address, such as `mov eax,[7FF6A1DF1A30]`, but the bytes hold only a displacement that differs per site
and per build. Relative jumps and calls (`E8`, `E9`, `EB`, `7x`, `0F 8x`) work the same way.

- A byte copy points to the wrong place: `readmem`, a raw `db`, and the `asm_generate_injection` scaffold, which copies
  `expectedBytes` into the cave as one `db` line. If a copied instruction has a RIP-relative operand or is a relative
  branch, take its bytes out of that line and write its `opcode` text from `code_disassemble` in their place. The text
  holds the absolute target, so the assembler re-encodes it for the cave; CE's own injection templates copy the
  originals as text too. In a script kept across restarts, write the target module-relative, such as
  `mov eax,[game.exe+2F1A30]`, as `memory_get_address_info(addresses=["7FF6A1DF1A30"])` names it. Run `asm_check`
  afterwards.
- `reassemble(<address>)` does the same at apply time, but not under `asm_check`: a syntax check defines every
  `aobscan` result as `00000000`, so `reassemble` of a scan-relative address reads the wrong place and can be
  rejected. Prefer the `opcode` text.
- Re-encoding works only within +/-2 GB of the data: another reason to allocate near.
- Wildcard `disp32` and `rel32` fields in signatures ([AOB signatures](aob-signatures.md)).
- These operands are also stable static bases for pointers; see [code analysis](code-analysis.md).

## Patch whole instructions

1. `code_disassemble(address="<injection point>", before=4, count=12)`. From the injection point, add up each
   instruction's `size` until the total is at least 5. That run of whole instructions, the concatenated `bytes`, is the
   `expectedBytes` (at most 64 bytes). `before` only estimates earlier boundaries: count forward from a known one, such
   as a debugger hit.
2. No branch may land inside the run after its first byte. For each later instruction of the run,
   `code_get_function(address="<instruction>", jumpRange=4096)` must report `addressIsJumpDestination` false. In
   `code_get_function_graph(address="<startAddress>")`, no block `start` other than the injection point may fall inside
   the run. Neither check follows indirect jumps (switch tables), so neither is proof.
3. Pick a point after the flags and registers you need are set, not inside a tight loop if the cave is heavy, and not
   in code that enemies share unless you filter ([structures](structures.md),
   [filter shared code](../Workflows/shared-code-filter.md)).
4. For a byte change or a removal, build an equal-length replacement yourself and apply it with
   `asm_apply_code_patch(address="<first byte>", expectedBytes="<whole instruction bytes>", replacementBytes="<same length>", name="...")`.
   It takes 1 to 64 bytes, asserts the original bytes before writing and restores them on release
   ([NOP patch](../Workflows/nop-patch.md)).
5. For custom code, `aob_generate_signature(address="<injection point>")`, then
   `asm_generate_injection(module="game.exe", signature="<pattern>", expectedBytes="<whole instruction bytes>", symbolName="INJECT_HP", offset=<offset>)`.
   Edit the cave, run `asm_check`, then `asm_apply` ([AOB injection](../Workflows/aob-injection.md),
   [review an AA script](../Workflows/review-aa-script.md)).

Multi-byte NOPs for 1 to 9 bytes: `90` · `66 90` · `0F 1F 00` · `0F 1F 40 00` · `0F 1F 44 00 00` ·
`66 0F 1F 44 00 00` · `0F 1F 80 00 00 00 00` · `0F 1F 84 00 00 00 00 00` · `66 0F 1F 84 00 00 00 00 00`.
Replacing one instruction with one NOP of the same length keeps every instruction boundary valid. In a script,
`nop 6` (the count is hex) emits multi-byte NOPs of at most 9 bytes each (single `90` bytes on CPUs that CE does not
recognize as supporting them).

## Hooking a function entry

`asm_generate_api_hook(address="<entry>", jumpTarget="<your code>", newCallAddress="<8-byte slot>")` returns CE's
API-hook script without applying it ([Auto Assembler](auto-assembler.md)). `jumpTarget` must already resolve, such as
an executable `memory_allocate` block.

- ENABLE copies the whole instructions that the jump overwrites into `originalcall`, as disassembly text so that
  RIP-relative operands are re-encoded, followed by a jump back. On x64 it adds a trampoline near the entry that
  reaches `jumpTarget` anywhere. It stores the address of `originalcall` at `newCallAddress` and writes the jump. Its
  near allocations can raise the "Nearby allocation error" dialog (see Jumps and distance).
- DISABLE writes the original bytes back and frees its allocations; `newCallAddress` keeps its value. `extension`
  suffixes the label names, so two hooks can share one script.
- Your code is entered as the function (on x64, arguments in `RCX`, `RDX`, `R8`, `R9`). Keep the nonvolatile registers
  and `ret`, or jump to `originalcall`; to run the original first, `call [<slot>]` as a normal call (see above).
- Check for branches into the overwritten run (step 2 of the previous section), then `asm_check` and, with consent,
  `asm_apply`. The generated x64 comment `//special jump trampoline in the current region (64-bit)` makes `asm_apply`
  also require `Mcp:EnableUnsafeLua` and `Mcp:EnableTargetCodeExecution`: delete it when they are off.

## Threads and timing

- Every thread that executes the instruction runs your code, possibly at the same time. A global that the cave writes
  (an [injection copy](../Workflows/injection-copy-base.md)) holds whichever thread or entity ran last. Do not keep
  per-call state in shared memory.
- Shared code serves many entities (player and enemies): filter with a discriminator before changing anything
  ([structures](structures.md)).
- On Windows, CE writes a patch without suspending the target. Pause it around apply and release
  (`process_set_paused(paused=true)`, then `process_set_paused(paused=false)`) so no running thread meets half-written
  bytes. MCP tracks its pause as a resource until you resume. A thread already stopped inside the overwritten run (it
  resumes mid-jump) or inside the cave when release frees it still crashes; both cases are rare.
- On a 32-bit target, `pushad`/`popad` and `pushfd`/`popfd` work, and a 5-byte jump always reaches. Conventions:
  `cdecl` (the caller cleans the stack), `stdcall` (`ret N`), `thiscall` (`this` in `ECX`), `fastcall` (`ECX`, `EDX`).
  `EAX`, `ECX` and `EDX` are volatile.

## Verify

1. After `asm_apply`, `code_disassemble(address="<injection point>", count=4)` must show a 5-byte `jmp` (`bytes`
   starting with `E9`) followed by NOPs up to the next original instruction. A 14-byte `FF 25` jump means the cave is
   far away: release the patch at once and restart the target, because the scaffold's release restores only the
   `expectedBytes` length and the 9 bytes after it stay overwritten.
2. Disassemble the cave at the target that the `jmp` line's `opcode` shows: the copied instructions must show the same
   operands as the originals, especially RIP-relative ones.
3. After `asm_release_patch(patchId=...)` returns `released` true, disassemble again: the bytes must equal
   `expectedBytes`. Release every patch (`asm_list_patches()` lists them) before `process_attach`, which is refused
   (`busy`) while a patch or any other MCP resource is retained.

## Sources

- https://learn.microsoft.com/cpp/build/x64-calling-convention
- https://learn.microsoft.com/cpp/build/x64-software-conventions
- https://learn.microsoft.com/cpp/build/stack-usage
- https://www.felixcloutier.com/x86/ (pages jcc, jmp, nop, comiss, movss, mov, pusha:pushad)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/Assemblerunit.pas (far forms, `nop N`)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/autoassembler.pas (nearby allocation,
  `alloc` sizes, labels, `reassemble`, syntax check)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/globals.pas (nearby allocation answers)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/frmautoinjectunit.pas (templates, API hook)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas (`executeMethod`: remote
  thread, timeout)
- https://forum.cheatengine.org/viewtopic.php?t=605605
- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Auto_Assembler
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:reassemble
