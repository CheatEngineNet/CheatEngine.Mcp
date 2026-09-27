# Value types and scan strategy

Use the `scan_*` tools to find where a game stores a value. Each instance has its own scanners. Attach with
`process_attach`, then confirm the target with `process_get_current`. Addresses come back as uppercase hex without `0x`;
values and 64-bit numbers come back as strings.

## Value types

| `valueType`        | CE type                    | Size        | Typical game use                                                                  |
|--------------------|----------------------------|-------------|-----------------------------------------------------------------------------------|
| `int8` / `uint8`   | Byte                       | 1           | flags, booleans, small counters, levels in older games                            |
| `int16` / `uint16` | 2 Bytes                    | 2           | item ids, stat points, older engines                                              |
| `int32` / `uint32` | 4 Bytes                    | 4           | health, ammo, money, score, counts; the default guess for whole numbers           |
| `int64` / `uint64` | 8 Bytes                    | 8           | large currencies, XP, ids, timestamps                                             |
| `float`            | Float                      | 4           | health/stamina bars, positions, velocities, timers, multipliers, 0..1 percentages |
| `double`           | Double                     | 8           | money or timers in script-driven and idle games, large-world positions            |
| `pointer`          | 4 or 8 Bytes, shown as hex | 4 / 8       | object references, climbing pointer chains                                        |
| `string`           | String (UTF-8/ANSI)        | text length | names and ASCII text                                                              |
| `wstring`          | String, Unicode (UTF-16)   | 2 x length  | Windows, .NET and Unity strings                                                   |
| `bytes`            | Array of byte              | n           | fingerprints, packed or encoded data                                              |

- Integers are little-endian two's complement: `int32` and `uint32` store the same bytes. Signedness only changes
  display and ordering comparisons (`greater`, `less`, `increased`) across the sign boundary. Use the widths the scan
  schema offers.
- Numeric input is decimal. Prefix hex with `0x`. Call `util_convert_value` to see every interpretation of a value
  instead of converting by hand.
- `pointer` follows the target's pointer size. Check `process_get_current` for `pointerSize`, `bitness` and
  `pointerSizeMismatch`.
- `bytes` use spaced hex (`48 8B 05`); `??` wildcards work on `main` only.

## Choosing a type

- Whole number on screen: try `int32`, then `float`, then `double`, then `int16` or `int64`.
- Bar or gauge without a number: unknown-initial scan with `float`.
- Percentage: a float in 0..1 or 0..100, or an integer 0..100.
- Scaled display: `12.5` may be stored as `125` or `1250`; `1.2k` as `1200`. Try x10, x100 and /100.
- Money above about 2.1 billion: `int64` or `double`.
- Positions: three adjacent floats (X, Y, Z four bytes apart), sometimes doubles. After finding one, dissect the
  neighbourhood (see [structures](structures.md)).
- Timers: float seconds, or integer milliseconds or frames.
- Nothing matches with any type: the value may be encoded (XOR, stored twice, checksummed) or computed on demand. Find
  the code that uses it instead (see [debugger](debugger.md)), or run unknown and `changed` scans.

## Comparisons

| Scan        | `comparison`                   | Inputs                            | Use                                  |
|-------------|--------------------------------|-----------------------------------|--------------------------------------|
| first, next | `exact`                        | `value`                           | known value                          |
| first, next | `between`                      | `value`, `upperValue` (inclusive) | floats, rounded displays             |
| first, next | `greater`, `less`              | `value`                           | bounded guesses                      |
| first       | `unknown`                      | none                              | baseline for a value you cannot read |
| next        | `increased`, `decreased`       | none                              | direction known                      |
| next        | `increased_by`, `decreased_by` | `value` (delta)                   | exact change known                   |
| next        | `changed`, `unchanged`         | none                              | direction unknown; noise shedding    |

- `compareTo="first"` (main only) compares against the first scan instead of the previous one, like CE's "compare to
  first scan" option.
- `percentage=true` (main only) turns `between`, `increased_by` and `decreased_by` into percentage comparisons.

## Known-value loop

1. `scan_first(valueType="int32", comparison="exact", value="100")`, then poll `scan_get_status` until `results_ready`.
2. Ask the user to change the value in game.
3. `scan_next(comparison="exact", value="93")`, poll again. Repeat until 10 or fewer results.
4. Page with `scan_list_results(offset=0, limit=100)`, then verify candidates (below).

## Unknown-value loop

1. `scan_first(valueType="float", comparison="unknown")`. The state becomes `baseline_ready`; it cannot be paged yet.
2. After each in-game change, `scan_next(comparison="decreased")` or `increased`; use `changed` when the direction is
   unclear.
3. While the value is idle, run `unchanged` two or three times to shed timers, animation and camera noise.
4. Once the set is small, read a candidate and finish with `between` or `exact`.

## Floats and rounding

- Displays round. An exact float scan for `57` rarely matches `57.38`. Start with `between` v-0.5 .. v+0.5 (`56.5` ..
  `57.5`).
- `floatDecimals` defaults to the decimals typed in `value`: `57` compares at integer precision, `57.0` is stricter.
  Never type digits the game does not show.
