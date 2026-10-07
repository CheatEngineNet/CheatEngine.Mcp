# Pointers and pointer scans

A heap or stack address moves when the game restarts or reloads a level. A pointer chain finds it again from a static
root inside a module. Read this page once you have the value's current address (see [value-scans](value-scans.md)) and
before you save it in a table. It covers notation, reading a chain, the manual method from a writer, the pointer
scanner, ranking and storing a chain, and what to do when the scanner fails.

Reading memory, capturing maps and searching paths do not change the target. Attaching the debugger, pausing the
process, writing through a record and injecting code do: explain each one and get the user's consent first. Use them
only on single-player or offline software the user owns or may modify, never on online, competitive or
anti-cheat-protected games (see [safety](safety.md)).

## Notation and offset order

- Cheat Engine writes a chain as `[[["game.exe"+1A2B30]+10]+18]+4C8`. Read the pointer at `game.exe+1A2B30` and add
  `10`; read the pointer there and add `18`; read again and add `4C8`: that sum is the value's address. Every number
  is hex. Each bracket pair is one read; the last offset is added, not read.
- Quote a module name that contains `-`, `+`, `*`, brackets or parentheses, because CE splits on them:
  `"Tutorial-x86_64.exe"+346CA0`. For the full grammar and the typecasts, see
  [address expressions](address-expressions.md).
- A root inside a module image (exe or DLL) is static. Store it as module+offset, because ASLR can load a module at a
  different base after a restart. Heap and stack addresses are never static roots.
- Offsets are signed. A negative offset means the stored pointer points past the field it leads to.

The table shows one chain in each place you meet it: step 8 of the x64 tutorial installed with CE 7.7,
`[[[["Tutorial-x86_64.exe"+346CA0]+10]+18]+0]+18` (the root offset can differ in another build).

| Where | Order | The offsets |
|---|---|---|
| CE address expression | dereference order, left to right | `+10`, `+18`, `+0`, `+18` |
| `offsets` of the pointer and record tools | dereference order, signed hex strings | `["10","18","0","18"]` |
| CE pointer scanner columns Offset 0..N | dereference order | 10, 18, 0, 18 |
| CE Add/Change address dialog | the box just above the base first, the top box last | top to bottom: 18, 0, 18, 10 |
| `.CT` `<Offsets>`, Lua `memrec.Offset[0]` | reversed: the first entry is applied last | 18, 0, 18, 10 |

- The tools take and return the same strings: pass the `offsets` of pointer_list_paths, pointer_read_chain and record
  reads to the record tools unchanged. Reverse them only when you copy them from CE's dialog or from a `.CT` file.
- Write a negative offset as `"-1C"`, never `"FFFFFFE4"` (refused); a bare `"1224"` is hex.
- Mono and .NET field offsets are decimal integers: the `hex` of `util_calculate(expression="1224")` is 4C8, the form a
  chain takes.
- Pointer width follows the target: 8 bytes in a 64-bit process, 4 bytes in a 32-bit one (WOW64 included). The pointer
  tools always take it from the target's bitness. `process_get_current()` reports `pointerSize`, the width Cheat
  Engine itself uses for records and for brackets in expressions; it normally equals the target's.
  `process_set_pointer_size(pointerSize=4)` exists for the rare 64-bit process that keeps 32-bit pointers; change it
  only when you know the target does that. The pointer tools still follow 8-byte pointers there, so check such a
  chain with a record or an expression instead.

## Read and check a chain

- `pointer_read_chain(base="game.exe+1A2B30", offsets=["10","18","4C8"], valueType="int32")` follows 1 to 64
  offsets. It returns every hop (`readAt`, `pointer`, `offset`, `next`), the final `address`, the `expression` and the
  `value`, read little-endian (for a big-endian value, use memory_read with `address`).
- An unreadable hop fails with `memory_read_failed`, and the error details name `hopIndex` and `readAt`. This usually
  means the object does not exist yet (menu, loading screen). Retry in the right game state; do not rewrite the chain.
- Classify the hops with `memory_get_address_info(addresses=["<readAt>", "<pointer>"], includeRtti=true)`. A `readAt`
  with a `module` is static, and `rttiClass` on a `pointer` names the class of the object it leads to, for MSVC C++
  objects with a vtable (see [structures](structures.md)).
- `symbol_register(name="playerBase", address="[game.exe+1A2B30]+10")` resolves the expression once, now: a fixed
  address for this session, not a live chain. After the object moves, `symbol_unregister(name="playerBase")` and
  register it again (a name in use is refused). A record with offsets follows its chain on every read.

