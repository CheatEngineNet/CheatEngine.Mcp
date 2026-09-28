# Value scan strategy

Read this before the first `scan_*` call. It covers how to find where a game stores a number: which scanner to use, how
to start, narrow and read a scan, how floats and signed integers compare, and when a memory snapshot, a sampling call
or a one-shot byte search is the better tool. Types and encodings are in [value types](value-types.md). For scans that
find nothing, see [when a scan finds nothing](troubleshooting-scans.md). Terms are in the [glossary](glossary.md).

Scans only read the target, but the verification step writes to it. Work only on single-player or offline software
the user owns or may modify, never on online, competitive or anti-cheat-protected games, and ask before every write
(see [safety](safety.md)).

## Main and named scanners

Every `scan_*` tool takes `scannerName`. `main` (the default) is Cheat Engine's own scanner; any other name is an
independent session.

| | `main` (default) | named scanner (any other name) |
|---|---|---|
| What it is | Cheat Engine's visible scan tab | an independent session, invisible in the UI |
| Timing | returns at once; poll `scan_get_status` | synchronous: blocks Cheat Engine until the scan ends |
| Scope | Cheat Engine's scan panel settings | whole address space, unfiltered, unless `scan_first` scopes it |
| Lifetime | Cheat Engine's; MCP never deletes it | activation resource; blocks `process_attach` |
| How many | one | up to 32 per activation |
| `scan_stop` | asks a running scan to stop and unticks Repeat | releases the session, like `scan_delete` |
| `scan_reset` | runs Cheat Engine's New Scan | clears the results and keeps the session |

- Use main for the normal loop, and always for a whole-process unknown-value baseline. Cheat Engine scans in the
  background, the user sees the found list, and main also reads and narrows scans the user started by hand. Main's
  `scan_first` and `scan_next` set the Hex box themselves (ticked only for `bytes`), so MCP values stay decimal.
- Main holds the user's work. If `scan_get_status(scannerName="main")` shows results, ask before
  `scan_reset(scannerName="main")`, or use a named scanner instead.
- For main, `scan_get_status.settings` shows, read-only, the Cheat Engine settings its scans use, MEM_* region boxes
  included (these apply to named scans too). Check them when main finds nothing or far too much.
- Use a named scanner for a second hypothesis in parallel (float and int32 at once), for a scan that must not touch the
  user's found list, or for a scan bounded to one module or heap region. Always scope it ("Scoping a named scan"
  below). An unscoped named scan checks every address of the process, code included, and can hold Cheat Engine for
  seconds; an unknown-value baseline takes much longer.
- Names are case-sensitive, 1 to 256 characters; only the exact lowercase `main` is main (`Main` is a named scanner).
  `scan_first` creates a named session on first use. The other `scan_*` tools fail with `not_found` for an unknown
  name and never fall back to main. `scan_list_scanners` and the live resource `cheatengine://instance/scanners` list
  main and every named session.
- A named session is listed by `runtime_list_resources` and blocks `process_attach` to another process. Release it
  with `scan_delete(scannerName="hp")` when done; `runtime_release_resources` releases it with everything else at the
  end of a session (see [clean up a session](../Workflows/cleanup-session.md)).

## Start a scan