- `rounding` (main only) selects CE's rounded (default), truncated or extreme-rounded matching. Truncated ignores the
  fraction; extreme is the most tolerant. An explicit `between` is clearer.
- Fast-moving floats (positions, velocities) need the target still: `pauseWhileScanning=true` on main, or
  `process_set_paused` around each scan.

## Verify candidates one at a time

- Read them all with `memory_read_batch`.
- Test one address per step: `memory_write(address, valueType, value)` returns `previous`. Watch the game, then restore
  `previous` unless the change is wanted. Never write every candidate at once; a wrong write can crash the target.
- The real value changes gameplay: damage uses it and shops accept it. A display copy changes only the HUD, or reverts
  on the next frame.
- For proof, `debugger_start_capture(address, trigger="write")` shows the game-logic instruction that writes it
  (see [debugger](debugger.md)).
- A write is a mutation. After an error, read `error.kind` and `hostEffect`; never repeat a write whose effect is
  `started` or `unknown` without reading the address first.

## Static and dynamic addresses

- `scan_list_results` returns `symbol` (module+offset) for static hits, which CE shows in green. They live in a module
  image (globals, `.data`) and survive restarts relative to the module base.
- Hits without `symbol` are dynamic (heap or stack). They move after a restart or level load and need a pointer chain or
  an injection base (see [pointers](pointers.md)).
- `memory_get_address_info(addresses=[...])` reports region, module and section for any address.

## Scope and speed

- Alignment: CE's fast scan checks 4-aligned addresses by default. Pass `alignment` (for example `4`) or `lastDigits`
  (known trailing hex digits), never both. Use no alignment for `int8`, `int16`, strings, bytes and packed structures.
- Protection: `writable`, `executable` and `copyOnWrite` each take `required`, `excluded` or `any`. For game values use
  `writable="required"`, `copyOnWrite="excluded"`, `executable="any"`.
- Range: `module` limits the scan to one module image (static values). `startAddress`/`endAddress` bound it in hex.
- CE's own settings scan private and image memory but skip mapped memory by default. Values inside file mappings (some
  emulators, shared memory) need the user to change that CE setting; MCP never changes CE settings.

## Main and named scanners

- `scannerName="main"` (the default) is CE's visible scan tab. MCP drives its native controls, so the UI, found count
  and later manual scans stay in sync. It can read and narrow a scan the user started by hand.
- UI options change only when passed, then are read back; `uiOptionsChanged` lists what changed in the user's UI.
  Main-only options: `rounding`, `caseSensitive`, `pauseWhileScanning`, `compareTo`, `percentage`.
- Main is CE-owned. It survives plugin disable, `runtime_release_resources` leaves it alone, and MCP never destroys it.
  With results present, call `scan_reset` (CE's New Scan) before another `scan_first`. `scan_stop` cancels a running
  main scan.
- Any other name is an independent Client session (up to 32 per instance). It never updates the UI, is an
  activation-owned resource and runs as a background job. It cannot be interrupted once started. `scan_reset` clears it
  and keeps the session; `scan_delete` releases it.
- Run whole-process unknown-initial scans on main. Give a named unknown scan a `module` or an address range.
- Names are case-sensitive. An unknown name fails with `not_found`; it never falls back to main. `scan_list_scanners`
  lists them all.

## Polling states

| `state`          | Meaning                                         | Next step                             |
|------------------|-------------------------------------------------|---------------------------------------|
| `no_target`      | no process attached                             | `process_attach`                      |
| `created`        | no scan, or CE reset a failed or cancelled scan | inspect CE; start a first scan        |
| `scanning`       | running; `progress` shows scanned, total, found | poll again                            |
| `baseline_ready` | unknown baseline stored                         | narrow with `scan_next` before paging |
| `results_ready`  | `count` results                                 | `scan_list_results`, `scan_next`      |
| `failed`         | `error` holds CE's text                         | fix inputs, `scan_reset`              |
| `invalidated`    | target or runtime changed                       | `scan_reset` and start over           |

- A start is not a result. After a timeout or lost response, never re-issue the scan: read `scan_get_status` and the
  visible UI first. On failure CE may show its own error dialog and return to `created`.
- A running main scan refuses reset, paging and narrowing, and blocks target changes (`process_attach` is refused).
  Wait, or call `scan_stop`. If the user enabled CE's "repeat until stopped" mode by hand, ask them to stop it in CE.
- `scan_list_results` pages at most 1024 rows; `nextOffset` is omitted on the last page.

## Pitfalls

- Results from before a restart or target switch are meaningless; start a new scan.
- Two CE instances attached to the same game see the same memory but have separate scanners.
- Keep scans narrow in time: each scan should follow exactly one known change in game.
- Record what you found (address, type, how it was proven) before moving on to pointers or code
  (see [cheat-tables](cheat-tables.md)).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Memory_Scanning
- https://wiki.cheatengine.org/index.php?title=Help_File:Scan_settings
- https://wiki.cheatengine.org/index.php?title=Tutorials%3AFinding_values%3AFloats
- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt
- https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-memory_basic_information
