# Windows process memory

How a Windows process lays out its memory and what the region, module and address tools report. Read it to decide
whether an address is static, to explain a failed read, to choose what a scan covers, before changing protection, or
before editing a file through Cheat Engine's file-as-process mode.

## Address space and pages

- A 64-bit process owns user addresses `0` to `7FFFFFFFFFFF`. A 32-bit process on 64-bit Windows (WoW64) gets 2 GB, or
  4 GB when large-address-aware. A candidate pointer above the top of user space is not a pointer into the target.
- A page is 4 KB (`1000`); protection applies to whole pages. Reservations start on 64 KB boundaries (`10000`), so a
  module base ends in `0000`.

## Regions

A region is a run of pages with the same state, protection and type inside one allocation; a module is one
allocation, split where the protection changes (roughly per section). `memory_list_regions` pages the map in address
order; `memory_get_address_info` returns the region of each address, always with its `allocationBase`.

| `state` / `type` | Windows | Meaning |
|---|---|---|
| committed | `MEM_COMMIT` | Backed by storage; the only state that can be read or scanned, and the default filter. |
| reserved | `MEM_RESERVE` | Claimed, no storage; every access faults. Windows leaves its protection undefined. |
| free | `MEM_FREE` | Unused. Windows leaves its protection and type undefined; the tools usually show type `none`. |
| image | `MEM_IMAGE` | EXE and DLL sections; memory_list_regions names the `module`. |
| mapped | `MEM_MAPPED` | File views and shared sections, often an emulator's guest RAM. |
| private | `MEM_PRIVATE` | Heaps, stacks, `VirtualAlloc` memory, TEB/PEB, Cheat Engine and memory_allocate blocks. |

The tools reduce the Windows protection to four flags, `read`, `write`, `execute` and `copyOnWrite`:

| Windows | Reported | Typical content |
|---|---|---|
| `PAGE_NOACCESS` 01 | none | Barrier pages. |
| `PAGE_READONLY` 02 | read | PE header, `.rdata`. |
| `PAGE_READWRITE` 04 | read, write | Heaps, stacks, module globals already written. |
| `PAGE_WRITECOPY` 08 | read, write, copyOnWrite | Module globals not written yet. |
| `PAGE_EXECUTE` 10 | execute | Rare: executable but not readable. |
| `PAGE_EXECUTE_READ` 20 | read, execute | `.text`, JIT code. |
| `PAGE_EXECUTE_READWRITE` 40 | read, write, execute | Code caves, executable allocations, some JIT code. |
| `PAGE_EXECUTE_WRITECOPY` 80 | all four | Usually the allocation protection of image regions. |

- `write` includes copy-on-write, as in Cheat Engine's Writable scan option; `copyOnWrite` tells the two apart.
- The modifiers `PAGE_GUARD`, `PAGE_NOCACHE` and `PAGE_WRITECOMBINE` are dropped: a stack's guard page reports plain
  read-write.
- `memory_list_regions(format="detailed")` adds `allocationBase` (the module base for image regions),
  `allocationProtection` (the access the allocation was created with) and, when Cheat Engine reports one,
  `mappedFile`.

## Where data lives

| Data | Region | Survives a restart? | Reach it through |
|---|---|---|---|
| Code `.text` | image, read + execute | as module+offset | `game.exe+off`, or an AOB that survives updates |
| `.rdata`: constants, strings, vtables, usually the IAT | image, read | as module+offset | a vtable at object+0 names its class |
| Globals `.data`/`.bss` | image, read + write or copy-on-write | yes: static, green in CE | the root of a pointer chain |
| Heap objects | private, read + write | no | a chain from a static, or a code hook |
| Thread stacks | private, guard page below | no | `THREADSTACKn` |
| Thread-local data | private | no, one copy per thread | a hook on the code that uses it |
| JIT code (Mono, CoreCLR) | usually private, execute | no, rebuilt every run | Mono/.NET tools, or an AOB by range |
| Mapped views, emulator RAM | mapped | varies | view base + offset ([emulators](emulators.md)) |

