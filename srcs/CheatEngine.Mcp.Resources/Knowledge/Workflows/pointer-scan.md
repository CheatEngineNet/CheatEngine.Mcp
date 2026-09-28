# Find a static pointer with the pointer scanner

## Goal

Find pointer paths from a module to the value at `{address}` that survive restarts, with at most `{maxDepth}` levels
(default 5) and offsets up to `{maxOffset}` (default 4096, at most 1048576). Single-player or offline software the
user may modify only.

## Steps

1. `memory_read(address="{address}", valueType="<type>")`: the user confirms the value.
   `memory_get_address_info(addresses=["{address}"])`: an address with a `module` is already static; stop.
2. Optional, with consent: `process_set_paused(paused=true)`, so the map is one consistent snapshot (a tracked
   resource until step 3 resumes).
3. `pointer_create_map(mapName="run1", maxBytes=536870912, maxPointers=4194304, lifetimeSeconds=300)` starts a job
   (the defaults stop at 64 MiB). Poll `pointer_list_maps()` until its `state` is ready; note `incomplete`. Then
   `process_set_paused(paused=false)` if you paused.
4. `pointer_find_paths(scanName="scan1", mapName="run1", target="{address}", maxDepth={maxDepth}, maxOffset={maxOffset})`
   starts a job. Poll `pointer_list_scans()` until ready; note `count`, `incomplete` and `traversalLimited`.
5. `pointer_list_paths(scanName="scan1", limit=50)`: fewest levels first.
6. If CE or the plugin will restart, save with `pointer_save_map` and `pointer_save_scan`, then restore with
   `pointer_load_map` and `pointer_load_scan`. Rescan the restored paths in step 9. See
   [pointers](../Documents/pointers.md#save-restart-and-reopen).
7. The user closes the game completely and starts it again (the main menu is not enough).
   `process_attach(process="<exe name>")`: maps and scans survive the switch.
8. Find the value again ([find a known value](find-known-value.md)) and confirm it: a wrong address removes the
   good paths for good.
9. `pointer_rescan_paths(scanName="scan1", target="<new address>")` keeps the paths that still reach it; read
   `count`, `verified`, `removed` and `unresolved`.
10. Repeat 7-9 two or three times, also after a level load, until `count` stops falling.
11. `pointer_list_paths(scanName="scan1", moduleContains="<main module>")`: prefer `verification` live_match,
    fewer levels and small offsets.
12. Check the best three: `pointer_read_chain(base="<module>+<moduleOffset>", offsets=[...], valueType="<type>")`;
    `address` must be the value's current address.
13. With consent:
    `record_create(records=[{description="...", address="<module>+<moduleOffset>", variableType=2, offsets=["<first>", "<second>"]}])`:
    the path's `offsets` as listed; `variableType` 2 int32, 4 float, 5 double; no `value`, so nothing is written.
    Keep the `id`; `currentAddress` must be the value's address.
14. Clean up every scan and map you made: `pointer_delete_scan(scanName="scan1")`,
    `pointer_delete_map(mapName="run1")`.

## Decisions

- `count` 0 and the map stopped at a limit: it is read upward, so x64 module roots at the top were likely cut.
  `pointers` at `maxPointers` on x64: map again keeping fewer, then search that map:
  `pointer_create_map(mapName="run2", maxBytes=536870912, maxPointers=4194304, alignment=8)`.
- `count` 0 from a complete map: raise the offset before the depth (Unity games often need 16384):
  `pointer_find_paths(scanName="scan2", mapName="run1", target="{address}", maxOffset=16384)`.
- Still no path: a [manual chain](manual-pointer-chain.md), an [injection copy](injection-copy-base.md) or managed
  statics ([Unity Mono recon](unity-mono-recon.md)).
- `traversalLimited`: scan again with larger `pointer_find_paths.maxNodes` and `pointer_find_paths.maxResults`.
- Hundreds of paths left: more restarts, or
  `pointer_rescan_paths(scanName="scan1", target="<new address>", dropUnresolved=true)`.
- The writer's offset is known ([find what writes](find-writer.md)): keep the paths whose last offset matches it.

## Pitfalls

- `incomplete` or `traversalLimited` with no path proves nothing; `unresolved` paths are not verified.
- No thread-stack roots: a value reached only from a stack needs another method.
- At most 4 maps (8388608 pointers in all) and 16 scans, by unique case-sensitive name, in plugin memory until
  deleted or the plugin is disabled.
- A running map or scan job makes `process_attach` fail with `busy`: wait for ready, or
  `runtime_stop_job(jobId=...)` keeps what it found.
- `monoAutoAttach` true from `process_attach`: the table's Uses Mono option lets Cheat Engine inject its Mono
  collector again.
- Offsets are in dereference order; Cheat Engine's address list shows them reversed. Quote a module name with `-`:
  `"Tutorial-x86_64.exe"+346CA0`.

## Report

A table of the best paths: expression, module+offset, offsets, levels, verification; the restarts survived and the
record id. Maps and scans still held (`pointer_delete_scan`, `pointer_delete_map`); the record stays until
`record_delete(ids=[<id>])`. See [pointers](../Documents/pointers.md).
