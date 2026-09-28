# Explain what is at an address

## Goal

Explain what `{address}` is: code, static data, a heap object, a stack slot, a mapped view or an unusable page, with
its name, its class when known, who points to it, and the workflow to use next. Every step only reads.

## Steps

1. `symbol_resolve(expressions=["{address}"])`: the resolved `address` and Cheat Engine's `name` (a symbol or
   module+offset). On an `error`, check the expression and `process_get_current()`.
2. `memory_get_address_info(addresses=["{address}", "THREADSTACK0", "THREADSTACK1"], includeRtti=true, includePointerValue=true)`
   gives `module`, `section`, `isSystemModule`, the `region` (state, protection, type, allocationBase),
   `pointerValue` and `rttiClass`. `THREADSTACKn` lies in thread n's stack (add `THREADSTACK2` and up as needed).
3. Classify (the first fitting row wins):

   | Evidence | Class | Next |
   |---|---|---|
   | `image`, execute (`.text`) | code | [trace the logic](trace-logic.md) |
   | `image`, read-only (`.rdata`) | constant, literal or vtable | [code by string](find-code-by-string.md) |
   | `image`, writable (`.data`) | static data | [find what writes](find-writer.md) |
   | same `allocationBase` as a `THREADSTACKn` row | stack slot: a local | [find what writes](find-writer.md) |
   | `private`, read-write | heap object or field | [dissect it](dissect-structure.md) |
   | `private`, execute | JIT (Mono, .NET) or injected code | [.NET recon](dotnet-recon.md) |
   | `mapped` | file view, shared or emulated RAM | [emulator memory](emulator-memory.md) |
   | `reserved`, `free` or an error | nothing usable now | Pitfalls |

4. Code: `code_get_function(address="{address}")` estimates the function;
   `code_disassemble(address="<start>", count=20)` from its start, or
   `code_disassemble(address="{address}", before=4, count=12)`, shows what it does. Operands are absolute
   hexadecimal without module names: name a target with `symbol_resolve` as in step 1.
5. Data: `memory_read(address="{address}", valueType="bytes", size=64)`, then typed reads such as
   `memory_read(address="{address}", valueType="int32", count=8)` (also `float`, or `wstring` with `length`). A
   `pointerValue` inside a committed region is probably a pointer (or two `int32`); describe it as in step 2.
6. Object start: `rttiClass` appears only where an MSVC vtable pointer sits (an object's start or a base class in
   it). Round the address down to a multiple of 8 (`<a>`) and describe the 8-byte steps before it in one call (up to
   256): `memory_get_address_info(addresses=["<a>", "<a>-8", "<a>-10"], includeRtti=true)`; the nearest row with an
   `rttiClass` is the start. On .NET, `dotnet_get_object(address="<object>")` gives the type and fields. On Unity
   (Mono or IL2CPP), when `mono_get_status()` reports `attached`, `mono_get_object(address="{address}")` finds the
   start itself: `className`, the fields and `offsetInObject` (decimal; a field must cover it, else the address lies
   past that object). It can block Cheat Engine for seconds; attaching needs consent
   ([Unity Mono recon](unity-mono-recon.md)).
7. Owners: `pointer_find_references(target="{address}", maxOffset=256, limit=20)`. Each `offset` is where
   `{address}` lies inside the object a holder points to; a holder with a `symbol` is a static root, and a heap
   holder's own object is found as in step 6. It scans the whole target, blocking Cheat Engine: tell the user first.

## Decisions

- `isSystemModule` code (Windows or a runtime) serves every caller: follow the game's caller instead.
- Static data: record the module+offset `name`. Heap data moves every run: anchor it with a
  [pointer scan](pointer-scan.md).
- A number that fits a guess (100 as `int32`) is no proof of its type: change it in game and read again, or
  [compare snapshots](compare-snapshots.md).

## Pitfalls

- An unreadable address has a cause: a guard or no-access page, a freed object, a restarted process (new heap and
  module bases) or a 32-bit target read with 8-byte pointers.
- `before` is an estimate; decoding from inside an instruction gives garbage, so start at a known instruction.
- Function bounds are heuristics (`found` can be false in JIT or packed code); RTTI names only MSVC types declared
  `class` with virtual methods (Unreal builds usually disable RTTI).

## Report

A table of address, name (symbol or module+offset), region (type, protection, section), class, evidence
(disassembly, values, RTTI, holders) and next workflow. Nothing changed or remains active. See
[memory model](../Documents/memory-model.md), [code analysis](../Documents/code-analysis.md) and
[structures](../Documents/structures.md).
