# When a scan finds nothing

Read this when a value scan ends with no rows, keeps too many rows, or finds an address that does not behave. Each
entry names the likely cause and the fix with this server's tools. The scan loop is in [value scans](value-scans.md);
how games store numbers is in [value types](value-types.md).

Scans only read the target, but several fixes pause it, attach the debugger, inject Mono's collector or write memory:
explain each one and get the user's consent first. Work only on single-player or offline software the user owns or may
modify (see [safety](safety.md)).

## Check these first

1. The process. `process_get_current()` names the target. A launcher, Unreal's small root `<Project>.exe` (the game is
   `*-Shipping.exe`) or the wrong Chromium, Electron or NW.js process holds no game state; find the real one with
   `process_list(nameContains="<game>")`. Browser-based games run in a renderer process, often the one using the most
   memory in Task Manager.
2. The scanner. `scan_get_status(scannerName="main")`: `NoTarget` means no process is open, `Failed` carries Cheat
   Engine's message in `scan_get_status.error`, and `BaselineReady` has no rows until a `scan_next`. For main it also
   returns `scan_get_status.settings`, the Cheat Engine options its scans use (see the settings section below).
3. One change per step, reported in the right direction. A wrong value or direction, or a restart or level load between
   two scans, drops the real address: start again.
4. An unusual value. 0, 1 and 100 match everywhere; have the user reach something like 137 first.

## No results: causes and fixes

