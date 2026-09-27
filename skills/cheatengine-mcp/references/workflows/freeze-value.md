# Freeze a value

## Goal

Keep the value at `{address}` (type `{valueType}`) at `{value}`, or at its current value, through a Cheat Engine
address-list record described as `{description}`.

## Steps

1. `memory_read(address="{address}", valueType="{valueType}")`: confirm this is the value the user means.
   `memory_get_address_info(addresses=["{address}"])`: a heap address will move after a restart.
2. `record_find(address="{address}")`: reuse an existing record instead of creating a duplicate.
3. Explain that a freeze makes CE rewrite the value repeatedly, and ask for consent.
4. `record_create(records=[{description:"{description}", address:"{address}", valueType:"{valueType}"}])`; keep the
   record id.
5. When `{value}` is given: `memory_write(address="{address}", valueType="{valueType}", value="{value}")`, keeping
   `previous`.
6. `record_set_active(ids=[<id>], active=true)`; check each record's `outcome` and `active`.
7. Ask the user to trigger the event (take damage, spend ammo), then
   `memory_read(address="{address}", valueType="{valueType}")` to confirm the value held.
8. When finished: `record_set_active(ids=[<id>], active=false)`; if the record was temporary, ask, then
   `record_delete(ids=[<id>])`.

## Decisions

- The value still drops or the player still dies: the freeze loses the race with game logic (a one-hit kill, a death
  check between rewrites). Find the writer with [find what writes](../workflows/find-writer.md), then use
  a [NOP patch](../workflows/nop-patch.md) or an [AOB injection](../workflows/aob-injection.md).
- The address is dynamic: build a [pointer path](../workflows/pointer-scan.md) first and freeze a pointer record, so the
  freeze follows the object.
- The record is a script record: activating it runs Auto Assembler (autoAssembler gate) instead of freezing; check its
  `kind` with `record_get(ids=[<id>])` first.
- `capability_disabled`: report which gate refused; never work around a gate with other tools.

## Pitfalls

- A freeze is a periodic write; the game still runs its logic between writes.
- The address list belongs to CE: records and freezes survive plugin disable, and `runtime_release_resources()` does not
  unfreeze them.
- Freezing a display copy changes nothing; verify that the value really drives the game.
- `record_set_active` can return `partial_effect`: inspect each record before any retry; never repeat a mutation
  blindly.
- String and byte values must keep their length.

## Report

Record id, address, type, frozen value, active state, and how to undo it: `record_set_active(ids=[<id>], active=false)`,
then `record_delete(ids=[<id>])`. The record is CE-owned and stays in the address list until removed; no other resource
is owned. See [cheat tables](../cheat-tables.md).