- `scan_first.valueType`: `byte` (unsigned, 0 to 255), `int16`, `int32` (default), `int64`, `float`, `double`,
  `string` (UTF-8), `wstring` (UTF-16) or `bytes`. There is no `pointer`, `int8` or wider unsigned scan type ("Signed
  and unsigned integers" below).
- `scan_first.value` is text. Integers are decimal, with no `0x` and no thousands separators. Floats use a `.` decimal
  point; a comma is read as a thousands separator, so `87,5` becomes 875. Bytes are hex pairs (`48 8B 05`); `??`
  wildcards are refused, so use `aob_find` for patterns (see [AOB signatures](aob-signatures.md)).

| `comparison` | Scan | Needs | Keeps |
|---|---|---|---|
| `exact` | first, next | `value` | equal values; floats at `floatDecimals` precision |
| `between` | first, next | `value`, `upperValue` | values in the inclusive range |
| `greater`, `less` | first, next | `value` | values strictly above or below; integers compare unsigned |
| `unknown` | first | nothing | every value, as a baseline without rows |
| `increased`, `decreased` | next | nothing | values above or below the previous scan's; integers unsigned |
| `increasedBy`, `decreasedBy` | next | `value` (the delta) | values that moved by the delta; floats: see below |
| `changed`, `unchanged` | next | nothing | values that differ from, or equal, the previous scan's |

- `string`, `wstring` and `bytes` scans take `exact` only. `upperValue` is accepted only by `between`. A comparison
  that needs no value refuses one.
- `scan_next` keeps the type of the first scan and always compares with the previous scan. MCP offers no "compare to
  first scan", percentage, Not or Lua-formula option: it unticks those Cheat Engine boxes, and Repeat, before every
  main scan.
- A named scanner, like main, refuses a second `scan_first` with `invalid_state` until `scan_reset` clears it.
- A refused argument fails with `invalid_argument` or `limit_exceeded` and `hostEffect` `not_started`.

### Scoping a named scan

These `scan_first` options apply to named scanners only. Main refuses them with `invalid_argument`, because it uses the
range, protection, fast-scan and region settings of Cheat Engine, which `scan_get_status.settings` shows.

- `scan_first.startAddress` and `scan_first.endAddress` are both required, or neither. They take address expressions
  (`game.exe`, `game.exe+2A0000`, `7FF6A1230000`). The end is exclusive and must resolve above the start. A match may
  begin slightly before the start. An expression that does not resolve fails with `not_found` and creates no session.
- `scan_first.writable`, `scan_first.executable` and `scan_first.copyOnWrite` each take `required`, `excluded` or
  `any` (default). `writable` set to `required` skips code and read-only data, which hold constants, not live state.
- `scan_first.alignment` (1 to 65536) keeps only addresses divisible by it; 4 matches Cheat Engine's fast scan for
  int32 and float. `scan_first.lastDigits` (1 to 16 hex digits) keeps addresses that end with those digits. Use one or
  neither.
- `scan_first.includeMapped` (default false) adds mapped memory, such as emulator RAM, to this one scan ("Fast scan and
  memory regions" below).

To get a range, read a module's base and `size` with `module_get(module="game.exe")`, or pick heap regions with
`memory_list_regions(writable="required", type="private")`. Expressions are hex, so write the size in hex. Example:

`scan_first(scannerName="hp", valueType="float", comparison="between", value="86.5", upperValue="87.5",
startAddress="7FF6A1230000", endAddress="7FF6A1230000+4000000", writable="required", alignment=4)`

## Poll, page and read

Main returns at once, usually in state `Scanning`: poll `scan_get_status(scannerName="main")` until `isScanning` is
false. A named scan has finished when its call returns. `resultsReady` is true, and `count` is present, only in
`ResultsReady`.

| `state` | Scanner | Meaning | Next step |
|---|---|---|---|
| `NoTarget` | main | no process is open | `process_attach` |
| `Created` | both | no scan yet (after New Scan or `scan_reset`) | `scan_first` |
| `Scanning` | both | main: still running. Named: a call cancelled mid-scan | poll main; `scan_delete` a named one |
| `BaselineReady` | main | unknown-value baseline, no rows yet | `scan_next` |
| `ResultsReady` | both | `count` rows | `scan_next` or `scan_list_results` |
| `Failed` | main | `error` holds Cheat Engine's message | `scan_reset`, then a corrected `scan_first` |
| `Invalidated` | named | a Cheat Engine call failed, or the target or Lua runtime changed | `scan_reset`; after a change, `scan_delete` |
| `Closed` | named | no longer usable: released or given up | `scan_delete`; the name is then free again |

- A named scanner has no `BaselineReady`: after an unknown first scan it reports `ResultsReady`. Narrow it with
  `scan_next` before reading rows.
- A start is not a result. If a `scan_first` or `scan_next` call times out or its reply is lost, do not repeat it:
  read `scan_get_status` first. Never repeat a call whose `hostEffect` is `started` or `unknown`.
- Main refuses with `busy` (retryable) while it scans, `not_attached` without a process, and `invalid_state` when
  `scan_first` finds a scan already there (reset first), when there is nothing to narrow, after a failure, or when
  `scan_list_results` meets an unknown-value baseline. A running main scan also makes `process_attach` fail with
  `busy`: wait, or stop it.
- `scan_stop(scannerName="main")` unticks Repeat and asks a running scan to stop, as Cheat Engine's Cancel button does,
  without waiting. `scan_get_status` reads `Scanning` until Cheat Engine has ended the scan, then `ResultsReady` or
  `BaselineReady` with only what it found before the stop: run `scan_reset` and `scan_first` again for complete
  results. `scan_stop.stopRequested` is false when main was idle.

Page results with `scan_list_results(scannerName="main", startIndex=0, maximumResults=100)`:

- Each row of `scan_list_results.results` has `address` (uppercase hex without `0x`) and `value`, which is Cheat
  Engine's display text. Re-read a row with `memory_read` for a typed value.
- `scan_list_results.maximumResults` is 1 to 1024 (default 1000). Continue with `scan_list_results.nextStartIndex`
  while `hasMore` is true. A start at or past the end returns an empty page.
- Narrow before paging: aim for about 10 to 50 rows. Thousands of rows are not worth reading.

## Known value

1. `process_get_current()` confirms the target. `scan_get_status(scannerName="main")` must show `Created`, or reset
   main with consent.
2. `scan_first(scannerName="main", valueType="int32", comparison="exact", value="1250")`, then poll.
3. Ask the user to change the value once and report the new number, then
   `scan_next(scannerName="main", comparison="exact", value="1180")`. When only the change is known, use
   `scan_next(scannerName="main", comparison="decreasedBy", value="70")`. Poll.
4. Repeat step 3 until about 10 rows remain. When many rows keep the same value, have the user wait and run
   `scan_next(scannerName="main", comparison="unchanged")`. Then verify the rows ("Verify candidates" below).

- Nothing found: try `float` with `between` (below), then `double`, `int16`, `int64` and `byte`. A whole number from
  2,147,483,648 to 4,294,967,295 may be an unsigned 32-bit value: scan its signed `int32` equivalent (below). Larger
  numbers are `int64` or `double`. A shown 12.5 may be stored as 125 or 1250 (see [value types](value-types.md)).
- Common values such as 0, 1 and 100 match everywhere. Have the user make the value unusual before the first scan.

## Unknown value

For bars, gauges, hidden stats and timers:

1. `scan_first(scannerName="main", valueType="float", comparison="unknown")` (int32 for counters), then poll until
   `BaselineReady`.
2. After each in-game change, `scan_next(scannerName="main", comparison="decreased")`, or `increased`. Use `changed`
   when the direction is unclear, and for encoded values whose order is scrambled.
3. While the value stays still, run `scan_next(scannerName="main", comparison="unchanged")` two or three times to drop
   timers, animation and camera values.
4. Add bounds when you can: floats `greater` than 0, or `between` 0 and 1 for a fraction.
5. With 64 rows or fewer, sample them ("Sample survivors" below), then finish with `exact` or `between`.

When the value sits in a known range of 16 MiB or less (an object, a module's `.data`), a snapshot comparison ("Memory
snapshots" below) is cheaper than an unknown scan.

## Floats and doubles

- Displays round. Use `between` over the rounding: shown `87` means 86.5 to 87.5, and shown `87.3` means 87.25 to
  87.35. A display that truncates needs 87 to 88. Scaled displays apply too: 87 % may be 0.865 to 0.875.
- `scan_first.floatDecimals` and `scan_next.floatDecimals` (0 to 15; default 6 for float, 12 for double; float and
  double only) set the decimals every float value is written with, `between` bounds included. Cheat Engine matches
  `exact` at that precision, so by default an exact `87` keeps only values within about a millionth of 87. Each decimal
  fewer widens the match tenfold. The typed value is converted to the type first, so 25.256 is sent as 25.256001.
- `increasedBy` and `decreasedBy` on floats compare at that precision too: by default the new value must equal old
  plus or minus the delta to six decimals, which float rounding noise can break, and a missed narrowing drops the real
  address. Pass the delta's own decimals:
  `scan_next(scannerName="main", comparison="decreasedBy", value="0.5", floatDecimals=1)`.
- On main, Cheat Engine's rounding setting also applies, and MCP never changes it: a fresh install ticks Rounded
  (extreme), not the option captioned Rounded (default). A named scan always uses Rounded (default). The modes'
  exact boundaries are not documented consistently, so prefer `between` over tuning `floatDecimals`.
- On main, a ticked Simple values only drops nonzero float and double values outside about 0.001 to 2048 in magnitude.
- `NaN` and `Infinity` are refused. A float stores whole numbers exactly only up to 16,777,216.
- Try `double` when float finds nothing: script-driven and idle games, and large-world positions, often use doubles
  (see [game engines](game-engines.md)).
- For values in constant motion (positions, velocities), pause the target around each scan with
  `process_set_paused(paused=true)` and resume with `process_set_paused(paused=false)`, telling the user first. MCP's
  pause is a tracked resource that blocks a target switch until you resume. Otherwise use `unknown`, then `changed`
  and `unchanged`. Main follows the user's "Pause the game while scanning" box.
- After one axis of a position is found, read its neighbours (see [structures](structures.md) and
  [find a position](../Workflows/find-position.md)).

## Percentages and bars

MCP cannot run Cheat Engine's percentage scan or compare to the first scan. Use one of these instead:

1. Guess the scale. A bar at about 75 % is a float from 0.70 to 0.80, or a float or integer from 70 to 80. Test the
   guesses one at a time on main, or in parallel on scoped named scanners.
2. Use the unknown-value procedure. It works whatever the scale.
3. Check the ratio. With 1024 survivors or fewer, read them with `memory_read_batch` before and after one change. The
   real value changes by the same ratio as the bar (80 % to 60 % is x0.75), whatever the scale or maximum.
4. Ask the user to run the percentage scan by hand in Cheat Engine (Compare to first scan, Percent, Between).
   `scan_get_status` and `scan_list_results` then read main's results. A later MCP `scan_next` on main unticks those
   boxes again.

## Signed and unsigned integers

- `exact` compares bits, so signedness does not matter for it.
- `int16`, `int32` and `int64` take signed values; `byte` takes 0 to 255. Pass an unsigned value above the signed
  maximum as its signed equivalent: uint32 4294967295 is int32 -1, and uint16 50000 is int16 -15536. Compute it with
  `util_convert_value(value="50000", sourceType="uint16", targetType="int16")`. A negative int8 is scanned as its
  `byte`: -1 is 255.
- Cheat Engine compares integers unsigned for `greater`, `less`, `increased` and `decreased`. A value going from 0 to
  -1 counts as increased, and `less` than 10 drops every negative value. `between` compares signed when a bound is
  negative. For values that can go negative, use `between` with a negative bound, or `changed`.
- `increasedBy` and `decreasedBy` on integers keep new = old + delta or old - delta, with wrap-around.

## Fast scan and memory regions

- Cheat Engine's fast scan, on by default, checks only aligned addresses: 4 for 4-byte, 8-byte, float and double, 2
  for 2-byte, and 1 for byte, text and byte arrays. Main follows the user's setting, and Cheat Engine resets the
  alignment to that size whenever `scan_first` sets the type, unless the user edited it. A named scan checks every
  address unless it sets `scan_first.alignment`. Leave alignment off for packed structures, serialized or emulated
  data, and byte or 2-byte fields.
- Cheat Engine's protection filter starts as Writable required, CopyOnWrite excluded and Executable either way, and
  main uses whatever the user set. A named scan has no filter until you set one. Excluding copy-on-write rarely loses
  live data: Windows gives a page a private writable copy on its first write.
- Cheat Engine skips mapped memory (file views and shared sections, where emulators keep guest RAM) unless MEM_MAPPED
  is ticked in its Scan Settings. Main follows that box: ask the user to tick it. `scan_first.includeMapped` adds
  mapped memory to one named scan, and `scan_next` keeps those regions. The override is Cheat Engine-wide: when the
  scan returns it ends every scan-region override, including one set by a table or `lua_execute`, and overlapping
  first scans must agree on includeMapped, or one is refused with `busy` (see [emulators](emulators.md)).
- Scans compare little-endian numbers. On a big-endian guest (GameCube, Wii, PS3), rely on `changed` and `unchanged`,
  then read candidates with
  `memory_read_batch(items=[{address="<address>", valueType="int32", byteOrder="big_endian"}])`.
- An unknown-value baseline stores every scanned byte, so its time and disk use grow with the memory it covers.

## Snapshots, sampling and one-shot searches

### Memory snapshots

A snapshot copies up to 16 MiB into this activation (at most 16 snapshots and 64 MiB in total) and holds no Cheat
Engine scanner. Use it for an unknown value inside a known range:

1. `memory_create_snapshot(name="before", address="<start>", size=65536)`
2. The user does one thing in game.
3. `memory_create_snapshot(name="after", address="<start>", size=65536)`
4. `memory_compare_snapshot(name="before", compareTo="after", valueType="float", change="decreased")`; page with
   `memory_compare_snapshot.offset` set to the returned `nextOffset`.
5. `memory_delete_snapshot(name="before")`, then the same for "after".

Comparing with live memory (without `compareTo`) reads again on every call, so its pages can shift; two snapshots page
stably. A live comparison fails with `target_changed` after a process switch. See
[compare snapshots](../Workflows/compare-snapshots.md).

### Sample survivors

`memory_read_samples(addresses=["<a1>", "<a2>"], valueType="float", intervalMs=100, durationMs=3000)` reads up to 64
addresses for up to 10 s while the user makes the value change. Each entry of `memory_read_samples.series` has
`first`, `last`, `distinctCount` and `changes`, whose `timeMs`, like `elapsedMs`, counts from the first read. The real
value follows the action, a constant has one distinct value, and a display copy may lag or jump. A tick that Cheat
Engine refuses as busy is skipped and counted in `memory_read_samples.missedTicks`.

### One-shot value search

`aob_find_value(valueType="uint32", value="3000000000", module="game.exe", writable="required", limit=20)` encodes
one value as the target's little-endian bytes and finds it with one byte-pattern scan, without scanner state. Use it:

- for a quick check while main holds the user's scan;
- for types `scan_first` lacks (`int8`, `uint16` to `uint64`, `pointer`);
- to find other copies of a verified value.

It matches exact bits only, so it cannot find a rounded float and cannot narrow. It blocks Cheat Engine while it runs,
so scope it with `module` or a range. Its alignment defaults to the value's size, up to 4; 1-byte integers and strings
match at any address. `aob_find_value.includeMapped` adds mapped memory, but not with `module`. Read
`aob_find_value.result`, whose `unique` flag needs a `limit` of 2 or more.

## Verify candidates

1. Read them all with `memory_read_batch(items=[{address="<address>", valueType="int32"}])`, or sample them while the
   value changes ("Sample survivors" above).
2. With the user's consent, test one address at a time:
   `memory_write(address="<address>", valueType="int32", value="999")` returns `memory_write.previous`. Watch the
   game, then restore with `memory_write(address="<address>", valueType="bytes", value="<previous>")` unless the change
   is wanted. Never write every candidate at once: a wrong write can crash the target.
3. The real value changes gameplay: damage uses it and shops accept it. A display copy changes only the HUD, or
   reverts on the next frame.
4. For proof, and with consent to attach the debugger (`debugger_attach` first),
   `debugger_start_capture(address="<address>", trigger="write")` shows the instruction that writes it; for an 8-byte
   value use `debugger_start_capture(address="<address>", trigger="write", size=8)` (see
   [find what writes](../Workflows/find-writer.md) and [debugger](debugger.md)).
5. After a failed write, read the error's `kind` and `hostEffect`, and read the address before any retry (see
   [errors and recovery](errors-and-recovery.md)).

## Static or dynamic

- `memory_get_address_info(addresses=["<address>"])` classifies a result row: `symbol` (such as `game.exe+1C0`),
  `module`, `section` and the region's `type`: `image` inside a module, `private` for heap, `mapped` for file views.
- A static address lies in a module image, such as a `.data` global. It survives restarts as module+offset.
- A dynamic address (heap or stack) moves after a restart or level load. It needs a pointer chain or a code injection
  (see [pointers](pointers.md) and [pointer scan](../Workflows/pointer-scan.md)).
- In a Unity game, `mono_get_object(address="<address>")` names the object that holds a heap hit, the hit's
  `offsetInObject` and the fields with their values. It needs the collector that `mono_attach` injects, so ask first
  (see [Mono and .NET](mono-and-dotnet.md)). For a .NET game, use `dotnet_get_object` on the object's start.

## Pitfalls

- Results from before a restart, a level load or a target switch mean nothing: start again.
- Each next scan should follow exactly one known change in game.
- Delete named sessions before the user picks another process in Cheat Engine by hand. After that, `scan_reset`
  refuses them and `scan_delete` reports `partial_effect`: follow its hint (see
  [errors and recovery](errors-and-recovery.md)) and scan under another name. Track which scanner holds which guess.
- Write down what you found (address, type, how it was proven) before moving on to pointers or code (see
  [cheat tables](cheat-tables.md)).

Guided versions: [find a known value](../Workflows/find-known-value.md),
[find an unknown value](../Workflows/find-unknown-value.md), [find a float](../Workflows/find-float-value.md),
[find a flag](../Workflows/find-flag.md), [find a timer](../Workflows/find-timer.md) and
[group scan](../Workflows/group-scan.md).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Memory_Scanning
- https://wiki.cheatengine.org/index.php?title=Tutorials%3AFinding_values%3AFloats
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/memscan.pas (unsigned integer
  comparisons, signed between, float rounding, MEM_MAPPED filter)
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/MainUnit.pas and MainUnit.lfm
  (fast-scan alignment, protection and rounding defaults, the Hex, Percent and Repeat boxes)
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/formsettingsunit.lfm (MEM_MAPPED)
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt (MemScan, rounding,
  setSpecialScanOptionsOverride)
- https://learn.microsoft.com/windows/win32/memory/memory-protection
- https://learn.microsoft.com/cpp/build/ieee-floating-point-representation
- https://learn.microsoft.com/dotnet/api/system.single.parse (a comma is a thousands separator in invariant floats)