## Why static means module+offset

- On x64 the linker fixes each global's RVA (offset from the image base) and code reaches it RIP-relative; 32-bit code
  holds absolute addresses that the loader relocates. Either way the whole image sits at one base, so every global
  lies at base + RVA.
- ASLR loads images at random bases (linkers set `/DYNAMICBASE` by default), and Windows also randomizes heaps and
  stacks. Microsoft describes image bases as chosen when the system boots, so a module can keep its base across
  restarts of the game, then move after a reboot. Never rely on that: store `game.exe+1A2B30`, an AOB or a chain,
  never an absolute address ([address expressions](address-expressions.md)).
- Heap addresses depend on allocation order, so a heap object needs a chain from a static ([pointers](pointers.md)).
  Freed blocks are reused: a stale pointer can reach another object of the same size. Check the class with
  `memory_get_address_info(addresses=["<object>"], includeRtti=true)` (MSVC classes with a vtable) or the vtable at +0.
- An update moves RVAs. Keep the `timeDateStamp` that `module_get(module="game.exe")` reports; when it changes, find
  code again by AOB ([AOB signatures](aob-signatures.md),
  [repair after an update](../Workflows/repair-after-update.md)).

## Module layout

`module_get(module="game.exe")` returns `base`, `size`, `path`, `is64Bit`, `sections` (name, address, size,
fileOffset and the PE header's executable and writable flags) and `pe` (machine, timeDateStamp, timeDateStampUtc,
headerImageBase, subsystem, isDll, managed, entryPoint, pdb).

- RVA = address - base. `headerImageBase` is usually rewritten to the load address; it is not the preferred base.
- Section flags come from the PE header; the pages' current access, in memory_list_regions, can differ.
- MSVC merges `.bss` into the tail of `.data`, which is then larger in memory than in the file. `.pdata` holds x64
  unwind entries (reliable function bounds). The TLS directory names the per-thread template data and the TLS
  callbacks, which run before the entry point.
- An import call goes through an IAT slot; module_list_imports lists the slots. code_disassemble prints the slot as an
  absolute address, such as `call qword ptr [7FF6A1DF1A30]`, never as module+offset:
  `memory_get_address_info(addresses=["7FF6A1DF1A30"])` gives its module+offset `symbol`. module_find_patches skips
  the IAT.
- `managed` marks a .NET assembly ([Mono and .NET](mono-and-dotnet.md)). IL2CPP static fields are not in a module:
  they sit behind each class's static-data pointer, and the class pointers are globals in GameAssembly.dll
  ([Unity IL2CPP](unity-il2cpp.md)).

## Stacks, thread-local data and JIT code

- A stack reserves 1 MB by default (the EXE header sets it) and commits pages as it grows down through a guard page.
  The x64 TEB (`gs:`) holds StackBase at +8, StackLimit at +10 and the PEB at +60.
- `THREADSTACKn` names a slot near the top of thread n's stack (Toolhelp order, 0 usually the main thread): Cheat
  Engine reads StackBase and takes the highest slot in the top 4096 bytes that points into kernel32.dll. Paths such
  as `THREADSTACK0-00000128` hold only while thread order and call chain repeat; validate them across restarts.
- `THREADSTACKn` is an address expression: `symbol_resolve(expressions=["THREADSTACK0"])` resolves it, and an address
  whose `region.allocationBase` equals that of `THREADSTACKn` lies in thread n's stack. CE's own pointer scanner can
  use stack roots; pointer_find_paths never does.
- Thread-local data has one copy per thread and no static address: hook the code that uses it
  ([find a writer](../Workflows/find-writer.md), [x64 injection](x64-injection.md)).
- JIT code has no module, so scope its signature by range:
  `aob_find(patterns=["<pattern>"], startAddress="<start>", endAddress="<end>", executable="required")`. First
  check `region.type` of a known JIT address with memory_get_address_info: code in a mapped view is skipped unless
  `aob_find.includeMapped` is true or Cheat Engine's MEM_MAPPED setting is ticked (see "Which tool answers what").

