# x64 code injection essentials

Injected code runs in the middle of someone else's function, on every thread that reaches the patched instruction. It must leave registers, flags and the stack exactly as the original code expects. Write scripts with the templates in [auto-assembler](auto-assembler.md); this page covers the machine-level rules. AA numbers are hex: `sub rsp,28` is 40 bytes.

## Registers

| 64-bit | 32-bit | 16-bit | 8-bit |
|---|---|---|---|
| `RAX` `RBX` `RCX` `RDX` | `EAX` `EBX` `ECX` `EDX` | `AX` `BX` `CX` `DX` | `AL` `BL` `CL` `DL` (and `AH` `BH` `CH` `DH`) |
| `RSI` `RDI` `RBP` `RSP` | `ESI` `EDI` `EBP` `ESP` | `SI` `DI` `BP` `SP` | `SIL` `DIL` `BPL` `SPL` |
| `R8`-`R15` | `R8D`-`R15D` | `R8W`-`R15W` | `R8B`-`R15B` |

- Writing a 32-bit register zeroes the upper half of the 64-bit register (`mov eax,1` clears the top of `RAX`). Writing 8- or 16-bit parts leaves the rest unchanged.
- `RIP` is the instruction pointer; `RFLAGS` holds ZF, CF, SF, OF and friends.
- `XMM0`-`XMM15` hold floats and doubles (scalar in the low 32 or 64 bits).
- Operand size decides the value type: `dword ptr` is 4 bytes, `qword ptr` 8, `byte ptr` 1. `movzx` means unsigned, `movsx`/`movsxd` signed.

## Which registers you may clobber

At an arbitrary injection point, every register may be live. The Microsoft x64 ABI only says what a function call you make may destroy:

| Class | Registers |
|---|---|
| Volatile (a callee may destroy) | `RAX`, `RCX`, `RDX`, `R8`-`R11`, `XMM0`-`XMM5`, upper halves of `YMM` |
| Nonvolatile (a callee preserves) | `RBX`, `RBP`, `RDI`, `RSI`, `RSP`, `R12`-`R15`, `XMM6`-`XMM15` |

- Save and restore any register you modify unless you proved it dead: the original code overwrites it before reading it after your `return:` label. Read the instructions after the injection point with `code_disassemble` before relying on that.
- There is no red zone on Windows x64: data below `RSP` can be overwritten at any time. Always `sub rsp` before storing on the stack.
- XMM registers cannot be pushed: `sub rsp,10` then `movdqu [rsp],xmm0`, and the reverse to restore.

## Flags

`cmp`, `test`, `add`, `sub`, `inc`, `and` and most arithmetic change flags. If the original code reads flags after your code (a `jcc`, `cmov`, `setcc` or `adc` following the injection point, or your cave sits between a `cmp` and its jump), wrap your code in `pushfq` / `popfq`. `mov`, `lea` and `movss` do not touch flags. The ABI expects the direction flag clear at calls.

## Calling a function from injected code

1. Save every volatile register that is live (`RAX`, `RCX`, `RDX`, `R8`-`R11`, and `XMM0`-`XMM5` if floats are in use), plus flags.
2. Align the stack: at the `call` instruction `RSP` must be a multiple of 16. After an unknown number of pushes, save `RSP` in a nonvolatile register you also saved, then clear its low four bits with `and rsp,FFFFFFFFFFFFFFF0`.
3. Reserve 32 bytes of shadow space (`sub rsp,20`), plus room for arguments beyond four.
4. Pass integer and pointer arguments in `RCX`, `RDX`, `R8`, `R9`; float and double arguments in `XMM0`-`XMM3` (by position). Further arguments go on the stack above the shadow space.
5. Read the result from `RAX` or `XMM0`, then restore `RSP` and everything saved, in reverse order.

For C++ methods, `this` is the first argument (`RCX`). Calling into the game from a hook is risky (reentrancy, locks, thread affinity); from outside the game prefer `exec_call_remote` or `exec_call_method` (`EnableTargetCodeExecution`), which refuse while the target is paused or broken.

