# Find a timer or cooldown

## Goal

Find the `{kind}` timer behind {description}; the game shows `{displayedValue}` when it shows a number. Tell the code
that sets it from the code that counts it, then choose a remedy with the user. Writes, the debugger and records need
consent. Single-player or offline games only.

## Steps

1. Predict the storage: seconds as `float` (most often), `double` in GameMaker and JavaScript, `int32` for frames or
   milliseconds (a JavaScript integer can be an `int32` holding 2n); `<type>` below. For unknown, watch the display
   fall (countdown, cooldown) or rise (countup).
2. `scan_get_status()` (main, the default scanner) must show `Created`; ask before `scan_reset()` discards the user's
   results. Then `scan_first(valueType="<type>", comparison="unknown")` and poll `scan_get_status()` until
   `BaselineReady`.
3. While it runs: `scan_next(comparison="decreased")` for a countdown or cooldown, `increased` for a countup; poll,
   and repeat every second or two. While it does not change (a ready cooldown, or a timer that the game's pause menu
   stops): `scan_next(comparison="unchanged")`.
4. With a displayed number, narrow sharply while it runs:
   `scan_next(comparison="between", value="<shown - 1>", upperValue="<shown + 1>")`, ×1000 for milliseconds, ×60
   for frames at 60 FPS.
5. Nothing survives: reset and try the other types. A cooldown can also count up from 0 (`increased` while it runs)
   or store an end time on the game clock, which stays still while it runs and rises at each use: scan `unchanged`
   while it runs and `increased` right after each use. Main drops a clock beyond 2048 while
   `scan_get_status().settings` shows `simpleValuesOnly` true: ask the user to untick it.
6. At 64 rows or fewer: `scan_list_results(maximumResults=64)`, then
   `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="<type>", durationMs=5000)` while it runs: a timer
   changes at a steady rate (1 per second for seconds, 60 for frames) and stops at its limit.
7. Test with consent: `memory_write(address="<timer>", valueType="<type>", value="0")` on a remaining-time cooldown
   should make the ability ready at once. Keep `memory_write.previous`; restore with
   `memory_write(address="<timer>", valueType="bytes", value="<previous>")` unless the change is wanted.
8. The code: [find what writes](find-writer.md) the timer while the user starts it (uses the ability) and lets it
   run. The writer hit once per start sets the full duration; the one hit every frame is the counter here, not a
   display copy: a `subss` or `addss` of the frame time before the store, or a `dec` or `sub` on the field.
9. Keep, with consent: `record_create(records=[{description="{description}", address="<timer>", variableType=4}])`
   (variableType 2 for `int32`, 5 for `double`); keep the record id.

## Decisions

- countdown (a level timer, a fuse): hold it high with [freeze a value](freeze-value.md), or remove the per-frame
  decrement with a [NOP patch](nop-patch.md).
- countup (elapsed time toward a limit): hold it low, or remove the increment.
- cooldown: hold the remaining time at 0 (an elapsed one at its limit), or remove the instruction that sets it on
  use. With an end time, [force the branch](patch-branch.md) of the check that compares it with the clock.
- Enemies or other abilities share the code: [filter shared code](shared-code-filter.md) before patching.
- This timer alone slower or faster: an [AOB injection](aob-injection.md) that multiplies the frame time by a factor
  stored in its allocated memory before the `subss` or `addss` of step 8.
- The whole game slower or faster: the [speedhack](speedhack.md) scales its clocks and the timers they drive.

## Pitfalls

- The display is often a rounded integer copy; the `float` behind it drives the game.
- A freeze rewrites every 100 ms by default: the game can act between rewrites, so a patch is more reliable.
- A timer frozen at its end value can fire its event over and over (a round ending, a buff expiring).

## Report

A table of address, type, kind (remaining time, elapsed time or end time), rate, the setting and counting
instructions and the record id; the remedy chosen and how to undo it. What remains: the record until
`record_delete(ids=[<id>])`, main's results in Cheat Engine. See [value scans](../Documents/value-scans.md),
[code analysis](../Documents/code-analysis.md) and [speedhack](../Documents/speedhack.md).