## Copy-on-write and guard pages

- Image pages are shared between processes. The first write to a copy-on-write page gives the writer its own copy,
  which then reports plain read-write (or read-write-execute). A `copyOnWrite` region is module data the game has not
  written yet; a scan that excludes copy-on-write skips it.
- A code patch changes only this process's copy until it exits; the file and other processes keep the original
  (module_find_patches lists the differences from the file).
- A guard page is a one-shot trap that grows stacks. Cheat Engine's scanner skips guard and no-access pages.
  Microsoft documents that a system call touching a guard page fails and clears the guard; treat a cross-process read
  as such a call. A thread whose guard is gone can crash when its stack next grows, so do not read a stack's lowest
  committed pages, and never change protection on a stack.

## Why a read fails

memory_read and the other range readers fail with `memory_read_failed` when any byte of the range is unreadable;
memory_read_batch reports it per item. Inspect the address with
`memory_get_address_info(addresses=["<address>"], includePointerValue=true)`:

| Cause | Sign | Do |
|---|---|---|
| Reserved or free | `region.state` not committed | Object gone or chain wrong; re-resolve from the root. |
| No read access | `region.protection.read` false | Usually not the data you want; look elsewhere. |
| Range runs into unreadable memory | it ends past `region.base` + `region.size` | Read less, or split at the region end. |
| Object not created yet | a chain hop fails | Retry in the right game state. |
| Guard page | reports read-write: a small committed region just above a stack's reserved part | Do not read it again. |

For a range with holes, `memory_create_snapshot(name="before", address="<address>", size=65536)` stores unreadable
bytes as zeros and lists them in `unreadable`; memory_dump_to_file can do the same in a file (next section).

## Compare, hash and back up

| Question | Call | Read |
|---|---|---|
| Did the bytes change, or come back after a disable? | `memory_hash(address="<address>", size=4096)` | `hash`: SHA-256 by default, up to 64 MiB |
| Where do two ranges differ? | `memory_compare(addressA="<a>", addressB="<b>", size=4096)` | `equal`, `differences`; up to 16 MiB |
| Which code differs from the module file? | `module_find_patches(module="game.exe")` | `patches` |
| Keep a copy before large writes | `memory_dump_to_file(address="<address>", size=65536, path="<file>", unreadable="zero")` | `bytesWritten`, `zeroFilled` |

- memory_dump_to_file writes only inside a folder of the `Mcp:Files:AllowedRoots` setting (empty by default, which
  refuses every dump; [configuration](configuration.md)). Unless `memory_dump_to_file.unreadable` is zero, an
  unreadable byte fails the dump and writes no file.
- `memory_load_from_file(address="<address>", path="<file>")` writes such a copy back. It changes the target: only with
  consent, into the same process and at the address the copy came from.

## Changing protection

This alters the target: only in single-player or offline software the user owns or may modify, after explaining it and
getting consent ([safety](safety.md)). Try the write first: Cheat Engine's own writers (Lua writeBytes, the Auto
Assembler) make the pages writable for the write and restore them. Change protection only when a write to a
read-only page still fails with `memory_write_failed`.

1. Read `region.base`, `region.size` and `region.protection` with `memory_get_address_info(addresses=["<address>"])`.
2. Pass all three flags: `memory_set_protection(address="<address>", size=4096, read=true, write=true, execute=false)`.
   The defaults are read only: a call with only address and size makes the range read-only.
3. Keep `previous` from the result; `current` is the first page's access after the change.
4. Restore when done, with the same address and size:
   `memory_set_protection(address="<address>", size=4096, read=<previous.read>, write=<previous.write>, execute=<previous.execute>)`.

| Flags passed | Pages become |
|---|---|
| read only | `PAGE_READONLY` |
| write, with or without read | `PAGE_READWRITE` |
| execute, with or without read | `PAGE_EXECUTE_READ` |
| write and execute | `PAGE_EXECUTE_READWRITE`; with read too, through Cheat Engine's fullAccess |
| none | `PAGE_NOACCESS`: the game crashes at its next touch |

