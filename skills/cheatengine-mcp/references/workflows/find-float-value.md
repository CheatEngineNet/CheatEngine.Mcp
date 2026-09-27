# Find a floating-point value

## Goal

Find a float or double such as health, stamina, speed or a coordinate. `{displayedValue}` is what the game shows
(optional); `{description}` names it.

## Steps

1. `process_get_current()` and `scan_get_status(scannerName="main")`: main must be idle; ask before
   `scan_reset(scannerName="main")`.
2. With `{displayedValue}`:
   `scan_first(scannerName="main", valueType="float", comparison="between", value="<low>", upperValue="<high>")`, the
   range covering the display rounding (shown 87 means 86.5 to 87.5; shown 87.3 means 87.25 to 87.35). Without it:
   `scan_first(scannerName="main", valueType="float", comparison="unknown")`.
3. Poll `scan_get_status(scannerName="main")` until done.
4. Ask the user to change the value, then
   `scan_next(scannerName="main", comparison="between", value=..., upperValue=...)` around the new display, or
   `"increased"`/`"decreased"` without a display. Poll and repeat until 10 or fewer remain; use `"unchanged"` while
   idle.
5. `scan_list_results(scannerName="main", maximumResults=20)`; read each with
   `memory_read(address=..., valueType="float")`.
6. Positions: once one axis is found, `memory_read(address=<X>, valueType="float", count=3)` usually shows X, Y and Z
   four bytes apart; velocity and rotation often sit next to them. Continue
   with [dissect a structure](../workflows/dissect-structure.md).
7. Ask for consent, then test with `memory_write(address=..., valueType="float", value=...)`, keeping `previous` to
   restore it, and ask the user what changed. With consent,
   `record_create(records=[{description:"{description}", address, valueType:"float"}])`.

## Decisions

- Nothing with float: repeat with `valueType="double"`; many engines and scripting runtimes store doubles.
- A percentage display (75%): try 0.745 to 0.755, then 74.5 to 75.5.
- Still nothing: the value may be an integer scaled by 10 or 100;
  use [find a known value](../workflows/find-known-value.md) with int32. A computed display (speed from a velocity
  vector) has no stored copy; find its inputs instead.
- A bar with no number: [find an unknown value](../workflows/find-unknown-value.md) with float.

## Pitfalls

- Never scan floats with `exact` against a rounded display; the stored value has more precision. The decimals written in
  `value` set the comparison precision.
- A maximum is often stored beside the current value; do not freeze the maximum by mistake.
- Writing an absurd coordinate can drop the player out of the world; ask first and restore `previous`.
- A running main scan blocks target changes; after an uncertain response, check `scan_get_status` before scanning again.
- A write error whose `hostEffect` is not `not_started` or `not_applied`: read the address before any retry.

## Report

Address, float or double, module+offset when static, neighbouring fields noticed, and record id. No MCP resource is
owned: main results remain in CE and the record stays in the address list until `record_delete`.
See [value scans](../value-scans.md) and [structures](../structures.md).