| Cause | Fix |
|---|---|
| Wrong type | `int32`, then `float` with `between`, then `double`, `int16`, `int64`, `byte`. A whole number from 2,147,483,648 to 4,294,967,295 may be a uint32 (see "Unsigned above the signed maximum"); a larger one is `int64` or `double`. |
| Exact float, rounded display | `exact` writes the value with `scan_first.floatDecimals` decimals (default 6 for float, 12 for double) and matches at about that precision, so an exact 83 misses 82.67. Use `between` 82.5 and 83.5 (83 and 84 when the display truncates). |
| Unsigned above the signed maximum | Only `byte` is unsigned: `int32` refuses 3000000000 with `invalid_argument`, and `int64` finds it only where the next 4 bytes are zero. Scan the signed equivalent, `util_convert_value.value` of `util_convert_value(value="3000000000", sourceType="uint32", targetType="int32")` (-1294967296). |
| Negative values, ordered comparisons | Cheat Engine compares integers unsigned for `greater`, `less`, `increased` and `decreased`: 0 to -1 counts as increased, and `less` than 10 drops negatives. `between` compares signed when a bound is negative: use it, or `changed`. |
| Scaled storage | 12.5 may be stored as 125 or 1250, money as cents, 75 % as 0.745 to 0.755. Scan each guess, or scan `unknown`, then `increased` or `decreased`, which work at any scale. |
| Engine encoding | Identify the engine ([engine triage](../Workflows/engine-triage.md)). RPG Maker XP/VX/Ace: `int32` 2n+1. Electron, NW.js, RPG Maker MV/MZ: `double`, then `int32` 2n (older 64-bit V8: n). Flash: `int32` `between` 8n and 8n+7. GameMaker: `double`. Godot: `int64` or `double`. See [value types](value-types.md#tagged-and-engine-specific-numbers). |
| Encoded (XOR key, offset, second copy) | `unknown`, then only `changed` and `unchanged`: encoding scrambles order. The writer's registers hold the plain value ([find what writes](../Workflows/find-writer.md)). Encryption from an anti-cheat or anti-tamper library means stop. |
| Computed on demand | Attack = base + gear + buff has no stored copy: scan the inputs. |
| Changes during the scan | With consent, `process_set_paused(paused=true)` before each scan, poll until it ends, then `process_set_paused(paused=false)`. MCP tracks its own pause (`process_set_paused.resourceId`), which blocks a target change until resumed. Or scan `unknown`, then `changed` and `unchanged`. |
| Emulator or mapped memory | Skipped by default: see "Emulators and mapped memory" below. |
| Main's inherited settings | A range, protection, fast-scan, rounding or region-kind choice in Cheat Engine excludes it: read `scan_get_status.settings` (next section). |
| Named scan scoped wrongly | `scan_first.endAddress` is exclusive. `scan_first.writable` set to `excluded`, a `scan_first.alignment` the field's address is not a multiple of, or a wrong `scan_first.lastDigits` hides it. |
| Online, server-side or anti-cheat protected | Out of scope: stop and tell the user. |

## Settings main inherits from Cheat Engine

On main, `scan_first` sets the value type (UTF-16 only for `wstring`). `scan_first` and `scan_next` both set the
comparison, the value and the Hex box, ticked only for `bytes` so that MCP's decimal values are never read as hex, and
untick Lua formula, Not, Repeat, Percent and Compare to first/saved scan. `scan_next` keeps the type of the scan it
narrows. Everything else is the user's Cheat Engine setting, which no dedicated tool changes.
`scan_get_status(scannerName="main")` reports it read-only in `scan_get_status.settings`, under the field names below;
an override that Lua set with `setSpecialScanOptionsOverride` is not shown.

| Setting (field) | Starts as (Cheat Engine source) | Misses the value when |
|---|---|---|
| Start and Stop (`startAddress`, `stopAddress`) | the whole user address space | the range was narrowed |
| Writable, Executable, CopyOnWrite (`writable`, `executable`, `copyOnWrite`) | Writable ticked (`required`), Executable grey (`any`), CopyOnWrite unticked (`excluded`) | Writable is unticked, or Executable or CopyOnWrite is ticked |
| Fast Scan (`fastScan`, then `alignment` or `lastDigits`) | on, aligned to the value type's size (1, 2 or 4), which `scan_first` sets again unless the user edited it | the field is packed or misaligned |
| Rounding (`rounding`), float and double | the last choice; Rounded (extreme) on a fresh install | the match needs a wider tolerance: use `between` |
| Simple values only (`simpleValuesOnly`), float and double | off | ticked: nonzero values outside about 0.001 to 2048 drop |
| Active memory only (`activeMemoryOnly`) | off | ticked: memory outside the working set is skipped |
| Pause the game while scanning (`pauseWhileScanning`) | off | the value moves mid-scan |
| Case sensitive (`caseSensitive`), text | on | the text's case differs |
| Codepage (`codePage`), text | off | ticked: a `string` scan encodes the text in the system code page, not UTF-8 |
| MEM_PRIVATE, MEM_IMAGE, MEM_MAPPED (`memPrivate`, `memImage`, `memMapped`; Settings, Scan Settings) | Private and Image ticked, Mapped unticked | the value's region kind is unticked |

Ask the user to change a setting in Cheat Engine's window, or use a named scanner. `scan_first` sets a named scan's
range, protection and alignment explicitly ([scoping a named scan](value-scans.md#scoping-a-named-scan)), and its float
scans use Rounded (default) whatever main shows. The three MEM_* boxes apply to named scans and AOB searches too.

## Emulators and mapped memory

Emulators usually keep guest RAM in a mapped region (Cemu 2.x uses private memory: see [emulators](emulators.md)). Per
Cheat Engine's source, the MEM_MAPPED setting filters every scan it runs, main and named scans and the byte-pattern
searches of `aob_find` and `aob_find_value`, even inside an explicit range. Reads and snapshots are not filtered.

1. Find the RAM: `memory_list_regions(type="mapped", format="detailed", limit=2000)`, the region sized like the
   console's RAM (see [emulators](emulators.md), [emulator memory](../Workflows/emulator-memory.md)).
2. Scan it with a named scanner that includes mapped memory:
   `scan_first(scannerName="emu", valueType="int32", value="100", startAddress="<guest base>", endAddress="<guest end>", includeMapped=true)`.
   `scan_next(scannerName="emu", value="95")` keeps the regions of that first scan. `aob_find` and `aob_find_value`
   take their own `includeMapped`, not with `module`.
   - The override is Cheat Engine-wide while the call runs, and its end also ends any `setSpecialScanOptionsOverride`
     that a table or `lua_execute` set.
   - A first scan that runs meanwhile with the other `includeMapped` choice is refused as `busy`: repeat it after the
     running one returns.
   - Main refuses `includeMapped` with `invalid_argument`. For main, ask the user to tick MEM_MAPPED under Edit,
     Settings, Scan Settings; `scan_get_status.settings` then shows `memMapped` true.
3. Or bypass the scanner: `memory_create_snapshot(name="ram-a", address="<guest base>", size=16777216)`, one change in
   game, a second snapshot `ram-b`, then
   `memory_compare_snapshot(name="ram-a", compareTo="ram-b", valueType="int32", change="changed")`. A snapshot holds at
   most 16 MiB, and at most 16 snapshots and 64 MiB exist together: split larger RAM, and free them with
   `memory_delete_snapshot(name="ram-a")`.

Big-endian guests (GameCube, Wii, Wii U, PS3):
`util_convert_value(value="100", sourceType="int32", targetType="bytes", byteOrder="big_endian")` returns
`util_convert_value.bytes` `00 00 00 64`. Search it with
`scan_first(scannerName="emu", valueType="bytes", value="00 00 00 64", startAddress="<base>", endAddress="<end>", includeMapped=true)`
and narrow with `scan_next(scannerName="emu", value="00 00 00 5A")`. Read a survivor as a number with
`memory_read(address="<hit>", valueType="int32", byteOrder="big_endian")`, or watch several with
`memory_read_samples(addresses=["<a1>", "<a2>"], valueType="int32", byteOrder="big_endian")`. For unknown values use
only `changed` and `unchanged`: `increased` and `decreased` compare little-endian, in scans and snapshot comparisons
alike ([value types](value-types.md#big-endian-data-in-emulators)).

## Too many results

- Narrow by the change: `scan_next(scannerName="main", comparison="decreasedBy", value="7")` (or `increasedBy`) is the
  most selective step. While the value stays still, run `scan_next(scannerName="main", comparison="unchanged")` two or
  three times.
- Bound it: floats `greater` than 0, or `between` 0 and 1 for a fraction.
- Scope it. On main, ask the user for a range or Writable only; `scan_get_status.settings` shows what main uses now. Or
  start a named scan with `scan_first.startAddress` and `scan_first.endAddress` (a module from `module_get`, heap
  regions from `memory_list_regions`), `scan_first.writable` set to `required` and `scan_first.alignment` set to 4;
  unscoped, it checks every address.
- Sample: `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="float", durationMs=3000)` watches up to 64
  survivors while the user acts; keep the series whose `changes` follow the action. Times count from the first read,
  and a tick skipped because Cheat Engine was busy is counted in `memory_read_samples.missedTicks`.
- Classify: `memory_get_address_info(addresses=["<a1>", "<a2>"])` gives `module`, `section`, `isSystemModule` and the
  region `type`. Skip system modules; a hit in the game's module image is static.
- Many rows moving together are copies (HUD, rendering, previous frame): test one at a time
  ([verify candidates](value-scans.md#verify-candidates)) or capture their writers.
- Still stuck: [compare snapshots](../Workflows/compare-snapshots.md) of the object, or
  [find code by string](../Workflows/find-code-by-string.md).

## The result does not behave

- **A write reverts, or only the HUD changes.** A display copy, or a value recomputed every frame. With consent,
  `debugger_attach(interface="windows")`, then
  `debugger_start_capture(address="<address>", trigger="write", aggregateByInstruction=true)`. Poll
  `debugger_poll_capture(jobId="<jobId>")` while the user changes the value, then `runtime_stop_job(jobId="<jobId>")`,
  which discards the hits. Keep the address that game logic writes (damage, purchase), not a UI refresh
  ([find what writes](../Workflows/find-writer.md)). When that instruction also writes other objects,
  `debugger_start_capture(address="<instructionAddress>", trigger="execute", groupByEffectiveAddress=true)` lists every
  address it touches ([shared code filter](../Workflows/shared-code-filter.md)). In interpreters and emulators the
  writer is shared VM code: stay on the data side. Detach with `debugger_detach()` when done; it refuses (`busy`)
  while MCP still holds a resource.
- **A freeze does not hold.** `record_set_active(ids=[<id>], active=true)` rewrites the value every 100 ms by default
  (Cheat Engine's Freeze interval setting); the game runs in between, so one large hit can still kill. A record left
  inactive carries `record_set_active.failure`; its reason `inaccessible` means Cheat Engine could not write the value.
  Freezing a display copy does nothing. With consent, stop the writer instead: [NOP patch](../Workflows/nop-patch.md)
  or [AOB injection](../Workflows/aob-injection.md).
- **Gone after a restart or level load.** Heap addresses move and old results mean nothing: rescan. For a lasting
  address, build a [pointer chain](pointers.md) ([pointer scan](../Workflows/pointer-scan.md)) or inject at the writer
  ([injection copy base](../Workflows/injection-copy-base.md)).
- **Right once, then other data.** The object was freed or moved and its memory reused: a new level, a pooled object,
  or a moving garbage collector (.NET, Java, JavaScript). Re-find the value, then reach it through its owner, a
  pointer chain or the engine's metadata ([game engines](game-engines.md)). In a Unity game, with consent,
  `mono_attach()` (target code execution gate), then `mono_get_object(address="<hit>")` names the object's class and
  lists its fields; `mono_get_object.offsetInObject` is the hit's field offset ([Mono and .NET](mono-and-dotnet.md)).

## Refusals

| Refusal | Cause | Do |
|---|---|---|
| `not_attached` on main | no process is open in Cheat Engine | `process_attach` |
| `busy` on main | a main scan runs, or its Repeat box is ticked | poll `scan_get_status`, or `scan_stop(scannerName="main")`, which unticks Repeat and asks a running scan to stop; its partial results need `scan_reset` and a new `scan_first` to be complete |
| `busy` on `scan_first` | a first scan with the other `includeMapped` choice is running | repeat after it returns |
| `invalid_state` on `scan_first` | the scanner already holds a scan | with consent, `scan_reset` (on main it runs New Scan and clears the user's list) |
| `invalid_state` on `scan_next` | nothing to narrow, a failed scan, a named scanner not in `ResultsReady`, or a main type MCP cannot narrow (Binary, All, Grouped, custom) | reset and rescan, or narrow in Cheat Engine |
| `invalid_state` on `scan_list_results` | no completed scan, or an unknown-value baseline without rows | `scan_next` first |
| `invalid_argument` on `value` or `upperValue` | not decimal (`0x`, separators), outside the type's range, empty text, NaN or Infinity | decimal text; the signed equivalent for large unsigned values |
| `invalid_argument` on a scope option | main refuses range, protection, alignment and `includeMapped` | use a named `scannerName` |
| `invalid_argument` on `comparison` | `string`, `wstring` and `bytes` take `exact` only | a numeric type, or `exact` |
| `unsupported` on `includeMapped` | this Cheat Engine has no `setSpecialScanOptionsOverride` | ask the user to tick MEM_MAPPED |
| `partial_effect` on `scan_first` (`host_refused` on `aob_find`, `aob_find_value`), `cleanup_unconfirmed` | the MEM_MAPPED override could not be removed | a completed `scan_first` keeps its results (`scanCompleted` in the details); run another `includeMapped` call, which ends the override, or ask the user to restart Cheat Engine |
| `not_found` | an unknown `scannerName` (case-sensitive, never falls back to main), or a range that does not resolve | `scan_list_scanners`; check the expressions |
| `limit_exceeded` on `scannerName` | 32 named sessions exist | `scan_delete` unused ones |
| named state `Scanning`, `Invalidated` or `Closed` | a cancelled call, a call that did not reach a safe state, a target or Lua runtime change, or a released session | `scan_reset` recovers `Invalidated` unless its target or Lua runtime changed; otherwise, and for the others, `scan_delete` (follow its hint on `partial_effect`) and scan under another name meanwhile |
| `busy` on `process_attach` | a running main scan, or an owned resource: a named session, MCP's pause, a patch | wait or `scan_stop`; find the holder with `runtime_list_resources` and release it (`scan_delete`, `process_set_paused(paused=false)`) |

Never repeat a scan whose reply was lost or whose `hostEffect` is `started` or `unknown`: read `scan_get_status` first
([errors and recovery](errors-and-recovery.md)).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Memory_Scanning
- https://wiki.cheatengine.org/index.php?title=Help_File:Memory_scan_settings
- https://github.com/cheat-engine/cheat-engine, folder `Cheat Engine`: `memscan.pas` (unsigned ordered comparisons,
  signed between, float rounding, Simple values only, the MEM_MAPPED region filter), `MainUnit.pas` and
  `MainUnit.lfm` (scan panel defaults and captions, rounding fallback, alignment reset on a type change, freeze timer),
  `formsettingsunit.lfm` (MEM_PRIVATE and MEM_IMAGE on, MEM_MAPPED off, Freeze interval 100), `simpleaobscanner.pas`
  (AOB searches use the same scanner)
- Cheat Engine 7.7 `celua.txt` (`setSpecialScanOptionsOverride`)
- https://v8.dev/blog/pointer-compression (Smi encoding)
- https://schnee.re/posts/encrypted-values/ (encoded values and changed or unchanged scans)