A change Cheat Engine refuses fails with `host_refused`, and its hostEffect says what happened:

- `not_applied`: the access is unchanged.
- `started`: the access changed anyway. Read it again with memory_get_address_info and, with consent, restore the
  protection noted in step 1.
- `unknown`: the access could not be read again; inspect before anything else.

Some systems refuse write and execute together, and Cheat Engine's reason says so: make the range writable, write,
then make it executable. fullAccess can be refused too.

Cautions:

- The range must lie in one committed region and span at most 16 MiB: no region gives `not_found`, an uncommitted
  one `invalid_state`, a range past the region end `invalid_argument` on `size`, a larger one `limit_exceeded`.
- Whole pages change, with every other value on them.
- It is not a tracked resource: runtime_release_resources does not undo it. `previous` carries no copy-on-write or
  guard state, so neither can be restored.
- Removing write or execute from memory the game uses crashes it at its next write or execution there.
- A breakpoint whose debugger_set_breakpoint.method is `page_exception` also changes the access of whole pages while
  it exists; delete it before judging protection ([debugger](debugger.md)).

## Which tool answers what

| Question | Call | Read |
|---|---|---|
| What is here; is it static? | `memory_get_address_info(addresses=["<address>"], includeRtti=true)` | `symbol` (module+offset if static), `section`, `region`, `rttiClass` |
| Where is writable heap? | `memory_list_regions(type="private", writable="required")` | `base`, `size` |
| A module's pages | `memory_list_regions(module="game.exe", format="detailed")` | `protection`, `allocationBase` |
| Build, sections, CPU | `module_get(module="game.exe")` | `sections`, `pe`, `is64Bit` |
| What surrounds an address | `memory_list_regions(state="any", startAddress="<start>", endAddress="<end>")` | `state`, `type` |

- memory_list_regions pages with `offset` and `limit` (default 500, at most 2000) until `nextOffset` is absent. It
  re-reads the map on each explicit tool call. Each successful call prepares the complete map for five seconds;
  the live resource `cheatengine://instance/regions` reads that snapshot, with the same JSON for committed regions,
  100 per page by default. A missing/expired snapshot or target change produces `invalid_state` with a refresh
  hint. Resource navigation never enumerates the map. Prepare regions after module-list/detail refreshes, which
  invalidate an older prepared region map.
- Cheat Engine skips `MEM_MAPPED` regions in every scan unless MEM_MAPPED is ticked in its scan settings, which a
  fresh install leaves off ([emulators](emulators.md)). The main scanner follows that box and the scan panel's
  writable, executable and copy-on-write filters; `scan_get_status(scannerName="main")` reports them in `settings`.
  Named scans and AOB searches take the filters in the call and can add mapped memory for that one call:
  `scan_first(scannerName="emu", valueType="int32", value="100", startAddress="<start>", endAddress="<end>", writable="required", includeMapped=true)`
  or `aob_find(patterns=["<pattern>"], startAddress="<start>", endAddress="<end>", includeMapped=true)`
  ([value scans](value-scans.md)). The override is Cheat Engine-wide, and when the call ends it also clears one that
  a table or lua_execute set. aob_find and aob_find_value refuse it together with a module scope.

## 32-bit targets (WoW64)

Pointers are 4 bytes and user space ends below 4 GB; module_get reports `is64Bit` false and machine `x86`. The memory
tools size `pointer` values from the target's bitness. process_get_current.pointerSize is Cheat Engine's configured
width; if it is not 4 for a 32-bit target, `process_set_pointer_size(pointerSize=4)` corrects it.

## Editing a file as a process

Cheat Engine can open a file in place of a process (file-as-process): the memory tools then read and write Cheat
Engine's in-memory copy of the file. Use it for a file of single-player or offline software the user owns or may
modify, such as an offline save, with consent for each change ([safety](safety.md)).

