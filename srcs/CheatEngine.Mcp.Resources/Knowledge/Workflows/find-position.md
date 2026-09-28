# Find coordinates and teleport

## Goal

Find the coordinates of {description} as `{valueType}` (auto: `double` on Unreal Engine 5, `float` otherwise), prove
which copy the game obeys, save them and, with consent, teleport. `{address}`, when given, is the X coordinate and
skips the scan. Single-player or offline games only.

## Steps

1. Resolve auto ([identify the engine](engine-triage.md) if unknown): `<type>` below is `float` (4-byte slots) or
   `double` (8-byte slots).
2. With `{address}`: `memory_read(address="{address}", valueType="<type>", count=3)` should give X, Y and Z; go to
   step 6.
3. `scan_get_status()` (main, the default scanner) must show `Created`; ask before `scan_reset()` discards the user's
   results. Then `scan_first(valueType="<type>", comparison="unknown")` and poll `scan_get_status()` until
   `BaselineReady`.
4. Start with height. The user climbs or jumps onto something and stays: `scan_next(comparison="increased")`; steps
   down: `decreased`; stands still: `unchanged`. Poll after each. Up is Y in Unity, Z in Unreal and Source. For a
   horizontal axis, scan `changed` while the user walks.
5. At 64 rows or fewer: `scan_list_results(maximumResults=64)`, then
   `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="<type>", durationMs=5000)` while the user moves; keep
   the rows that follow the movement smoothly.
6. Neighbours: `memory_read(address="<axis - 2 slots>", valueType="<type>", count=8)`. Three consecutive slots that
   change with movement are X, Y and Z. Nearby, a velocity triple is non-zero only while moving, and rotation
   changes when the player turns.
7. Save: `memory_read(address="<X>", valueType="<type>", count=3)`; keep the values.
8. Teleport, with consent and after an in-game save: optionally `process_set_paused(paused=true)`, then
   `memory_write_batch(items=[{address="<X>", valueType="<type>", value="<x>"}, ...], verify=true)` with the saved
   values plus a little height, then `process_set_paused(paused=false)`. The player must appear there and stay.
9. Keep, with consent: `record_create(records=[{description="{description} X", address="<X>", variableType=4}])`
   (variableType 5 for `double`), then Y and Z as its children, which follow X:
   `record_create(records=[{description="{description} Y", address="+4", variableType=4, parentId=<idX>}, ...])`,
   Z at `+8` (`+8` and `+10` for `double`: offsets are hexadecimal).

## Decisions

- Nothing survives the height scans: the axis may grow downward (screen Y does in many 2D games); redo them with
  `increased` and `decreased` swapped. Main drops coordinates beyond 2048 while `scan_get_status().settings` shows
  `simpleValuesOnly` true: ask the user to untick it.
- The player snaps back or nothing moves: that row is a copy (camera, render or interpolated transform, previous
  frame, minimap). Try the next row, or [find what writes](find-writer.md) it: movement or physics code computes the
  real position; copy code moves it from another address.
- Zero the velocity triple in the same write when the player keeps falling. Freezing the height hovers crudely:
  [freeze a value](freeze-value.md) only as a test.
- The object moves after a level load or a restart: anchor X with a [pointer scan](pointer-scan.md), then
  `record_update(updates=[{id=<idX>, address="<base>", offsets=["<o1>", "<o2>"]}])`; Y and Z follow it.
- An emulated console game: follow [emulator memory](emulator-memory.md), since guest RAM can be mapped and
  big-endian.

## Pitfalls

- A target inside geometry falls out of the world or gets stuck: save first and add height. Writes during a cutscene
  or a loading screen are overwritten or can crash the game.
- MCP's pause is a tracked resource: until `process_set_paused(paused=false)` the game stays frozen and a target
  switch or `debugger_detach()` is refused.
- A batch that fails with `partial_effect` kept its earlier items written: read X, Y and Z before writing again.

## Report

A table of axis, address, type, current value and record id; which row the game obeys and how that was proven; the
saved positions. What remains: the records until `record_delete(ids=[<idX>])`, which deletes Y and Z with X, main's
results in Cheat Engine, and MCP's pause if the game was not resumed. See [value scans](../Documents/value-scans.md),
[structures](../Documents/structures.md), [game engines](../Documents/game-engines.md) and
[value types](../Documents/value-types.md).
