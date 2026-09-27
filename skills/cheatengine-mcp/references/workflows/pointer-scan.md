# Find a static pointer with the pointer scanner

## Goal

Find pointer paths from a module to the value at `{address}` that survive restarts, with depth up to `{maxDepth}`
(default 5) and offsets up to `{maxOffset}` (default 4096).

## Steps

1. `memory_read(address="{address}", valueType=...)`: confirm it still holds the value.
2. `pointer_create_map(mapName="map1", writableOnly=true)`: a background job. Poll `pointer_list_maps()` until the map
   is complete; note `incomplete` and `unreadableBytes`.
3.
`pointer_find_paths(scanName="scan1", mapName="map1", target="{address}", maxDepth={maxDepth}, maxOffset={maxOffset}, staticRootsOnly=true)`:
also a background job. Poll `pointer_list_scans()` for `count`, `incomplete` and `traversalLimited`.
4. `pointer_list_paths(scanName="scan1", limit=50, sortBy="depth")`.
5. Ask the user to restart the game (or reload the level) so the value moves. Attach to the new process with
   `process_attach(process=...)`; maps and scans are kept for rescans, but captures, patches and other owned resources
   must be released first.
6. Find the value again ([find a known value](../workflows/find-known-value.md)) at a new address.
7. `pointer_rescan_paths(scanName="scan1", target="<new address>")`: live verification; read `verified`, `removed` and
   `unresolved`.
8. Repeat steps 5-7 two or three times until few paths remain.
9. Rank: root in the main executable, fewest levels, smallest offsets, live-verified.
   `pointer_list_paths(scanName="scan1", sortBy="depth", moduleContains="<main module>")`.
10. Verify the best three with `pointer_read_chain(base=<module+offset>, offsets=[...])`.
11. With consent, `record_create(records=[{description, address:<root>, offsets:[...], valueType}])`.
12. Clean up: `pointer_delete_scan(scanName="scan1")` and `pointer_delete_map(mapName="map1")`.

## Decisions

- No paths: raise `maxOffset` (large objects exceed 4096) or `maxDepth`, try `allowNegativeOffsets=true`, or capture a
  narrower but complete map. Still nothing: use an [injection copy](../workflows/injection-copy-base.md) or
  a [manual chain](../workflows/manual-pointer-chain.md).
- Thousands of paths after rescans: do more restarts;
  `pointer_rescan_paths(scanName="scan1", target=..., dropUnresolved=true)` drops unverified ones.
- Offline comparison: capture `pointer_create_map(mapName="map2")` after a restart and rescan with `mapName="map2"`.

## Pitfalls

- `incomplete` or `traversalLimited` never proves that a path does not exist; `unresolved` is not verified.
- There are no stack roots; a value reached only from a thread stack needs another method.
- The game changes memory while the map is captured; with consent, `process_set_paused(paused=true)` during the capture,
  then always `process_set_paused(paused=false)`.
- Offsets are signed hex in dereference order.

## Report

The best chains (expression, module+offset, offsets, levels, verification), the number of restarts survived, the record
id, and the maps and scans still held with their delete calls (`pointer_delete_scan`, `pointer_delete_map`).
See [pointers](../pointers.md).
