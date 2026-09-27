# Find a known value

## Goal

Find the address that holds a value the user can read on screen: `{currentValue}`, type `{valueType}` (default auto),
described as `{description}`.

## Steps

1. `process_get_current()`: confirm the target. `scan_get_status(scannerName="main")`: if main is busy, poll until it is
   idle; if it holds results the user may want, ask before `scan_reset(scannerName="main")`, or use a named scanner such
   as `scannerName="value1"`.
2. Choose the type: `{valueType}`, or for auto: int32 for whole numbers, float for numbers with decimals.
3. `scan_first(scannerName="main", valueType="<type>", comparison="exact", value="{currentValue}")`. Add
   `writable="required"` to skip code and constants. Numeric values are decimal text.
4. Poll `scan_get_status(scannerName="main")` until it stops scanning; read `count`.
5. Ask the user to change the value in game (take damage, spend money) and tell you the new number.
6. `scan_next(scannerName="main", comparison="exact", value="<new value>")`, then poll again. Repeat steps 5-6 until 10
   or fewer results remain. If the user cannot read the new number, use `comparison="decreased"`, `"increased"` or
   `"changed"`, and `"unchanged"` while nothing happens.
7. `scan_list_results(scannerName="main", maximumResults=20)`, then `memory_get_address_info(addresses=[...])`: a module
   address is static, a heap address is dynamic.
8. Ask for consent, then test one candidate at a time:
   `memory_write(address=..., valueType="<type>", value="<distinct test value>")`, keep `previous`, and ask the user
   whether the screen changed. Restore `previous` with `memory_write` when it did not.
9. With consent, `record_create(records=[{description:"{description}", address, valueType}])`; keep the returned record
   id.

## Decisions

- No results: try float then double with `comparison="between"` (value-0.5 to value+0.5), then int16 or int64, then
  scaled values (x10, x100, /100, a 0..1 float for a percentage). Still nothing: the value may be derived or obfuscated;
  switch to [find an unknown value](../workflows/find-unknown-value.md).
- Several addresses change together: display copies. The real one is written by game logic; confirm
  with [find what writes](../workflows/find-writer.md).
- A test write reverts at once: the game recomputes it from another field; find its writer.
- The address moves after a restart or level change: make it stable with a [pointer scan](../workflows/pointer-scan.md)
  or an [injection copy](../workflows/injection-copy-base.md).

## Pitfalls

- Main mirrors CE's visible scan tab; CE UI options (range, fast scan alignment) apply, and `uiOptionsChanged` lists
  what was changed. A running main scan blocks target changes.
- After an uncertain `scan_first` or `scan_next` response, call `scan_get_status` before starting again; never repeat a
  scan blindly.
- Strings: `string` is single-byte text, `wstring` is UTF-16.
- A `memory_write` error whose `hostEffect` is not `not_started` or `not_applied` may already have written: read the
  address before anything else.

## Report

Address, type, module+offset when static, current value, record id, and the scanner used. Owned state: a named scanner
is released with `scan_delete(scannerName=...)`; the record stays in CE's address list until `record_delete`; main
results stay in CE. See [value scans](../value-scans.md), [cheat tables](../cheat-tables.md)
and [CE tutorial](../ce-tutorial.md).
