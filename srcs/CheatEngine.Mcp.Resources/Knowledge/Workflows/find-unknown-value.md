# Find an unknown value

## Goal

Find a value the game does not show as a number (a bar, a hidden stat), scan type `{valueType}` (`auto`: you
choose), described as `{description}`. Only on single-player or offline software the user may modify; each test
write and record needs the user's consent.

## Steps

1. `process_get_current()`: confirm the target ([attach](attach-and-orient.md) if `isOpen` is false).
   `scan_get_status(scannerName="main")` must show `Created`; if main holds the user's results, ask before
   `scan_reset(scannerName="main")`.
2. For `auto`: `float` for bars, gauges and timers, `int32` for counters such as ammo.
3. `scan_first(scannerName="main", valueType="<type>", comparison="unknown")`: a whole-process baseline belongs on
   main. Poll `scan_get_status(scannerName="main")` until `isScanning` is false and `state` is `BaselineReady`, which
   has no rows yet.
4. Ask the user to make the value go down or up once, and which way. Then
   `scan_next(scannerName="main", comparison="decreased")` or `scan_next(scannerName="main", comparison="increased")`;
   `changed` when the direction is unclear. Poll after every scan.
5. While the value stays still, `scan_next(scannerName="main", comparison="unchanged")` two or three times drops
   timers and animations.
6. An integer whose exact change is known (lost 5):
   `scan_next(scannerName="main", comparison="decreasedBy", value="5")` (or `increasedBy`). A float: bound it with
   `scan_next(scannerName="main", comparison="greater", value="0")`, or `between` 0 and 1 for a fraction.
7. Repeat 4-6 until 64 rows or fewer remain: `scan_list_results(scannerName="main", startIndex=0, maximumResults=64)`.
8. Ask the user to change the value during the next few seconds, then
   `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="<type>", intervalMs=100, durationMs=5000)`: keep the
   series whose `changes` follow the action (a constant has `distinctCount` 1).
9. With consent, test one candidate at a time:
   `memory_write(address="<a1>", valueType="<type>", value="<test value>")`, keep `previous`, ask whether the bar
   moved, and restore unless wanted: `memory_write(address="<a1>", valueType="bytes", value="<previous>")`.
10. With consent,
    `record_create(records=[{description="{description}", address="<address>", variableType=4}])` with 2 for int32,
    4 for float or 5 for double; with no `value`, nothing is written. Keep the record id.

## Decisions

- Zero results: a direction was misreported or the type is wrong; start again with the other type (float or int32,
  then double). Encoded values and big-endian guests (GameCube, Wii, Wii U, PS3) move out of order: use only
  `changed` and `unchanged`. Main follows CE's own scan
  settings (range, Writable, fast scan, MEM_*), which `scan_get_status.settings` shows: see
  [when a scan finds nothing](../Documents/troubleshooting-scans.md). Emulated consoles:
  [emulator memory](emulator-memory.md).
- Many rows keep moving together: copies or a UI cache. Classify them with
  `memory_get_address_info(addresses=["<a1>", "<a2>"])`; confirm the real one with [find what writes](find-writer.md).
- A known object or range of 16 MiB or less: [compare snapshots](compare-snapshots.md) is cheaper.
- Main holds the user's work: ask first, or use a named scanner scoped to a module or heap range as in
  [value scans](../Documents/value-scans.md#scoping-a-named-scan); it blocks CE while it scans.
- A percentage or a shown decimal: [find a float value](find-float-value.md). A cooldown:
  [find a timer](find-timer.md).

## Pitfalls

- An unknown baseline copies every scanned byte, so it is slow and large. After a timeout, read `scan_get_status`
  before scanning again; never repeat a call whose `hostEffect` is `started` or `unknown`.
- Integers compare unsigned for `increased` and `decreased` (0 to -1 counts as increased): use `changed` for values
  that can go negative.
- Moving values: with the user's OK, `process_set_paused(paused=true)` before a scan and
  `process_set_paused(paused=false)` once the poll shows it finished. MCP tracks its pause, which blocks a target
  switch until resumed.
- Each next scan should follow exactly one change in game. A running main scan makes `process_attach` fail:
  `scan_stop(scannerName="main")`, then poll until idle. A restart or process switch voids the results.

## Report

Table: address, type, module+offset or heap, value, proof, record id and remaining candidates. Still owned: a named
scanner until `scan_delete(scannerName="<name>")`, a paused target until `process_set_paused(paused=false)`, the
record until `record_delete(ids=[<id>])`. Background: [value scans](../Documents/value-scans.md).
