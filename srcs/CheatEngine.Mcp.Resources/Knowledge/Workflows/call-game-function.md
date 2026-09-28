# Call a game function once

## Goal

Call the game function at `{functionAddress}` once to achieve `{purpose}`, as a method of the object at
`{instanceAddress}` when given, then verify the effect. Single-player or offline software the user may modify only.
It runs game code on a new thread and cannot be undone, only compensated.

## Steps

1. `process_get_current()`: `pointerSize` 8 is x64, 4 is x86. `debugger_get_status()`: a `broken` or paused target
   is refused.
2. `code_get_function(address="{functionAddress}")`: `startAddress` should equal the returned `address`, the entry.
   Then `code_disassemble(address="{functionAddress}", count=30)`: which of RCX, RDX, R8, R9 and XMM0-XMM3 (x86: the
   stack) it reads, and whether it ends with `ret N`.
3. Arguments from a caller: dissect the `.text` section of `module_get(module="{functionAddress}")` 1 MiB at a time
   (`code_start_dissect(address="<window>", size=1048576)`, polled with `code_poll_job(jobId="<jobId>")` until
   `completed`) until `code_find_references(address="{functionAddress}")` lists a caller. Then
   `code_disassemble(address="<fromAddress>", before=12, count=2)` shows what the caller loads: each argument's type,
   a typical value (hexadecimal) and whether RCX (x86: ECX) holds an object.
4. Agree on the arguments with the user. Text or a struct goes in a buffer, with consent:
   `memory_allocate(name="call-arg", size=<bytes>)`, then
   `memory_write(address="<address>", valueType="string", value="<text>", nullTerminate=true)`: `wstring` for
   wchar_t text (as the caller's text reads), `bytes` without a terminator for a struct.
5. Read what the call should change (`memory_read`) and ask the user to save the game.
6. With consent, naming the function, the arguments and the risk, call once, integers and the buffer's `address` as
   0x hexadecimal:
   `exec_call_remote(functionAddress="{functionAddress}", arguments=[{type="integer", value="0x3E8"}], timeoutMilliseconds=10000)`,
   or for a method
   `exec_call_method(functionAddress="{functionAddress}", classInstance="{instanceAddress}", arguments=[...])` with
   `exec_call_method.classRegister` 1 (x64: `this` in RCX, argument 1 in RDX or XMM1). On x86, set
   `exec_call_remote.callingConvention` to cdecl when the caller pops the arguments (`add esp,N`).
7. `returnValue` is RAX (x86: EAX) as decimal text; a float result is not reported. Verify with the read of step 5
   and in game, then `memory_free(name="call-arg")` unless the function keeps the pointer.

## Decisions

- `busy`, `hostEffect` not_started: the target is paused or at a breakpoint; with consent resume it
  (`process_set_paused(paused=false)` or `debugger_continue`), then call once.
- A timeout, or `host_refused` with `hostEffect` unknown: the thread may still run. Never call again; read the value
  of step 5, wait, read again, and keep the buffer until the effect is known.
- No caller found, or unclear arguments: never guess types; find a caller through a text it shows
  ([find code by string](find-code-by-string.md)) or ask the user.
- Code that draws, spawns, loads, saves or runs a script engine (Unity, Unreal, Lua) may need the main thread or its
  locks and can deadlock or crash: prefer a function that only updates data, or an [AOB injection](aob-injection.md).
- A method without `{instanceAddress}`: step 3 shows where the caller takes `this` from; find that object
  ([identify an address](identify-address.md)) and confirm it with the user.
- More than 16 arguments, or anything but numbers and pointers: use an injection.

## Pitfalls

- One consent, one call: calling again repeats the effect (1000 gold twice).
- Digits alone are decimal: pass the disassembly's `mov edx,00000064` as `"0x64"` (100), and a `memory_allocate`
  `address` such as 3250000 as `"0x3250000"`.
- A function may keep the buffer's address (a name setter): freeing it then corrupts the game.
- The effect outlives the session: undo it with the opposite function or by writing the old value back.

## Report

The function as module+offset, the arguments, `returnValue`, the values before and after, and each buffer freed or
kept. See [x64 injection](../Documents/x64-injection.md), [code analysis](../Documents/code-analysis.md) and
[safety](../Documents/safety.md).
