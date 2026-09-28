# Find what changed in a memory range

## Goal

Find which slots of the `{size}` bytes (at most 16 MiB) at `{address}`, read as `{valueType}`, change when the user
does `{description}` (ask for one action when it is not given), and name them. This only reads the target; snapshots
live in the plugin.

## Steps

1. `memory_get_address_info(addresses=["{address}"])`: the region must be committed and readable; note its module
   or its private (heap) region.
2. `memory_list_snapshots()`: the names below (before, after, idle) must be free, and `totalBytes` must leave room.
3. With the game in the before state: `memory_create_snapshot(name="before", address="{address}", size={size})`;
   note `unreadableBytes`.
4. The user does `{description}` once, and nothing else, then waits for the game to settle.
5. `memory_create_snapshot(name="after", address="{address}", size={size})`.
6. `memory_compare_snapshot(name="before", compareTo="after", valueType="{valueType}")`: `total` of `compared` slots
   changed; each entry of `changes` has `offset`, `before` and `after`. Pass `nextOffset` as
   `memory_compare_snapshot.offset` for the next page.
7. Noise: a few seconds later, without acting, take snapshot idle as in step 5, then
   `memory_compare_snapshot(name="after", compareTo="idle", valueType="{valueType}")`: these slots change on their own
   (timers, animation); drop them from step 6.
8. Direction: when the action lowers the value (damage, spending),
   `memory_compare_snapshot(name="before", compareTo="after", valueType="{valueType}", change="decreased")`; for
   gains, set `memory_compare_snapshot.change` to increased.
9. Confirm: `memory_read_samples(addresses=["<changes[].address>"], valueType="{valueType}", durationMs=5000)` while
   the user repeats the action; the real field changes each time.
10. Interpret: compare the same snapshots again as another type (no memory is read again), or
    `util_convert_value(value="<after>", sourceType="{valueType}", targetType="<type of the same size>")`. With a
    defined structure, `structure_read(name="<structure>", addresses=["{address}"])` names the offsets; otherwise
    [dissect the structure](dissect-structure.md).
11. Clean up: `memory_delete_snapshot(name="before")`, the same for after and idle, then `memory_list_snapshots()`
    shows none of them left.

## Decisions

- Quick look: `memory_compare_snapshot(name="before", valueType="{valueType}")` compares with live memory, but its
  pages shift while the game runs, and it fails with `target_changed` once Cheat Engine selected another target.
- Nothing changed and `skipped` is 0: no compared byte changed, so the value lives elsewhere (or changed back before
  step 5): snapshot more bytes (up to 16 MiB) or find it with [find an unknown value](find-unknown-value.md).
- Changed slots but no direction match: the field may be unaligned or of another type; retry step 8 with
  `memory_compare_snapshot.alignment` 1 or another value type.
- Too many changes: apply the noise and direction filters, repeat the action and keep the slots that change every
  time, or narrow the range.

## Pitfalls

- At most 16 snapshots, 16 MiB each and 64 MiB in total (`limit_exceeded`); a name in use fails with
  `invalid_state`: delete it or choose another.
- A copy reads 1 MiB per dispatch while the game runs, so a large snapshot is not one instant; it fails with
  `target_changed` if Cheat Engine selects another process meanwhile.
- Unreadable bytes are stored as zeros; slots touching them are skipped and counted in `skipped`.
- `increased` and `decreased` order numbers by `{valueType}` with its signedness; NaN never matches.
- Big-endian guest memory (emulators): `changed` still finds the slots, but `before`, `after` and the direction
  filters read little-endian; confirm in step 9 with `memory_read_samples.byteOrder` set to big_endian (refused for
  int8 and uint8, which need none).

## Report

A table of offset, address, before, after, direction, confirmed by sampling (yes or no) and likely meaning; the noise
slots dropped and `skipped`. No target memory was changed. Snapshots left are freed with
`memory_delete_snapshot(name=...)` or when the plugin is disabled. See [structures](../Documents/structures.md),
[value types](../Documents/value-types.md) and [memory model](../Documents/memory-model.md).
