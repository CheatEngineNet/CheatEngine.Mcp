# Find a floating-point value

## Goal

Find a float or double such as health, stamina, speed or a coordinate. `{displayedValue}` is what the game shows (not
given for a bar without a number); `{description}` names it. Only on single-player or offline software the user may
modify; each test write and record needs the user's consent.

## Steps

1. `process_get_current()`: confirm the target. `scan_get_status(scannerName="main")` must show `Created`; if main
   holds the user's results, ask before `scan_reset(scannerName="main")`.
2. Turn `{displayedValue}` into a range over the display's rounding: shown 87 is 86.5 to 87.5, shown 87.3 is 87.25
   to 87.35, a truncating display needs 87 to 88. A percentage (75%): try 0.745 to 0.755, then 74.5 to 75.5.
3. `scan_first(scannerName="main", valueType="float", comparison="between", value="<low>", upperValue="<high>")`;
   with no display, `scan_first(scannerName="main", valueType="float", comparison="unknown")`. Poll
   `scan_get_status(scannerName="main")` until `isScanning` is false.
4. Ask the user to change the value once, then
   `scan_next(scannerName="main", comparison="between", value="<low>", upperValue="<high>")` around the new display,
   or `scan_next(scannerName="main", comparison="decreased")` (or `increased`) without one. While nothing happens,
   `scan_next(scannerName="main", comparison="unchanged")`. Poll; repeat until about 10 rows remain.
5. Values in constant motion: with the user's OK, `process_set_paused(paused=true)` before each scan and
   `process_set_paused(paused=false)` once the poll shows it finished.
6. `scan_list_results(scannerName="main", startIndex=0, maximumResults=20)` gives display text; re-read typed with
   `memory_read_batch(items=[{address="<a1>", valueType="float"}])`.
7. Neighbours: `memory_read(address="<address>-8", valueType="float", count=6)` (offsets are hex). A maximum often
   follows the current value; X, Y and Z sit 4 bytes apart (8 for doubles).
8. With consent, test one address at a time:
   `memory_write(address="<address>", valueType="float", value="<test value>")`, keep `previous`, ask what changed,
   and restore unless wanted: `memory_write(address="<address>", valueType="bytes", value="<previous>")`.
9. With consent,
   `record_create(records=[{description="{description}", address="<address>", variableType=4}])` (5 for a double);
   with no `value`, nothing is written. Keep the record id.

## Decisions

- Nothing with float: try `double` (GameMaker, JavaScript engines and UE5 positions use it): on main, reset and
  repeat steps 3-4. To keep main's results, a named scanner, scoped since it blocks CE while it scans:
  `scan_first(scannerName="dbl", valueType="double", comparison="between", value="<low>", upperValue="<high>", writable="required", alignment=4)`.
- Still nothing: an integer scaled by 10 or 100 ([find a known value](find-known-value.md) with int32), or a value
  computed for display only (a speed from a velocity): find its inputs. More causes:
  [when a scan finds nothing](../Documents/troubleshooting-scans.md).
- An emulator: [emulator memory](emulator-memory.md). Big-endian guests (GameCube, Wii, Wii U, PS3) allow only
  `unknown`, `changed` and `unchanged` float scans; read with
  `memory_read(address="<address>", valueType="float", byteOrder="big_endian")`, likewise `memory_write`.
- Several rows follow the display: copies. Confirm the one game logic writes with [find what writes](find-writer.md).

## Pitfalls

- Never scan a rounded display with `exact`: it matches at `scan_first.floatDecimals` decimals (default 6 for float,
  12 for double), so prefer `between`. Main also applies CE's rounding, and a ticked Simple values only drops values
  outside about 0.001 to 2048, large coordinates included: check `scan_get_status.settings`.
- A float holds whole numbers exactly only up to 16,777,216.
- Do not freeze the maximum by mistake. An absurd coordinate can drop the player out of the world: restore
  `previous`.
- A running main scan blocks `process_attach`: `scan_stop(scannerName="main")`, then poll until idle. After a
  timeout, read `scan_get_status` before scanning again; never repeat a scan or a write whose `hostEffect` is
  `started` or `unknown`.

## Report

Table: address, float or double, value, neighbours noticed, proof and record id. Still owned: a named scanner until
`scan_delete(scannerName="dbl")`, MCP's pause until `process_set_paused(paused=false)`, the record until
`record_delete(ids=[<id>])`. Background: [value scans](../Documents/value-scans.md) and
[structures](../Documents/structures.md).