## Analyze supplied access facts

`pointer_get_access_info` takes instruction text/address/length, x86/x64 `architecture`, `registers`, and optional `symbols` and `observedAccessAddress`.
Text is authoritative; optional `instructionBytes` checks length/address-size prefixes without decoding text.
RIP/EIP use the next instruction's address; EIP wraps to 32 bits.
No live memory or symbol lookup occurs.
Use `contextPhase="post_execution"` for data hits and an execute capture when registers may have changed.
Check `status`, `contextMayHaveChanged`, `observedAddressMismatch`, and `uncertainty` before using `candidateStructureBase` or `nextPointerSearchValue`.
`dynamicOffset=true` identifies indexed access rather than a stable chain offset.
Stack-relative POP, BT/BTC/BTR/BTS, and prefixed text are unsupported; bracket arithmetic is insufficient.

## Validate several supplied chains

`pointer_read_chains` accepts `{id,base,offsets}` candidates, optional `target` comparison and `valueType`, without changing scans.
Bounds: 128 candidates, 64 offsets each, 4,096 pointer/value reads, 65,536 final-value bytes; each dispatch allows 32 candidates and 128 reads.
`chainStatus`, `comparisonStatus`, and `valueStatus` are independent: failed value reads retain the resolved address and comparison.
`matchesOnly=true` retains all error/miss counts in `summary`.
Cancellation preserves completed candidates; submitted minus processed is unprocessed, and `cancelled` reports interruption.
`target_changed` discards mixed-target results.
Module roots rebase; absolute roots remain absolute, so revalidate after restarts.

## Manual method: from a writer back to a static root

Use it when you can make the game change the value. It needs the debugger (see [debugger](debugger.md)); the guided
version is the [manual pointer chain](../Workflows/manual-pointer-chain.md) workflow.

1. With consent, run `debugger_attach(interface="windows")`, then
   `debugger_start_capture(address="<value address>", trigger="write", size=4)`, with the size of the value (the
   address must be aligned to it). Make the game change the value, then page the hits with
   `debugger_poll_capture(jobId="<jobId>")`. The `context` of a hit holds `instructionAddress`, `disassembly` and
   `registers`.
2. Read the operand. For `mov [rbx+000004C8],eax` the offset is `4C8`, and the object base is the value's address
   minus 4C8: the `hex` of `util_calculate(expression="0x<value address> - 0x4C8")`. It should equal `RBX` in
   `registers`.
3. Find who holds the base: `pointer_find_references(target="<base>", maxOffset=0)`. Each reference gives the holder's
   `address`, the pointer `value`, the `offset` to add after the read and, for a holder inside a module image, its
   `symbol`.
4. A holder with a `symbol` such as `game.exe+1A2B30` is a static root. The chain is `[game.exe+1A2B30]+4C8`: the
   symbol is the base and `["4C8"]` the offsets. With a larger `maxOffset` a holder may point below the base; add its
   `offset` to the field offset (an `offset` of 10 gives `[game.exe+1A2B30]+4D8`).
5. If every holder is on the heap, go up one level.
   `debugger_start_capture(address="<holder>", trigger="access", size=8, aggregateByInstruction=true)` shows the code
   that reads the holder (size 4 on a 32-bit target). An instruction such as `mov rbx,[rsi+18]` gives offset `18` and a
   new base in `RSI`: search for that base as in step 3. Each level adds an offset at the front, so
   `[[game.exe+Y]+18]+4C8` has the offsets `["18","4C8"]`.
6. Verify with `pointer_read_chain`. Ask the user to restart the game, find the value again and verify once more. Stop
   every capture with `runtime_stop_job(jobId="<jobId>")` and, when you no longer need the debugger, run
   `debugger_detach()`; it refuses with busy while anything this server tracks still holds target state.

Without a debugger, `pointer_find_references(target="<value address>", maxOffset=4096, module="game.exe")` lists the
pointers inside `game.exe` that point at most 4096 bytes below the value; each `offset` is a candidate field offset.
This finds a chain only when the module points straight at the object, and it is noisy: pointers to neighbouring
objects match too. The live search blocks Cheat Engine while it scans; with `pointer_find_references.mapName` it
searches a stored map instead, nearest first. On a 32-bit target a live search with a non-zero `maxOffset` is refused
for targets above 7FFFFFFF; search a map there.

Pitfalls:

