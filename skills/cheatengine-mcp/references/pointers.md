# Pointers and pointer scans

A dynamic address (heap or stack) moves after a restart or level load. A pointer chain reaches it from a static root instead. Find the dynamic address first (see [value-scans](value-scans.md)), then build a chain manually or with the pointer scanner.

## Notation

- CE writes chains as `[["game.exe"+1A2B30]+10]+4C8`. Read the pointer at `game.exe+1A2B30`, add `10`, read the pointer there, add `4C8`: that is the final address.
- Every number is hex. Each pair of brackets is one pointer read, so N offsets means N reads; the last offset is added, not read.
- MCP tools take `base` (`game.exe+1A2B30`, a registered symbol, or a hex address) and `offsets` as signed hex strings in **dereference order**: `["10", "4C8"]`. Negative offsets are written `"-8"`.
- CE stores record offsets in reverse: its internal index 0 is the offset nearest the final value, and a `.CT` file lists that one first. The `record_*` tools convert for you. When you copy offsets from CE's UI or a table file, reverse them.
- Pointer width follows the target: 8 bytes on x64, 4 bytes on x86 (WOW64). Check `process_get_current` for `pointerSize` and `pointerSizeMismatch`; fix a mismatch with `process_set_pointer_size` before any pointer work.

## Read and verify a chain

- `pointer_read_chain(base="game.exe+1A2B30", offsets=["10","4C8"], valueType="int32")` returns every hop (`readAt`, `pointer`, `offset`, `next`), the final `address` and the value.
- An unreadable hop fails with `memory_read_failed` and `details` naming the hop. That usually means the object does not exist yet (menu, loading screen). Retry in the right game state; do not rewrite the chain.
- Register a reusable root with `symbol_register` (for example `playerBase`), then use it as `base` or inside records.

## Manual method (from a writer instruction)

1. Find the instruction that writes the value: `debugger_start_capture(address, trigger="write")`, trigger the change in game, then `debugger_poll_capture`. The hardware trap reports the instruction after the access; confirm the real one with `code_disassemble` (see [debugger](debugger.md)).
2. Read the operand. For `mov [rbx+000004C8],eax` the offset is `4C8` and the base is the `RBX` value in the hit's registers. Check that `RBX + 4C8` equals your address.
3. Find who holds that base: `pointer_find_references(target=<RBX>, maxOffset=0)`. Raise `maxOffset` to also catch pointers into the middle of the object. A value scan works too: `scan_first(scannerName="ptr1", valueType="pointer", comparison="exact", value="0x<RBX>")`.
4. Keep static results: those with a module+offset `symbol` (check with `memory_get_address_info`). One static holder gives `["game.exe"+X]+4C8`.
5. If every holder is dynamic, repeat one level up: capture `trigger="access"` on a holder, read its instruction (for example `mov rbx,[rsi+18]`), and search for `RSI`. Each level adds an offset at the front of the dereference list: `[["game.exe"+Y]+18]+4C8`.
6. Verify with `pointer_read_chain`, save it (see [cheat-tables](cheat-tables.md)), restart the game and verify again.

Pitfalls:

- Shared code writes many objects; keep only hits whose computed address is yours (see [structures](structures.md) for filtering).
- The register may change between the load and the write. Read the few instructions before the writer.
- `[rax+rcx*4+10]` indexes an array. The chain needs the object base, not the scaled index.
- A function argument or `this` pointer arrives in `RCX` on x64 Windows. A capture at the function entry gives the object base directly.

## Pointer scanner flow

1. `pointer_create_map(mapName="run1")` captures a snapshot of every pointer-sized value that points into memory. It is a job: poll `pointer_list_maps` until `state` is ready. Optional `startAddress`/`endAddress`, `writableOnly` and `alignment` narrow it.
2. `pointer_find_paths(scanName="hp", mapName="run1", target=<address>)` searches the map for chains back to static roots. It is also a job: poll `pointer_list_scans`. Key options: `maxDepth` (levels), `maxOffset` (largest field offset), `staticRootsOnly`, `allowNegativeOffsets`, `maxResults`, `maxNodes`.
3. `pointer_list_paths(scanName="hp", sortBy="depth")` pages paths: `expression`, `module`, `moduleOffset`, `base`, `offsets` (dereference order) and `verification`.
4. Restart the game or reload the level, find the value again, then `pointer_rescan_paths(scanName="hp", target=<new address>)`. Without `mapName` it resolves every path live; with a fresh map (`pointer_create_map(mapName="run2")`) it checks against that snapshot. Module roots rebase by module name; absolute roots stay absolute.
5. Repeat step 4 across two or three restarts and different game states (other level, after death, after a menu). The count should settle.
6. Pick a chain, confirm it with `pointer_read_chain`, create a record, then free memory with `pointer_delete_scan` and `pointer_delete_map`.

