# Find an unknown value

## Goal

Find a value the game does not show as a number (health bar, stamina, timer), type `{valueType}` (default auto),
described as `{description}`.

## Steps

1. `process_get_current()` and `scan_get_status(scannerName="main")`: main must be idle; ask before
   `scan_reset(scannerName="main")` if it holds results.
2. Choose the type: `{valueType}`, or for auto: float for bars, gauges, positions and timers; int32 for counters and
   ammo.
3. `scan_first(scannerName="main", valueType="<type>", comparison="unknown")`. Unknown whole-process baselines belong on
   main. Narrow when you can: `module=...`, `startAddress`/`endAddress`, `writable="required"`.
4. Poll `scan_get_status(scannerName="main")` until the baseline is ready. A baseline has no result list yet; do not
   page it.
5. Ask the user to make the value go up or down and to say which. Then
   `scan_next(scannerName="main", comparison="decreased")` or `"increased"`; `"changed"` when the direction is unclear.
   Poll after each scan.
6. While the value stays still, have the user move or wait, then `scan_next(scannerName="main", comparison="unchanged")`
   two or three times to shed noise.
7. When the user knows the exact change (lost 5 points), use `comparison="decreased_by"` or `"increased_by"` with
   `value="5"`.
8. Repeat until about 50 results remain, then `scan_list_results(scannerName="main", maximumResults=50)` and watch the
   values with `memory_read_batch(items=[{address, valueType}, ...])` while the user acts.
9. Once the value is guessable, narrow with `comparison="between"` (`value`, `upperValue`) or `"exact"`.
10. Ask for consent, then test one candidate at a time with `memory_write` (keep `previous` and restore it) and ask the
    user whether the bar moved. With consent,
    `record_create(records=[{description:"{description}", address, valueType}])`.

## Decisions

- The count stops falling with hundreds of candidates moving together: copies or a UI cache. Check
  `memory_get_address_info(addresses=[...])` and confirm the real one
  with [find what writes](../workflows/find-writer.md).
- Zero results: a direction was misreported or the type is wrong. Start again with the other type (float vs int32, then
  double).
- The value is back at its starting level: `scan_next(scannerName="main", comparison="unchanged")` only keeps addresses
  unchanged since the preceding scan. If the first-scan comparison is required, start a new scan when the value is at
  that level.
- Percent bars are often 0..1 or 0..100 floats; see [find a float value](../workflows/find-float-value.md).

## Pitfalls

- An unknown scan over the whole process is large and slow; narrow the range and poll, never wait.
- `pauseWhileScanning=true` (main only) pauses the game during a scan; ask first.
- A running main scan blocks target changes; after an uncertain response, check `scan_get_status` before scanning again.
- A test write whose `hostEffect` is not `not_started` or `not_applied` may already have happened: read before any
  retry.

## Report

Address, type, module+offset when static, value, record id, and the remaining candidate count. Main results stay in CE;
a named scanner is released with `scan_delete(scannerName=...)`; the record stays in CE's address list until deleted.
See [value scans](../value-scans.md).