## SSE float operations

| Instruction | Meaning |
|---|---|
| `movss xmm0,[rbx+48]` / `movss [rbx+48],xmm0` | Load/store a 32-bit float (`movsd` for a 64-bit double) |
| `addss`, `subss`, `mulss`, `divss` | Scalar float arithmetic (`addsd`... for doubles) |
| `comiss xmm0,xmm1` / `ucomiss` | Compare; sets ZF, PF and CF like an unsigned compare: branch with `ja`, `jb`, `jae`, `jbe`, never `jg`/`jl` |
| `cvtsi2ss xmm0,eax` | Integer to float |
| `cvttss2si eax,xmm0` | Float to integer, truncating |
| `cvtss2sd`, `cvtsd2ss` | Float to double and back |
| `xorps xmm0,xmm0` | Zero a register |

There is no float immediate for XMM registers: store the constant in your cave (`myConst: dd (float)100`) and `movss xmm0,[myConst]`. A plain memory write can use a cast: `mov dword ptr [rbx+48],(float)100`.

## Jumps and distance

| Encoding | Size | Reach |
|---|---|---|
| `E9 rel32` (`jmp`) | 5 bytes | +/-2 GB from the next instruction |
| `EB rel8` (short `jmp`) | 2 bytes | -128 to +127 bytes |
| `FF 25 00000000` + 8-byte address | 14 bytes | Anywhere |

x64 has no direct 64-bit relative jump. When the cave is farther than 2 GB, CE must emit the 14-byte form, which overwrites more instructions than the template planned. Always `alloc(newmem,$1000,<injection point>)` so the cave lands within reach, and check the emitted jump with `code_disassemble`. `asm_generate_injection` offers `farJump` for the `code` template when you really need the long form.

## RIP-relative operands

In x64 code `mov eax,[rip+disp32]` addresses memory relative to the next instruction; CE displays it as an absolute or module-relative address such as `[game.exe+2F1A30]`. Relative jumps and calls work the same way.

- Copying such an instruction's bytes elsewhere (`readmem`, raw `db`) makes it point to the wrong place. Keep it as text with its absolute operand, which the assembler re-encodes at the new address, or use `reassemble(address)`.
- Re-encoding only works within +/-2 GB of the target data: another reason to allocate near.
- These operands are also stable static bases for pointers; see [code-analysis](code-analysis.md).

## Patch whole instructions

- The 5-byte jump must replace complete instructions. Add up the lengths from `code_disassemble` (typed `size` is authoritative) until the total is at least 5; NOP-pad the remainder of the last instruction and copy all of them into the cave.
- Never let the overwritten bytes cover a jump target: another path jumping into the middle of your jump crashes. Check `code_get_function` (`addressIsJumpDestination`) and look for jumps into the range.
- Pick a point after the flags and registers you need are set, and not inside a tight loop if the cave is heavy.
- For a plain removal, `asm_apply_code_patch(nopInstructions=...)` does this bookkeeping and asserts the original bytes.

## Threads and timing

- Every thread that executes the instruction runs your code, possibly at the same time. A global written by the cave (an injection copy) holds whichever thread or entity ran last; do not keep per-call state in shared memory.
- Shared code serves many entities (player and enemies): filter with a discriminator before changing anything ([structures](structures.md)).
- A thread can be executing the bytes while they are overwritten. For code that runs constantly, pausing the target around the apply (`process_set_paused`) reduces that risk; resume it afterwards.
- On a 32-bit target, use `pushad`/`popad` and `pushfd`/`popfd`; a 5-byte jump always reaches.

## Sources

- https://learn.microsoft.com/cpp/build/x64-calling-convention
- https://learn.microsoft.com/cpp/build/x64-software-conventions
- https://learn.microsoft.com/cpp/build/stack-usage
- https://forum.cheatengine.org/viewtopic.php?t=605605
- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Auto_Assembler
- https://wiki.cheatengine.org/index.php?title=Auto_Assembler:reassemble