1. `process_open_file(filename="<absolute path>", startAddress="0x10000000")` **changes the selected target**. Cheat
   Engine detaches its debugger when it can and no longer reaches the game. A resource MCP still owns
   (`runtime_list_resources()` lists them, orphans included) or a running main scan refuses the switch with `busy`;
   release it first ([errors and recovery](errors-and-recovery.md)).
2. Map offsets to addresses: Cheat Engine loads the whole file at `startAddress`, so address = startAddress + file
   offset, for `observedFileSize` bytes. Above, file offset 1A2B is `10000000+1A2B`.
3. Read with `memory_read(address="10000000+1A2B", valueType="int32")`, then write with consent:
   `memory_write(address="10000000+1A2B", valueType="int32", value="999", verify=true)`. For a big-endian file format
   set `memory_read.byteOrder` and `memory_write.byteOrder` to big_endian.
4. `process_save_file(filename="<new absolute path>")` writes the edited copy to a new file.
5. Delete any named scanner made on the file (`scan_delete(scannerName="<name>")`), then return to the game with
   `process_attach(process="game.exe")`.

Rules:

- Cheat Engine reads `process_open_file.startAddress` as a number, not an expression (CE source): write hex with `0x`.
  Bare digits are decimal, and a symbol gives 0. Without it the base is 0 and an address equals the file offset;
  whether every tool accepts address 0 was not checked live, so prefer a base such as the one above.
- `process_open_file.is64Bit` (default true) sets the pointer size Cheat Engine uses for pointer values and
  disassembly.
- There are no modules, symbols, stacks or threads: write hex addresses. CE source reports the file as one committed,
  private, read-write-execute region from the base, so memory_list_regions, scans and aob_find should cover it (not
  checked live). Reads outside the file fail. Keep writes inside it: a longer write can make Cheat Engine ask the
  user whether to grow the file.
- The file on disk does not change: Cheat Engine edits a copy loaded at open, and the path is held against writes and
  renames only while it opens.
- Saving needs a folder of `Mcp:Files:AllowedRoots` (empty by default, which refuses every save;
  [configuration](configuration.md)). The destination must not exist, so the opened file is never overwritten.
  Cheat Engine writes a protected temporary file that MCP then publishes; a failed publish is `partial_effect` and
  can leave a `.partial` file. The user replaces the original by hand and keeps it as the backup.
- Opening needs no root, only the path rules for reads. If the current table sets UsesMono, the open needs
  `Mcp:EnableTargetCodeExecution` and reports `monoAutoAttach` ([Mono and .NET](mono-and-dotnet.md)).
- A failure with hostEffect `completed` or `unknown` can leave the file selected: read `process_get_current()` before
  calling again.

## Sources

- Microsoft Learn (learn.microsoft.com/windows/win32): `memory/memory-protection-constants`,
  `memory/creating-guard-pages`, `api/winnt/ns-winnt-memory_basic_information`,
  `memory/memory-limits-for-windows-releases`, `procthread/thread-stack-size`, `debug/pe-format`;
  `/cpp/build/reference/dynamicbase`; security bulletin MS13-063 ("What is ASLR?").
- CE source, https://github.com/cheat-engine/cheat-engine: `memscan.pas` (region filter), `formsettingsunit.lfm`
  (MEM_MAPPED default), `CEFuncProc.pas` (`GetStackStart`), `symbolhandler.pas` (`THREADSTACK`), `LuaHandler.pas`
  (`writeBytes`, `setMemoryProtection`, `fullAccess`, `openFileAsProcess`, `saveOpenedFile`), `autoassembler.pas`,
  `NewKernelHandler.pas` (`DBKFileAsMemory`), `Filehandler.pas` (file reads, writes and region).
- celua.txt (Cheat Engine 7.7): `setMemoryProtection`, `fullAccess`, `openFileAsProcess`, `saveOpenedFile`.
- https://wiki.cheatengine.org/index.php?title=Help_File:Scan_settings ;
  https://en.wikipedia.org/wiki/Win32_Thread_Information_Block