- Start with `maxDepth` 4-5 and `maxOffset` 1024-4096 bytes; widen only when nothing is found. Newer engines need deeper chains and larger offsets. Check the live schema for defaults and caps.
- Maps and scans live in managed memory for this activation. They survive a target switch (so rescans work after a restart) but not a plugin disable, and they are never CE `.PTR` files. Up to 4 maps are kept per instance.
- Map capture reads a live target; values can change mid-capture. Pause the target with `process_set_paused` if the game allows it.
- Cancel with `pointer_delete_map` or `pointer_delete_scan`, or `runtime_stop_job` with the id shown by `runtime_list_jobs`.

## Ranking chains

Prefer, in order:

1. A root in the game's main module (`game.exe`, `GameAssembly.dll`) rather than a system or driver DLL.
2. Fewer levels.
3. Small offsets that look like fields (multiples of 4 or 8, below hex `1000`).
4. Survival across every rescan and game state tried.
5. Hops that land on recognisable objects: check them with `memory_get_address_info(includeRtti=true)` (see [structures](structures.md)).

Many chains reaching the same value is normal; you only need one reliable chain.

## Reading the result flags

| Flag | Meaning | Action |
|---|---|---|
| `incomplete` (map) | capture hit `maxBytes`/`maxPointers` or skipped regions | narrow the range or raise caps |
| `unreadableBytes` > 0 | guarded or no-access pages were skipped | usually fine; incomplete coverage |
| `traversalLimited` | search hit `maxNodes` or `maxResults` | tighten depth or offset, or raise caps |
| `unresolved` (rescan) | a hop could not be read live | rescan when the object exists; it is not verified |
| `removed` (rescan) | path no longer reaches the target | expected; it was a false path |

- `incomplete` or `traversalLimited` with zero results is **not** proof that no chain exists.
- Unresolved paths are kept unless `dropUnresolved=true`. Report them as unverified, never as matches.

## Limits and alternatives

- No negative offsets unless `allowNegativeOffsets=true`, which multiplies results and search time.
- No thread-stack roots. CE's own pointer scanner can use `THREADSTACKn` roots; this one cannot.
- Roots must be static (module) addresses when `staticRootsOnly=true`.

When the scanner fails, use one of these:

- **Injection copy base.** Find an instruction that touches only your object (an access capture shows a single address). Inject `globalalloc(playerBase,8)` and `mov [playerBase],rbx` (see [auto-assembler](auto-assembler.md)), then point records at `[playerBase]+4C8`. The symbol reads 0 until the code runs, and it goes stale after a reload until the code runs again. This needs the `EnableAutoAssembler` gate.
- **RIP-relative globals.** x64 code loads singletons with `mov rax,[rip+disp32]`, shown by CE as `mov rax,[game.exe+1A2B30]`. That global is a static root. `code_start_search(module="game.exe", mode="rip_relative")` lists them as a job and returns a `jobId`: poll `code_poll_job(jobId, afterSequence, limit)` and read `nextAfterSequence` and `more`, then stop it with `runtime_stop_job(jobId)`. Disassembling the writer's function often shows the root directly (see [code-analysis](code-analysis.md)).
- **Managed runtimes.** Mono and .NET static fields have direct addresses (`mono_get_static_field_address`, `dotnet_get_type`); see [mono-and-dotnet](mono-and-dotnet.md).

## Sources

- https://wiki.cheatengine.org/index.php?title=Tutorials:Pointers
- https://wiki.cheatengine.org/index.php?title=Help_File:Pointer_scan
- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://forum.cheatengine.org/viewtopic.php?t=572465
- https://forum.cheatengine.org/viewtopic.php?t=585266
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MemoryRecordUnit.pas
- https://learn.microsoft.com/windows-hardware/drivers/debugger/x64-architecture
