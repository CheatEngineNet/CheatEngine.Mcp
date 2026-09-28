# Cheat Engine Lua API cheat sheet

The CE 7.7 Lua functions that `lua_execute` chunks and table scripts need most: signatures, effects and known traps.
Read [Lua in CheatEngine.Mcp](lua.md) first, and use the dedicated tools each group names whenever they cover the job
([tool map](tool-map.md)).

## Before you write a chunk

- Signatures in backticks are copied from `celua.txt`, typos included; `OPTIONAL` arguments may be omitted. Confirm each
  one on the connected install, for example `lua_find_api(query="readString")`. It wins over this page, except where
  this page says CE does otherwise.
- Tags: **[R]** reads only; **[W]** changes target memory or state; **[X]** runs code in the target; **[K]** kernel
  driver or hypervisor; **[UI]** modal dialog or long wait on the calling thread; **[H]** acts on CE itself or the host.
- `lua_execute` runs on CE's main thread, so a **[UI]** call freezes CE and every MCP call to that instance
  ([CE's main thread](lua.md#ces-main-thread)).
- Get consent before any **[W]**, **[X]**, **[K]** or **[H]** call. Use them only on single-player or offline software
  the user owns or may modify, never on online or anti-cheat-protected games ([safety](safety.md)).
- From Lua these functions skip every `Mcp:Enable…` gate except `Mcp:EnableUnsafeLua`; `runtime_get_info` reports the
  gates. After a tool returns `capability_disabled`, never make the same change from Lua
  ([not a sandbox](lua.md#not-a-sandbox)).
- "(CE source)" marks behaviour read in CE's public Pascal source (GitHub master, older than 7.7), not verified on the
  7.7 binary.

## Conventions

- Addresses are integers or strings. A string is a CE address expression (`"game.exe+1A2B"`, `"[game.exe+10]+8"`, a
  registered symbol) whose numbers are hex: `"1234"` is 0x1234. Lua literals need `0x`. An expression can run Lua
  (`$name`, or an unknown name), so never pass unchecked text ([address expressions](address-expressions.md)).
- Reads return `nil` on failure, also when an address string does not resolve; writes return `false` when the write
  fails and `nil` when the address does not resolve (CE source). `getAddress`, `readBytes` and `writeBytes` raise on an
  unresolvable string. Check every result before arithmetic.
- Lua 5.3 integers are signed 64-bit: a `readQword` above 0x7FFFFFFFFFFFFFFF is negative. Use the native `& | ~ << >>`
  and format addresses with `string.format('%X', a)`.
- CE object arrays start at 0 (`sl[0]`, `mr.Offset[0]`, `fl.Address[0]`); plain tables CE returns start at 1.
- String lists, MemScans, found lists, disassemblers and timers without an owner live until `obj.destroy()`: free them
  in the same chunk, on the error path too.
- `*Local` variants (`readIntegerLocal`, `executeCodeLocal`) and `targetself` arguments act on CE's own process **[H]**.
- Constants (`vtDword`, `soExactValue`, `bptWrite`, `co_run`) are globals from `defines.lua`.

## Memory

Tools: `memory_read`, `memory_read_batch`, `memory_write`, `memory_copy`, `memory_hash`, `memory_list_regions`,
`memory_get_address_info`, `memory_allocate`, `memory_free`, `memory_set_protection`, `util_convert_value`.

- `readBytes(address,bytecount, ReturnAsTable )` **[R]**: pass `true` for one 1-based table (otherwise `bytecount`
  separate values). A partial read returns fewer bytes, so check `#t` (CE source).
- **[R]** `readShortInteger(address) / readByte(address)`, `readSmallInteger(address)`, `readInteger(address)`,
  `readQword(address)`, `readPointer(address)` (target pointer width), `readFloat(address)`, `readDouble(address)`.
  `readSmallInteger` and `readInteger` are unsigned unless the second argument is `true`.
- `readString(address, maxlength, widechar OPTIONAL)` **[R]**: reads to the 0 terminator. `maxlength` counts **bytes**,
  also for UTF-16 (pass twice the characters), and defaults to 50 (CE source). Non-UTF-8 bytes reach MCP as U+FFFD.
- **[W]** `writeInteger(address,value)`, `writeQword(address, value)`, `writeFloat(address,value)`,
  `writeDouble(address,value)`: `true` on success. `writeBytes(address, table)` **[W]** returns the number of bytes
  written instead, and lifts the page protection for the write (CE source).
- `writeString(address,text, widechar OPTIONAL)` **[W]** writes no terminator (CE source): append `'\0'` if needed.
- `copyMemory(sourceAddress: integer, size: integer, destinationAddress:integer SEMIOPTIONAL, Method:integer OPTIONAL)`
  **[W]**: method 0 target to target, 1 target to CE, 2 CE to target, 3 CE to CE; without a destination CE allocates
  one. Returns the copy's address, or `nil`.
- `enumMemoryRegions()` **[R]** returns the whole map: filter it before returning. `getMemoryRegionInfo(address)`,
  `getMemoryProtection(address)` (lower-case `r`, `w`, `x`) and `md5memory(address, size)` **[R]** cover one address
  or range.
- `allocateMemory(size, BaseAddress OPTIONAL, Protection OPTIONAL)` and `deAlloc(address, size OPTIONAL)` **[W]**: MCP
  does not track Lua allocations; `memory_allocate` does.
- `fullAccess(address,size)` **[W]** makes a range writable and executable: `true` or `false`.
- `setMemoryProtection(address, size, {r:boolean; w: Boolean; x: Boolean})` **[W]**: CE 7.7 reads the upper-case keys
  `R`, `W` and `X`, whatever `celua.txt` shows; a missing key is `false`, so `{r=true, w=true}` sets PAGE_NOACCESS and
  the target's next access faults. Write `{R=true, W=true, X=false}`; W or X always includes read. It returns the old
  protection number, or `false` and a reason when the system refuses W and X together (make the range writable, write,
  then executable), or `false` alone (CE source). Prefer `memory_set_protection` ([memory model](memory-model.md)).

## Symbols, modules and the process

Tools: `symbol_resolve`, `symbol_find`, `symbol_register`, `symbol_list_registered`, `symbol_reload`, `module_list`,
`module_get`, `process_attach`, `process_list_threads`, `process_set_paused`.

- `getAddressSafe(string, local OPTIONAL, shallow OPTIONAL)` **[R]**: `nil` when unresolved; a number comes back
  unchanged. Prefer it to `getAddress(string, local OPTIONAL)`, which raises.
- `errorOnLookupFailure(state)` is CE-wide: `false` makes every script's failed lookup return 0 instead of raising.
  Leave it alone; a chunk that must flip it restores the previous state it returns before ending
  ([MCP's own state](lua.md#leave-mcps-own-state-alone)).
- `getNameFromAddress(address,ModuleNames OPTIONAL=true, Symbols OPTIONAL=true, Sections OPTIONAL=false)` **[R]**:
  symbol, `module+offset` or hex.
- `enumModules(processid OPTIONAL)` **[R]**: `{Name, Address, Size, Is64Bit, PathToFile}` per module. Also **[R]**:
  `getModuleSize(modulename)`, `enumSectionsOfModule(modulebase/modulename)`, `inModule(address)`,
  `inSystemModule(address)`, `getRTTIClassName(address)`.
- `registerSymbol(symbolname, address, OPTIONAL donotsave)` / `unregisterSymbol(symbolname)` **[H]**: saved with the
  table unless `donotsave`. `symbol_list_registered` lists such a symbol as not owned, and MCP never removes it.
- `reinitializeSymbolhandler(waittilldone: BOOLEAN OPTIONAL, default=TRUE)`, `waitForSections()`, `waitForExports()`,
  `waitForPDB()` **[UI]**: they wait for symbol loading.
- `openProcess(processid) / openProcess(processname)` **[H]**: a name with no exact match falls back to a
  case-insensitive substring match (CE source), so `"game"` can open `gamelauncher.exe`. Prefer `process_attach`, then
  check `getOpenedProcessID()` (0 = none).
- **[R]** `targetIs64Bit()`, `getPointerSize()`, `getThreadlist()` (7.7: index 1 is usually the main thread),
  `isPaused()` (also true while broken on a breakpoint). `pause()` / `unpause()` **[W]** are untracked;
  `process_set_paused` tracks its pause, which `runtime_release_resources` resumes.

## Array-of-bytes scans

Tools: `aob_find` (bounded, reports uniqueness), `aob_find_value`, `aob_generate_signature`.

- `AOBScan(aobstring, OPTIONAL protectionflags, OPTIONAL alignmenttype, OPTIONAL alignmentparam)` **[R]**: a StringList
  of hex addresses without `0x` (`tonumber(sl[0], 16)`), or `nil` when nothing matches (CE source). Read it inside a
  `pcall`, then call `sl.destroy()` on both paths.
- **[R]** `AOBScanUnique(aobstring, OPTIONAL protectionflags, …)- Integer` and `AOBScanModuleUnique(modulename,
  aobstring, OPTIONAL protectionflags, …)- Integer` (abridged) return the first match found or `nil`; with several
  matches it can be any of them, and their alignment arguments are ignored (CE source). Prove uniqueness with
  `aob_find(patterns=["48 8B 05 ?? ?? ?? ??"], module="game.exe", limit=2)`.
- Protection flags: `X` executable, `W` writable, `C` copy-on-write, each prefixed `+` (required), `-` (excluded) or
  `*` (either); `"+X-C-W"` for code, `"+W-C"` for data, empty for all memory. Alignment type 0 none, 1 divisible by,
  2 last digits; its parameter is a string.
- Scans skip mapped memory (emulator RAM) unless CE's settings include it. Prefer `includeMapped` (`aob_find`,
  `aob_find_value`, a named `scan_first`) to `setSpecialScanOptionsOverride({MEM_MAPPED=true})`, which is CE-wide and
  which every `includeMapped` call ends.
- `getUniqueAOB(address): AOBString,Offset` **[R]** takes a number, not an expression, and scans the module that holds
  the address, or the whole address space when no module does. Without a unique pattern it returns text starting with
  `ERROR:` and a meaningless offset (CE source). `aob_generate_signature` refuses an address outside a module, and
  above 64 MiB builds the pattern itself. Every scan holds the calling thread ([AOB signatures](aob-signatures.md)).

## Disassembler and assembler

Tools: `code_disassemble`, `code_decode`, `code_get_function`, `asm_assemble`, `asm_check`, `asm_apply`.

- `disassemble(address)` **[R]**: one string, `"address - bytes - opcode : extra"`.
- `splitDisassembledString(disassembledstring)` **[R]**: documented as address, bytes, opcode, extra, but it returns
  the reverse, extra first (CE source; 7.7's `autorun/pseudocodediagram.lua` reads the third value as the bytes). Use
  `LastDisassembleData` instead.
- `createDisassembler()` **[R]**: `d.disassemble(address)` fills `d.LastDisassembleData` (`address`, `prefix`,
  `opcode`, `parameters`, `bytes` from 1, `isJump`, `isCall`, `isRet`, `isRep`, `isConditionalJump`, `modrmValue`,
  `parameterValue`); `d.destroy()` afterwards. Leave `getDefaultDisassembler()` alone: it is CE's.
- Estimates **[R]**: `getInstructionSize(address)`, `getPreviousOpcode(address)`, `getFunctionRange(address)` (start
  and stop).
- `assemble(line, address OPTIONAL, assemblePreference OPTIONAL, skipRangeCheck OPTIONAL)` **[R]**: a byte table,
  nothing written.
- `autoAssembleCheck(text, enable, targetself)` **[R]**: `true`, or `false` and usually a message. CE still runs the
  script's `{$lua}` blocks during a check, with `syntaxcheck` true (CE source); `asm_check` refuses such scripts.
- `autoAssemble(text, targetself OPTIONAL, disableInfo OPTIONAL)` / `autoAssemble(text, disableInfo OPTIONAL)` **[W]**
  **[X]**: `true` and a disableInfo table (`allocs`, `registeredsymbols`, `symbols`, `exceptionlist`,
  `ccodesymbols`), or `false` and usually a message. `autoAssemble(sameScript, disableInfo)` runs `[DISABLE]`. MCP
  never sees a Lua patch; `asm_apply` keeps a lease listed by `asm_list_patches` ([Auto Assembler](auto-assembler.md)).

## Address list and memory records

Tools: `record_list`, `record_get`, `record_create`, `record_update`, `record_set_active`, `record_set_script`,
`record_set_dropdown`, `record_delete`.

- `getAddressList()` returns `AddressList`: `Count`, `[i]` from 0, `getMemoryRecordByID(ID)`,
  `getMemoryRecordByDescription(description)`, `createMemoryRecord()`. Prefer `ID` to an index, but look records up
  again after a table load: IDs come from the loaded file, so an old ID can name another record (CE source).
- Record fields: `Description`, `Address` (base expression), `OffsetCount`, `Offset[]`, `CurrentAddress` (resolved),
  `Type` (number) and `VarType` (string), `Script`, `DropDownList`. For UTF-16 set `Type` to `vtString`, then
  `String.Unicode` and `String.Size` (characters); `record_create` takes type 6 with `unicode` and `length`.
- Setting `Value` (a string) writes the target **[W]**. For number types CE runs a value in brackets as Lua, with
  `value` the current one: `'[value*2]'` (CE source). Setting `Active` freezes a value record **[W]** or runs the
  `[ENABLE]`/`[DISABLE]` section of a script record **[X]**; read `Active` back to confirm.
- **Offset order:** CE applies `Offset[OffsetCount-1]` first and `Offset[0]` last, next to the value. CE 7.7's
  `monoscript.lua` stores `[[X.Static]+field]+C` as `Offset[0]` 0xC and `Offset[1]` field.
  `mr.setAddress(base, {offsets})` puts the table's first item in `Offset[0]` (CE source): list offsets last to first.
  MCP tools take and return signed hex strings in dereference order, nearest the base first ([pointers](pointers.md)).
- Setting `Address` clears the offsets (CE source): set it first, then the offsets.
- `onMemRecPreExecute` and `onMemRecPostExecute` are shared global hooks: chain them, never replace them.
- `OnActivationFailure(memrec, reason, text)`: 7.7 passes `reason` 0 (unknown) to 11, one more than `defines.lua`'s
  `af*` constants (`afInaccessible` is 0; CE passes 1). `record_set_active` reports it as `failure`.

## Structures

Tools: `structure_create`, `structure_add_elements`, `structure_autoguess`, `structure_read`, `structure_get`.

- `createStructure(name)` stays private until `s.addToGlobalStructureList()`; the table saves global structures.
- `getStructureCount()`, `getStructure(index)`, `getStructure(name)` (case-sensitive).
- `s.addElement()` returns an element: set `Offset`, `Name`, `Vartype`, `Bytesize` (strings, byte arrays),
  `ChildStruct`. Wrap batches in `s.beginUpdate()` / `s.endUpdate()`.
- `s.autoGuess(baseaddresstoguessfrom, offset, size)` guesses from live bytes. `e.getValue(address)` **[R]** and
  `e.setValue(address,value)` **[W]** use `address` as given; `e.getValueFromBase(baseaddress)` adds the element's
  offset ([structures](structures.md)).

## Value scans (MemScan and FoundList)

Tools: `scan_first`, `scan_next`, `scan_list_results`, `scan_get_status`, `scan_stop`, `scan_reset`, `scan_delete`.

- `getCurrentMemscan()` is the user's visible scan and MCP's `main` scanner: do not drive it from Lua.
- `createMemScan(progressbar OPTIONAL)` makes a private scanner. `ms.firstScan(...)` is positional: `scanoption,
  vartype, roundingtype, input1, input2, startAddress, stopAddress, protectionflags, alignmenttype, "alignmentparam",
  isHexadecimalInput, isNotABinaryString, isunicodescan, iscasesensitive`. Never drop an empty string or `false`.
- `ms.waitTillDone(timeout OPTIONAL)` **[UI]** blocks until the scan ends (`false` on timeout); in `lua_execute` use a
  named scanner through `scan_first` instead ([value scans](value-scans.md)).
- Results: `fl = createFoundList(ms)`, `fl.initialize()`, then `fl.Count` and `fl.Address[i]` (hex strings from 0);
  finally `fl.destroy()` and `ms.destroy()`. 7.7 also reports `ms.FoundCount`.

## Debugger

Tools: `debugger_attach`, `debugger_set_breakpoint`, `debugger_start_capture`, `debugger_get_context`,
`debugger_continue`, `debugger_start_trace`, `debugger_list_breakpoints` ([debugger](debugger.md)).

- `debugProcess(interface OPT)` **[W]**: 0 default, 1 Windows, 2 VEH (injects a DLL, **[X]**), 3 kernel **[K]**.
- `debug_setBreakpoint(address, size OPTIONAL, trigger OPTIONAL, breakpointmethod OPTIONAL, functiontocall() OPTIONAL)`
  **[W]** starts the debugger without asking (CE source). Triggers `bptExecute` (0, size ignored), `bptAccess` (1),
  `bptWrite` (2); methods `bpmInt3` (0), `bpmDebugRegister` (1), `bpmException` (2), by default CE's preferred method
  (CE source). 7.7 returns a success flag, then the ID (or an error message); remove the breakpoint with
  `debug_removeBreakpointByID(id)`. Pass `fn`, not `fn()`.
- `debugger_list_breakpoints` shows a Lua breakpoint as not owned, and `debugger_delete_breakpoint` refuses it.
- Hardware breakpoints use the 4 debug registers, each watching 1, 2, 4 or 8 aligned bytes (8 on 64-bit only). CE
  splits a misaligned range into aligned pieces, one register each, silently keeping a partial watch when registers
  run out (CE source); MCP's tools refuse it. Data breakpoints fire after the access: the accessing instruction is
  `getPreviousOpcode(RIP)`, unless RIP is on a `rep` instruction (7.7's AITools checks `isRep`).
- `debug_continueFromBreakpoint(continueMethod)`: `co_run` (0), `co_stepinto` (1), `co_stepover` (2).
- `debug_getContext(BOOL extraregs)`, `debug_setContext(BOOL extraregs)`, `debug_getCurrentContextTable(BOOL extraregs)`
  work only while a thread is broken. Callbacks see registers as globals (`EAX` to `EIP` and `EFLAGS`; `RAX` to `R15`
  and `RIP` on 64-bit); edits are applied when the callback returns (CE source). Otherwise use `debug_setContext`.
- Callbacks run on CE's main thread while the thread waits; with the Windows debugger the whole target is suspended.
  Never block or call Mono in one.
- A global `debugger_onBreakpoint` set from Lua makes `debugger_start_trace` refuse; during a trace it is the trace's.

Callback return values (breakpoint rows: CE source, which reads the value as an integer, so `true` counts as 0):

| Callback | Returns | Effect |
|---|---|---|
| Global `debugger_onBreakpoint()` (no breakpoint function) | `0` or nothing | The thread stays broken; CE shows it |
| | anything else | The thread continues, with the last method given to `debug_continueFromBreakpoint` |
| `functiontocall` of `debug_setBreakpoint` | `0` or nothing | The thread continues |
| | anything else | The thread stays broken until continued |
| `debugger_onModuleLoad(modulename, baseaddress)` | `1` | Break (Windows debugger only) |
| Record `OnActivate` / `OnDeactivate` while `before` is true | anything but `true` | The change is cancelled |
| Record `OnActivationFailure` | `true` | CE retries: avoid loops (`record_set_active` stops after 8) |

## Timers, threads and the main thread

- `createTimer(delay, function(...),...)` runs once and destroys itself: never touch it afterwards.
  `createTimer(owner OPT, enabled OPT)` lives until `t.destroy()` (or its owner's): set `t.Interval` (ms) and
  `t.OnTimer` (runs on the main thread); it is enabled by default.
- `createThread(function(Thread,...), ...)` runs on a worker: `synchronize(...)`, `waitfor(timeout)`, `terminate()`,
  `Terminated`. By default the object frees itself when the function ends (`freeOnTerminate`).
- Workers must reach forms, the address list and records only through `synchronize(function(...), ...)`, which waits
  for the main thread, so it waits as long as a `lua_execute` chunk runs.
- `createThreadNewState(scripttext)` runs in a new Lua state without your functions and with a limited CE API; it must
  poll `t.Terminated`, and it does not free itself.
- In `lua_execute` you are already on the main thread (`inMainThread()` is true): `synchronize` is unnecessary, and
  `sleep(milliseconds)`, `t.waitfor()` and `processMessages()` are traps ([CE's main thread](lua.md#ces-main-thread)).

## Code execution and injection

All **[X]** unless marked; the matching tools need `Mcp:EnableTargetCodeExecution`. CE runs a remote call on a new
thread, not the game's, which can crash or deadlock the target ([x64 injection](x64-injection.md));
`exec_call_remote` and `exec_call_method` refuse a paused or broken target.

- `executeCodeEx(callmethod, timeout, address, {type=x,value=param1} or param1,{type=x,value=param2} or param2,...)`,
  tool `exec_call_remote`: `callmethod` 0 stdcall, 1 cdecl (32-bit targets). Types: 0 integer or pointer, 1 float,
  2 double, 3 ASCII string, 4 wide string (the tools take no strings). On a 32-bit target CE writes a double's low
  half twice (CE source); the tools pass two dwords. Timeout `nil` or -1 waits forever; 0 does not wait and leaks the
  call memory. Returns E/RAX.
- `executeMethod(callmethod, timeout, address, {regnr=0..15,classinstance=xxxxxxxx} or classinstance, ...)`
  (abridged), tool `exec_call_method`: the instance goes in a register, ECX (1) by default. On x64, CE then still
  loads the arguments from RCX (RDX, R8, R9 or XMM0-3 next), so a first integer argument replaces the instance in RCX
  (CE source): pass the instance as argument 1 of `executeCodeEx`, as `exec_call_method` does.
- `executeCode(address, parameter OPTIONAL, timeout OPTIONAL)`: one stdcall parameter.
  `executeCodeLocal(address, parameter OPTIONAL)` runs inside CE **[H]**, tool `exec_call_local`.
- `injectDLL(filename, skipsymbolreloadwait OPTIONAL)` / `injectLibrary(filepath, skipsymbolreloadwait OPTIONAL)`, tool
  `exec_inject_library`. `injectDotNetDLL(dllpath, FullClassName, MethodName,parameterstring, timeout optional)`, tool
  `exec_inject_dotnet`: 7.7's `autorun/DotNetInject.lua` never reads `timeout` and waits, without a limit, until the
  method returns **[UI]**.
- `compile(text, address OPTIONAL, targetself OPTIONAL, kernelmode OPTIONAL, nodebug OPTIONAL)` **[W]**, tool
  `exec_compile_c`: TCC C code; symbol addresses, or `nil` and a message. `kernelmode` makes it **[K]**.

## Speedhack, Mono and .NET

- `speedhack_setSpeed(speed)` **[X]** (`autorun/SpeedhackV3.lua`) injects CE's helper library and hooks the time
  functions, once: after `speedhack_wantedspeed` exists it never hooks that process again, even after a failure. 1.0
  leaves the hooks in place; a failure shows a modal dialog **[UI]**. Use `speedhack_set_speed` and
  `speedhack_get_state` ([speedhack](speedhack.md)).
- The Mono helpers (`LaunchMonoDataCollector()`, `mono_findClass(namespace, classname)`, …) come from
  `autorun/monoscript.lua`, not `celua.txt`, so `lua_find_api` cannot find them. They inject a collector DLL **[X]**
  and talk to it over a pipe. On the main thread, a call that stalls for `mono_timeout` (5000 ms), for example while
  the target is paused or broken, opens a modal "about to timeout" dialog **[UI]**. Use the `mono_*` tools
  ([Mono and .NET](mono-and-dotnet.md)); they use a collector Lua launched, but `mono_detach` closes only its own.
- For `getDotNetDataCollector()`, use the `dotnet_*` tools instead.

## Functions to avoid in automation

- **[UI]**: `showMessage`, `messageDialog`, `inputQuery`, `showSelectionList`, `requireAdmin`; `createForm` opens a
  window that outlives the call.
- **[H]**: `shellExecute`, `runCommand`, `deleteFile`, `io.*`, `os.*`, `loadPlugin`, `resetLuaState`, `closeCE`.
- **[K]**: `dbk_*`, `dbvm_*`, `enableDRM`, `debugProcess(3)`. They can bugcheck the machine, and from Lua they bypass
  `Mcp:EnableKernelAccess`; use the `kernel_*` tools ([kernel](kernel.md)).

## Recipe

A pointer walk equal to `pointer_read_chain(base="game.exe+1A2B30", offsets=["10","4C8"])`, which a record stores as
`mr.setAddress('game.exe+1A2B30', {0x4C8, 0x10})`:

```lua
local p = getAddressSafe('game.exe+1A2B30')
if p == nil then return { ok = false, error = 'base not found' } end
for _, offset in ipairs({ 0x10, 0x4C8 }) do
  local v = readPointer(p)
  if v == nil then return { ok = false, error = string.format('unreadable at %X', p) } end
  p = v + offset
end
return { ok = true, address = string.format('%X', p), value = readInteger(p) }
```

The full procedure is the [write_lua_script workflow](../Workflows/write-lua-script.md).

## Sources

- Cheat Engine 7.7 `celua.txt`, `defines.lua`, `autorun/monoscript.lua`, `autorun/SpeedhackV3.lua`,
  `autorun/DotNetInject.lua`, `autorun/luasymbols.lua` and `Extensions/AITools/tools/aitools.lua` in the installation
  folder (read locally, not redistributed).
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaMemoryRecord.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MemoryRecordUnit.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/debugeventhandler.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/autoassembler.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/frmautoinjectunit.pas
- https://learn.microsoft.com/windows/win32/debug/debugging-events
- https://wiki.cheatengine.org/index.php?title=Lua
- https://en.wikipedia.org/wiki/X86_debug_register
