# Freeze a value

## Goal

Hold the `{valueType}` at `{address}` at `{value}` (or at its current value when `{value}` is not given) with a Cheat
Engine address-list record described as `{description}` (propose one if not given), verify that it holds and leave
an undo. Single-player or offline software the user may modify only.

## Steps

1. `memory_read(address="{address}", valueType="{valueType}")`: the user confirms this is the value they mean; keep
   `value` as the original. For long text raise `memory_read.length`; `bytes` needs `memory_read.size`, the byte
   count the user confirms.
2. `memory_get_address_info(addresses=["{address}"])`: inside a module, use its `symbol` (module+offset) as the record
   address; a private (heap) region moves after a restart (Decisions).
3. `record_find(address="<symbol or {address}>")`: reuse a match of the same `variableType` (keep its `id`, skip
   step 5).
4. Ask for consent: a `{value}` is written at creation, and an active record rewrites the value every freeze tick
   (100 ms by default).
5. `<code>` is the variableType beside `{valueType}` in the inputs:
   `record_create(records=[{description="{description}", address="<symbol or {address}>", variableType=<code>, value="{value}", length=<n>, unicode=true}])`,
   with `value` only when `{value}` is given (else nothing is written), `length` only for string, wstring or bytes
   without `{value}` (the characters or bytes of step 1) and `unicode` only for wstring. A `{value}` longer than the
   data there overwrites what follows. Keep the `id`.
6. `record_set_active(ids=[<id>], active=true)`: Cheat Engine reads the current value and holds it. Expect `active`
   true; when `pending` is true, read `record_get(ids=[<id>])` again; `active` false with a `failure`: Decisions.
7. Ask the user to trigger the event (take damage, spend ammo) during
   `memory_read_samples(addresses=["{address}"], valueType="{valueType}", durationMs=5000)` (`memory_read` for
   string, wstring and bytes): a `distinctCount` of 1 means the freeze held; `changes` shows brief dips.
8. When finished: `record_set_active(ids=[<id>], active=false)`; if the record was temporary, ask, then
   `record_delete(ids=[<id>])`.

## Decisions

- `active` stays false, with a `failure` such as `inaccessible`, or the record's `value` is `??`: Cheat Engine cannot
  read or write the address; check it against step 2.
- The value still drops or the player still dies (a one-hit kill, a death check between ticks): find the writer with
  [find what writes](find-writer.md), then use a [NOP patch](nop-patch.md) or an [AOB injection](aob-injection.md).
- Heap address: build a [pointer path](pointer-scan.md) or a [manual pointer chain](manual-pointer-chain.md), then
  move the record onto it, offsets as signed hex strings in dereference order, nearest the base first:
  `record_update(updates=[{id=<id>, address="game.exe+...", offsets=["<first>", "<second>"]}])`; `currentAddress` must
  stay the address of step 2.
- `{value}` for a reused record, or another value later: `record_update(updates=[{id=<id>, value="..."}])` writes
  it and Cheat Engine holds it.
- Healing must still work (allow increase), or a hotkey is wanted: no record tool sets these; the user does, in
  Cheat Engine.

## Pitfalls

- A freeze is a periodic write: game logic still runs between ticks, and freezing a display copy changes nothing.
- Records belong to Cheat Engine: they stay, still frozen, after the plugin is disabled, and
  `runtime_release_resources()` ignores them. Record ids expire after a table load.
- A number `{value}` is plain decimal or hex (100, -5, 1.5, 0x64); text such as 1+2 is refused, as Cheat Engine would
  run it as Lua.
- No record tool sets signed display: unless Cheat Engine's "Show values as if they are signed" setting is on, a
  negative value shows as unsigned.
- An error with `hostEffect` started or unknown may have changed state: `record_get(ids=[<id>])` before any retry.

## Report

A table of record id, description, address, `offsets`, `currentAddress`, `variableType`, held and original values and
`active`. Undo: `record_set_active(ids=[<id>], active=false)`, on request the original value through `record_update`,
then `record_delete(ids=[<id>])`. See [cheat tables](../Documents/cheat-tables.md).