- Data breakpoints are traps. A write or access hit is reported after the instruction ran: `ip` is the next
  instruction, `registers` hold the values after it, and `instructionAddress` is decoded backwards (`isHeuristic`
  true). After `mov rax,[rax+10]` the base register is already overwritten. Read the instructions before it with
  `code_disassemble(address="<instructionAddress>", before=8)`, or capture the instruction itself with
  `debugger_start_capture(address="<instructionAddress>", trigger="execute")`: an execute breakpoint fires before the
  instruction runs, so its registers still hold the base.
- A data capture fires only for your address, but an execute capture fires for every object that runs the code
  (player and enemies).
  `debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true)` lists
  each accessed address as `effectiveAddress` with its `hitCount`; each object's base is that address minus the
  displacement. See the [shared code filter](../Workflows/shared-code-filter.md) workflow.
- After `lea rcx,[rbx+20]` and `mov [rcx+8],eax`, RCX points inside the object and no holder stores it. Walk back to
  RBX, or search below RCX: `pointer_find_references(target="<RCX>", maxOffset=256)` also finds the holders of RBX,
  with `offset` 20, which you add to the field offset.
- `[rax+rcx*4+10]` indexes an array. The chain then holds for that element only; you need the array base and the index
  logic (see [find an entity list](../Workflows/find-entity-list.md)).
- On x64 Windows the first integer argument, and `this` for a C++ member function, arrives in RCX. An execute capture
  at the function entry gives the object base directly.
- Code that touches the value only while a level loads does not fire during play: capture while the game loads.

## Pointer scanner: maps, path searches and rescans

The scanner finds chains without the debugger, like CE's pointer scan, but runs in this plugin's memory. The guided
version is the [pointer scan](../Workflows/pointer-scan.md) workflow.

1. **Map.** `pointer_create_map(mapName="run1", maxBytes=536870912, maxPointers=4194304, lifetimeSeconds=300)` starts
   a job that records every aligned, pointer-sized value that points into committed, readable memory. It returns a
   `jobId` and `state` running. There is no poll tool: read `pointer_list_maps()`, or the live resource
   `cheatengine://instance/pointer-maps`, until `state` is ready; `progressPercent` shows how far the capture is.
2. **Paths.** `pointer_find_paths(scanName="hp", mapName="run1", target="<value address>")` searches the map for chains
   from module roots to the target, off Cheat Engine's main thread. Pass the address from the same run as the map, in
   hex: a symbol is resolved in the process selected now. Read `pointer_list_scans()` or
   `cheatengine://instance/pointer-scans` until `state` is ready, then check `count`, `incomplete`, `traversalLimited`
   and `visitedNodes`.
3. **Page.** `pointer_list_paths(scanName="hp", sortBy="depth", moduleContains="game.exe", limit=50)` returns
   `expression`, `module`, `moduleOffset`, `base`, `offsets` (dereference order) and `verification`; pass `nextOffset`
   as pointer_list_paths.offset for the next page. `cheatengine://instance/pointer-scans/hp/paths` serves the same
   page, fewest levels first.
4. **Restart.** Ask the user to exit the game completely and start it again; back at the main menu the game may keep
   the same objects. `process_attach` is refused with busy while this server still holds state in the old process:
   running jobs (pointer captures and searches, debugger captures) and resources such as registered symbols, named
   scanners or MCP's pause. Check `runtime_list_resources()`: let the pointer jobs finish and release the rest with
   their own tools (`runtime_stop_job`, `symbol_unregister`, `process_set_paused(paused=false)`), or, if the user
   agrees to release everything, `runtime_release_resources()`. Then `process_attach(process="game.exe")`. Maps and
   scans survive the switch. Find the value again.
5. **Rescan.** `pointer_rescan_paths(scanName="hp", target="<new address>")` follows every path in live memory, 256 per
   dispatch, and keeps those that still reach the target. Check the new address first: a wrong one removes the good
   paths, and there is no undo. To check against a snapshot instead, capture `pointer_create_map(mapName="run2")` in
   the new run, then `pointer_rescan_paths(scanName="hp", target="<new address>", mapName="run2")`. Module roots rebase
   by module name; absolute roots stay absolute. The result gives `count`, `verified`, `removed`, `unresolved` and
   `incomplete`.
6. Repeat steps 4 and 5 across two or three restarts and different game states (another level, after a death, after
   loading a save) until `count` stops changing.
