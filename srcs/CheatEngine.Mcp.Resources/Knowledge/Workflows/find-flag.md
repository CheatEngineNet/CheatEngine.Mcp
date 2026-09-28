# Find a boolean flag or toggle

## Goal

Find the flag behind {description}, which is `{state}` in game now: scan it as `{width}` (auto: `byte`, then `int32`)
while the user toggles it, prove it, then read its bits and the code that tests it. Writes, the debugger and records
need consent; single-player or offline games only.

## Steps

1. `scan_get_status()` (main, the default scanner) must show `Created`; when it holds the user's results, ask before
   `scan_reset()`.
2. `<type>` below is the resolved `{width}`. Scan the current state, 1 for on and 0 for off; 0 matches far more
   bytes, so let the user switch it on first when possible:
   `scan_first(valueType="<type>", comparison="exact", value="<1 or 0>")`, then poll `scan_get_status()` until
   `isScanning` is false.
3. The user toggles the flag and waits a moment; `scan_next(comparison="exact", value="<new state>")`, then poll.
   Repeat for five or six toggles. While nothing is toggled, `scan_next(comparison="unchanged")` drops noise.
4. At 64 rows or fewer: `scan_list_results(maximumResults=64)`, then
   `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="<uint8 or int32>", intervalMs=50, durationMs=5000)`
   while the user toggles twice: the flag's `changes` follow each toggle.
5. Nothing left with `byte`: reset and repeat as `int32` (Win32 `BOOL`, enums). Still nothing: reset, then
   `scan_first(valueType="int32", comparison="unknown")`, `scan_next(comparison="changed")` after each toggle and
   `unchanged` between toggles; this also finds bit fields and encodings such as Ruby's true 2 or Flash's true 13.
6. Bits: `memory_read(address="<flag>", valueType="<uint8 or uint32>")` in each state, then
   `util_calculate(expression="<on> ^ <off>")`: the `hex` result is the mask. A mask other than 1, or other bits set
   in both states, means one bit of a bit field (its other bits are other flags) or a tagged encoding from step 5.
7. Test with consent: `memory_write(address="<flag>", valueType="<uint8 or uint32>", value="<current ^ mask>")`, with
   `util_calculate` on a fresh read, keeping `memory_write.previous`; restore with
   `memory_write(address="<flag>", valueType="bytes", value="<previous>")` unless the change is wanted.
8. The code that tests it: [find what writes](find-writer.md) with trigger `access` and size 1 (4 for `int32`: code
   may test any of its bytes), which gets consent for the debugger and stops its capture. For each instruction that
   reads the flag, `code_disassemble(address="<instructionAddress>", count=8)` shows the compare or test and its
   conditional jump.
9. Keep it, with consent: `record_create(records=[{description="{description}", address="<flag>", variableType=0}])`
   (variableType 2 for `int32`); keep the record `id`. Name its states in Cheat Engine's value editor:
   `record_set_dropdown(id=<id>, items=[{value="<off>", description="off"}, {value="<on>", description="on"}])`.

## Decisions

- Nothing survives: the meaning may be inverted (god mode on may be "can take damage" 0); reset and rescan with 1 and
  0 swapped before step 5. `scan_get_status().settings` shows the range and protection options main scans with.
- Several rows follow the toggles: HUD icons and menu checkboxes copy the real flag. Test one at a time; only the
  real flag changes gameplay, and a copy reverts or changes only the display.
- More than two states (0, 1, 2 …) is a mode or an enumeration: list every value in the step 9 dropdown instead of
  forcing 0 or 1.
- Hold the state: [freeze a value](freeze-value.md) when the game only reads the flag; when it rewrites or recomputes
  it every frame, [force the branch](patch-branch.md) found in step 8.
- The address changes after a restart or a level load: anchor it with a [pointer scan](pointer-scan.md).

## Pitfalls

- 0 and 1 are the most common bytes: early rounds keep millions of rows. Never list them; toggle more.
- Scan only after the state has settled: a toggle can wait for a frame, an animation or a closed menu.
- Writing or freezing the whole byte or `int32` of a bit field also pins its other flags: flip only the mask bit. The
  record tools set no bit range, so the step 9 record holds the whole value: never freeze it, and skip the dropdown.
- A flag is often read every frame, and each access hit briefly stops the game's threads: keep that capture short.

## Report

A table of address, type (`byte`, `int32` or bit mask), on and off values, proof (toggles, write test, testing
instruction) and record id. What remains: the record and its dropdown until `record_delete(ids=[<id>])`, the debugger
until `debugger_detach()`, main's results in Cheat Engine. See [value scans](../Documents/value-scans.md),
[code analysis](../Documents/code-analysis.md) and [value types](../Documents/value-types.md).
