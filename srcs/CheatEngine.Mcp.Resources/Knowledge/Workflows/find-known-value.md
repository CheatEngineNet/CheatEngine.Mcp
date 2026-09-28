# Find a known value

## Goal

Find the address of a number the game shows: `{currentValue}` now, scan type `{valueType}` (`auto`: you choose),
described as `{description}`. Only on single-player or offline software the user may modify; each test write and
record needs the user's consent.

## Steps

1. `process_get_current()`: confirm the target ([attach](attach-and-orient.md) if `isOpen` is false).
   `scan_get_status(scannerName="main")` must show `Created`; if main holds results, ask before
   `scan_reset(scannerName="main")`, or use a named scanner (Decisions).
2. For `auto`: `int32` for a whole number (above 2147483647: Decisions), `string` for text (`wstring` for UTF-16);
   numbers are decimal, no `0x`. A shown decimal, `float` or `double` needs `between` over the display's rounding:
   [find a float value](find-float-value.md).
3. `scan_first(scannerName="main", valueType="<type>", comparison="exact", value="{currentValue}")`. Main returns at
   once: poll `scan_get_status(scannerName="main")` until `isScanning` is false, then read `count`.
4. Ask the user to change the value in game and to tell you the new number.
5. `scan_next(scannerName="main", comparison="exact", value="<new value>")`, then poll. Knowing only the change:
   `scan_next(scannerName="main", comparison="decreasedBy", value="<loss>")` (or `increasedBy`). While nothing
   happens: `scan_next(scannerName="main", comparison="unchanged")`. Repeat 4-5 until about 10 rows remain.
6. `scan_list_results(scannerName="main", startIndex=0, maximumResults=20)`, then
   `memory_get_address_info(addresses=["<a1>", "<a2>"])`: region type `image` is static, `private` is heap.
7. `memory_read_batch(items=[{address="<a1>", valueType="int32"}])` (scan `byte` reads as `uint8`). With consent,
   test one candidate at a time: `memory_write(address="<a1>", valueType="int32", value="<test value>")`, keep
   `previous`, ask whether the game changed, and restore unless wanted:
   `memory_write(address="<a1>", valueType="bytes", value="<previous>")`.
8. With consent, `record_create(records=[{description="{description}", address="<address>", variableType=2}])`
   (0 byte, 1 int16, 3 int64, 4 float, 5 double; text is 6 with `length=<characters>`, plus `unicode=true` for
   `wstring`); with no `value`, nothing is written. Keep the record id.

## Decisions

- Main busy or holding the user's work: a named scanner leaves CE's UI alone but blocks CE while it scans, so scope
  it (alignment 2 for int16, none for byte or text):
  `scan_first(scannerName="value1", valueType="<type>", comparison="exact", value="{currentValue}", writable="required", alignment=4)`,
  then `scan_next(scannerName="value1", comparison="exact", value="<new value>")`, without polling.
- No results: reset, then try `float` with `between`, `double`, `int16`, `int64`, `byte`, then scaled values (x10,
  x100, 0..1). Main inherits CE's range, Writable, fast-scan, rounding and MEM_* settings, which
  `scan_get_status.settings` shows: [when a scan finds nothing](../Documents/troubleshooting-scans.md). Emulators:
  [emulator memory](emulator-memory.md). Still nothing: [find an unknown value](find-unknown-value.md).
- Above 2147483647 and up to 4294967295, it may be a uint32: scan `int32` with the signed equivalent from
  `util_convert_value(value="4000000000", sourceType="uint32", targetType="int32")` (`int64` matches only if the next
  4 bytes are zero); larger is `int64` or `double`. `byte` is unsigned (-1 is 255).
- Rows that change together, or a test write that reverts: copies; [find what writes](find-writer.md) shows the real
  one. Moves after a restart: [pointer scan](pointer-scan.md).

## Pitfalls

- Common values (0, 1, 100) match everywhere: have the user make the number unusual first.
- Main answers `busy` while scanning, which also blocks `process_attach`: `scan_stop(scannerName="main")`, then poll
  until idle. After a timeout, read `scan_get_status` first; never repeat a scan or a write whose `hostEffect` is
  `started` or `unknown`.
- Results from before a restart or process switch mean nothing. Text scans take `exact` only.

## Report

Table: address, type, module+offset or heap, value, proof and record id. Still owned: a named scanner until
`scan_delete(scannerName="value1")`; the record until `record_delete(ids=[<id>])`. Practice: CE tutorial step 2
([CE tutorial](../Documents/ce-tutorial.md)). Background: [value scans](../Documents/value-scans.md).
