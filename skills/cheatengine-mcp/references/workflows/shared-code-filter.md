# Filter shared code by entity

## Goal

Make the code at `{instructionAddress}` affect only the intended entities (for example the player, at `{playerAddress}`
when known, and not enemies) by finding a field or register that tells them apart.

## Steps

1. `debugger_get_status()`; when not attached, ask for consent, then `debugger_attach(interface=0)`.
2. `code_decode(address="{instructionAddress}")`: read `memoryOperand` (base register, index and displacement).
3. `debugger_start_capture(address="{instructionAddress}", trigger="execute", lifetimeSeconds=60)`; keep the returned
   `jobId`. An execute breakpoint stops before the instruction runs, so the registers are exact.
4. Ask the user to play so the player and several other entities pass through the code (take damage, hit enemies).
5. `debugger_poll_capture(jobId=..., afterSequence=0, limit=100)`, passing back `nextAfterSequence` until `more` is
   false. For each hit compute the entity base from the memory operand's registers and group the hits by base. Then
   `runtime_stop_job(jobId=...)`.
6. Identify the player base (the one matching `{playerAddress}` minus the displacement, or the one that changes when the
   user acts) and at least two other bases.
7. `structure_autoguess(name="Entity", address="<player base>", size=1024, createIfMissing=true)`.
8.
`structure_compare(groupA=["<player base>"], groupB=["<base 2>", "<base 3>"], structureName="Entity", mode="discriminate")`:
fields constant within each group but different between them (team id, player flag, owner pointer).
9. Other discriminators: class names from `memory_get_address_info(addresses=[...], includeRtti=true)`, a stored player
   base from an [injection copy](../workflows/injection-copy-base.md), or a register that differs per caller.
10. Check the candidate on at least two allies and two enemies, and again after a restart.
11. Build the filtered script with [AOB injection](../workflows/aob-injection.md): in the new code compare the field
    (`cmp dword ptr [<reg>+<field>],<value>`) and jump straight to the original code for entities that must stay
    unchanged.
12. If the structure was temporary, ask, then `structure_delete(name="Entity")`.

## Decisions

- A single base in all hits: the code is not shared; no filter is needed.
- No stable field: filter by comparing the base with the stored player base.
- Allies and enemies may need different effects (god mode for the team, one-hit kills for enemies); agree on this with
  the user first.

## Pitfalls

- RTTI names exist only for C++ classes compiled with RTTI.
- A field that looks like a team id may be an unrelated counter; confirm it across restarts.
- `cmp` changes flags; preserve the flags and registers the original code relies on.
- Captures are jobs; stop them before switching target. Structures belong to CE and outlive the plugin.
- Applying the script is a mutation: ask for consent, and after an error whose `hostEffect` is not `not_started` or
  `not_applied`, inspect `asm_list_patches()` before retrying.

## Report

The discriminator (offset, type, values per group), how it was verified, the patch id if a script was applied, and the
structures, captures or patches still owned with their release calls.
See [structures](../structures.md), [auto assembler](../auto-assembler.md) and [debugger](../debugger.md).