7. Rank the survivors (see "Ranking chains" below), confirm the best with `pointer_read_chain`, and store it in a
   record. Then free the memory with `pointer_delete_scan(scanName="hp")` and `pointer_delete_map(mapName="run1")`. A
   scan keeps its paths after its map is deleted.

### Save, restart and reopen

Maps and scans normally live only in the plugin activation's memory. Save the finished data before disabling the plugin,
closing CE or changing machines; an ordinary game restart does not require this because the activation keeps its maps
and scans across `process_attach`.

1. Configure a dedicated existing folder in `Mcp:Files:AllowedRoots`, for example
   `C:\CheatEngine\Files`. Until it is configured and the plugin is disabled and enabled again, both save tools refuse
   to write. See [configuration](configuration.md#host-files-and-tables).
2. When the map and scan are usable (`ready`, `stopped` or `expired`), call
   `pointer_save_map(mapName="run1", path="C:\CheatEngine\Files\run1.scandata")` and
   `pointer_save_scan(scanName="hp", path="C:\CheatEngine\Files\hp.json")`. The destination must be inside that
   root. A save does not replace a file unless `overwrite=true`, and it commits the new file atomically.
3. After the new activation starts, call `pointer_load_map` and `pointer_load_scan` with unused map and scan names.
   Loading a map lets the server search or rescan against that snapshot without reading the target. Loading a scan
   restores paths as `unresolved`; it never claims that a stored result is still valid.
4. Attach the restarted game, find the value again, then call
   `pointer_rescan_paths(scanName="<loaded scan>", target="<new address>")`. For an offline check, give it the loaded
   or newly captured map with `mapName`; for a live check, omit `mapName` and use a target of the same pointer width.
   Module roots rebase by module name. Confirm surviving paths live before placing one in a record.

`pointer_save_map` and `pointer_load_map` exchange Cheat Engine's native version-1 `.scandata` pointer-map format.
They do not save or resume CE's native scanner queue, and they do not use `.PTR` files. Native maps do not contain the
source process identity or capture statistics; an imported map has unknown completeness, so an empty search is never
proof that no path exists.

`pointer_save_scan` writes MCP's version-2 JSON result format. `pointer_load_scan` accepts version 2 and legacy version
1 JSON, and writes made again by this server use version 2. The JSON holds bounded result paths, their module-relative
roots and whether the original search may have missed paths; it is not a Cheat Engine `.PTR` file. Its loaded paths must
be rescanned after a restart because memory values and module layouts can have changed. A successful rescan clears the
temporary uncertainty from imported paths, while any uncertainty from the original search remains.

### Job states and result flags

A map or scan `state` is running, ready, stopped, expired, failed, target_changed or cancelled (the plugin
activation ended). The data is usable when it is ready, stopped or expired; stopped and expired keep what was read
or found so far. If the selected process changes during a capture, the map is discarded with target_changed.

| Where | Field | Meaning | Action |
|---|---|---|---|
| map | `incomplete` | skipped memory, or hit `maxBytes`, `maxPointers` or its lifetime | see the limits below |
| map | `unreadableBytes` above 0 | some planned bytes could not be read | usually harmless; the map is incomplete |
| scan | `traversalLimited` | the search hit `maxNodes` or `maxResults` | raise that limit, or lower depth or offset |
| scan | `incomplete` | incomplete map, a limit, a stop, or unresolved paths | an absence proves nothing |
| path | `verification` snapshot_match | reached the target through a map (every new path) | confirm live before use |
| path | `verification` live_match | reached the target in live memory | the strongest evidence |
| path | `verification` unresolved | a hop could not be followed | not verified; rescan when the object exists |
| rescan | `removed` | now reaches another address, or was unresolved and dropped | expected: a false path |

- `incomplete` or `traversalLimited` with zero paths is not proof that no chain exists.
- Unresolved paths are kept unless you rescan with
  `pointer_rescan_paths(scanName="hp", target="<address>", dropUnresolved=true)`. Report them as unverified, never as
  matches. A failed rescan keeps the original paths.

### Limits, defaults and tuning

| Setting | This server | CE's pointer scan dialog (defaults) |
|---|---|---|
| Levels | `maxDepth`: 5 by default, 1 to 8 | Max level 7 |
| Largest offset | `maxOffset`: 4096 by default, 0 to 1048576 | Maximum offset value 4095 |
| Branching | every holder is visited, up to `maxNodes` and `maxResults` | Max different offsets per node: 3 |
| Visited holders | `maxNodes`: 1000000 by default, up to 10000000 | - |
| Paths kept | `maxResults`: 1000 by default, up to 100000 | - |
| Roots | module images, system DLLs included (`staticRootsOnly` on) | modules except system DLLs; 2 thread stacks |
| Negative offsets | off unless `allowNegativeOffsets` | off |
| Loops | a path never visits the same holder twice | No looping pointers: on |
| Storage | activation memory, or explicit `.scandata` map / JSON scan saves | .PTR and .scandata files on disk |

- By default a capture reads writable memory only, at 4-byte-aligned addresses, up to `maxBytes` (64 MiB; at most
  512 MiB) and `maxPointers` (1048576 by default, up to 4194304), within a job lifetime of 120 seconds (up to 300).
  It reads upward, so a limit cuts the highest addresses first, where a 64-bit process usually keeps its module
  images, the roots: such a map often gives zero paths, hence the maximum in step 1. If `pointers` still reaches
  `maxPointers`, capture again with `pointer_create_map.alignment` 8 on x64, which keeps fewer pointers; if
  `bytesRead` reaches `maxBytes` or the map expired, use the manual method or the fallbacks below.
- The store keeps at most 4 maps holding 8388608 pointers in total and 16 scans holding 1000000 paths in total. A
  running capture reserves its whole `maxPointers` and a running search its `maxResults`; once the job ends, only what
  it kept counts. Each stored pointer costs about 20 bytes of Cheat Engine's memory. A name in use fails with
  `invalid_state`; a full store fails with `limit_exceeded`. A new job fails with busy while the activation holds
  its maximum of jobs (16 by default; finished jobs count until their lifetime ends).
- Start with the default depth and offset. With no paths and nothing limited, raise `maxOffset` first (8192, then
  16384), then `maxDepth` by one. Every extra level, a larger offset and negative offsets multiply the work. Games on
  large engines such as Unity and Unreal often need both; prefer their runtime roots
  (see [game engines](game-engines.md)).
- Values move while a large map is read. With consent, run `process_set_paused(paused=true)` during the capture and
  always `process_set_paused(paused=false)` afterwards: MCP's pause is a tracked resource that blocks `process_attach`
  and `debugger_detach` until you resume.
- Stop a job with `runtime_stop_job(jobId="<jobId>")`: a stopped capture or search keeps what it had.
  `pointer_delete_map` and `pointer_delete_scan` also stop a running job, then discard its data. Maps and scans survive
  a target switch but not a plugin disable unless saved and loaded with the pointer persistence tools.
- There are no thread-stack roots and no "must end with offsets" filter. A value reachable only from a thread stack
  needs another method, or CE's own pointer scanner. When a writer gave you the last offset, keep the paths whose last
  `offsets` entry matches it yourself.

## Ranking chains

Prefer, in this order:

1. A root in the game's own module (`game.exe`, `GameAssembly.dll`, `UnityPlayer.dll`) over a system DLL, whose layout
   changes with Windows updates: `pointer_list_paths(scanName="hp", moduleContains="game.exe")`.
2. Fewer levels, then small offsets that look like fields (multiples of 4 or 8, below hex 1000):
   `pointer_list_paths(scanName="hp", sortBy="offset_sum")`.
3. A live_match `verification` after every restart and game state you tried.
4. Hops that land on recognisable objects: check them with `memory_get_address_info(addresses=[...], includeRtti=true)`,
   or confirm with an access capture that the game really reads a hop.

Many chains reaching the same value is normal. You need one reliable chain; keep one or two alternates in case a game
update breaks it.

## Store a chain in a record

- Create it with
  `record_create(records=[{description="Health", address="game.exe+1A2B30", variableType=2, offsets=["10","18","4C8"]}])`.
  The address is the root and the offsets are the pointer tools' strings, at most 128. `variableType` is the type of
  the final value: 2 for a 4-byte integer, 4 for a float (see [value types](value-types.md)). A chain is always
  `offsets`, never a type: 12 is refused, and a pointer shown as a number is 3 on x64 or 2 on x86.
- Without a `value` nothing is written. A value is written at the final address after the offsets are set: that
  changes the game, so ask first.
- Check the result's `currentAddress`, which must be the value's address, `offsetCount` and `offsets`. `record_get`,
  `record_list` and `record_find` return `offsets` in the same form; an offset a loaded table keeps as a symbol or Lua
  text is copied as that text, never evaluated.
- Change a chain with `record_update(updates=[{id=<id>, offsets=["10","18","4D0"]}])`, and remove it with an empty
  list. Cheat Engine clears the offsets when the address changes, so send both together:
  `record_update(updates=[{id=<id>, address="game.exe+1A2B40", offsets=["10","18","4C8"]}])`. An Auto Assembler
  record cannot take offsets.
- After a game update one offset often shifts (4C8 becomes 4D0): read the record's `offsets`, test the change with
  `pointer_read_chain`, then update the record (see the [repair after update](../Workflows/repair-after-update.md)
  workflow).
- Prefer offsets to a bracket expression as the address: CE re-evaluates address text only now and then (see
  [address expressions](address-expressions.md)).
- A saved `.CT` stores the offsets reversed; Cheat Engine and the record tools convert. See
  [cheat tables](cheat-tables.md) and the [robust table](../Workflows/build-robust-table.md) workflow.
- During loading a chain may reach a freed or reused object for a moment. Check `currentAddress`, or the object,
  before you freeze a record.

## When the scanner fails

- **Injection copy base.** Find an instruction that touches only your object, and inject code that stores its base
  register in a registered symbol (`globalalloc(playerBase,8)`, then `mov [playerBase],rbx`). Records then use
  that symbol as the root:
  `record_create(records=[{description="Health", address="playerBase", variableType=2, offsets=["4C8"]}])`.
  The symbol holds 0 until the code runs, and a stale object after a reload until it runs again. This needs
  `Mcp:EnableAutoAssembler` and consent. See the [injection copy base](../Workflows/injection-copy-base.md) workflow,
  [auto-assembler](auto-assembler.md) and [x64 injection](x64-injection.md).
- **RIP-relative globals.** x64 code reads a global with `mov rax,[rip+disp32]`. The tools print the resolved target
  as absolute hex, never as a module name: `mov rax,[7FF6A1DF1A30]`. Read the code before the writer with
  `code_disassemble(address="<instructionAddress>", before=40)` and look for the load of the base register from such
  an address. `memory_get_address_info(addresses=["7FF6A1DF1A30"])` names it, such as `game.exe+2F1A30`: a static
  root, so the chain is `[game.exe+2F1A30]+4C8`. Other code that uses the global contains the same hex:
  `code_start_search(address="<function start>", size=4096, textContains="7FF6A1DF1A30")`; poll it with
  `code_poll_job(jobId="<jobId>", afterSequence=0)` and stop it with `runtime_stop_job(jobId="<jobId>")`. See
  [code analysis](code-analysis.md). After a game update, find that instruction again with an AOB signature (see
  [aob-signatures](aob-signatures.md) and the [repair after update](../Workflows/repair-after-update.md) workflow).
- **Managed runtimes.** With the Mono collector attached (`mono_attach()` injects it, so ask first),
  `mono_get_object(address="<value address>")` walks back from the value to its object and returns its class, its
  start `address` and `offsetInObject`, the last offset of the chain (decimal). A static field's address is its
  `staticAddress` in `mono_list_fields`, or `mono_get_static_field_address(classHandle="<class>")` plus the field's
  offset. .NET statics come from `dotnet_get_type`. IL2CPP statics are reached through class data that
  `GameAssembly.dll` references, and Unreal games have global roots such as GWorld. See
  [mono and .NET](mono-and-dotnet.md), [Unity IL2CPP](unity-il2cpp.md) and [Unreal Engine](unreal-engine.md).

## Sources

- https://wiki.cheatengine.org/index.php?title=Tutorials:Pointers
- https://wiki.cheatengine.org/index.php?title=Help_File:Pointer_scan
- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://wiki.cheatengine.org/index.php?title=Help_File:Find_out_what_writes/accesses_this_address
- https://forum.cheatengine.org/viewtopic.php?t=602561 (pointer maps and rescans)
- https://forum.cheatengine.org/viewtopic.php?t=576114 (full restarts, a settling count)
- https://forum.cheatengine.org/viewtopic.php?t=591613 (adjusted base registers)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/PointerscannerSettingsFrm.lfm
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/pointerscannerfrm.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MemoryRecordUnit.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
- https://learn.microsoft.com/cpp/build/reference/dynamicbase-use-address-space-layout-randomization
- https://learn.microsoft.com/windows-hardware/drivers/debugger/x64-architecture
- Intel 64 and IA-32 Architectures Software Developer's Manual, Vol. 3, debug exceptions (instruction breakpoints are
  faults, data breakpoints are traps).
