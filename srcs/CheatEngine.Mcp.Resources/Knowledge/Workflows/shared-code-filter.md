# Filter shared code by entity

## Goal

Find a field or class that tells apart the objects the instruction at `{instructionAddress}` handles, such as the
player (value at `{playerAddress}` when known) and the enemies, so an injection there affects only the intended ones.
This workflow reads and debugs; [AOB injection](aob-injection.md) applies the filtered script.

## Steps

1. `code_decode(address="{instructionAddress}")`: in `opcode`, such as `mov [rbx+000004C8],eax`, note the base
   register (RBX) and the displacement (4C8); with an index (`[rbx+rcx*8+10]`) note both registers.
2. `debugger_get_status()`. When `attached` is false, explain that Cheat Engine's debugger attaches to the game, get
   consent, then `debugger_attach(interface="windows")`.
3. Capture the addresses it accesses, one item per address:
   `debugger_start_capture(address="{instructionAddress}", trigger="execute", groupByEffectiveAddress=true, maximumHits=256, lifetimeSeconds=60)`;
   keep `jobId`.
4. Ask the user to make the player and several other entities go through the code (take damage, hit enemies).
5. `debugger_poll_capture(jobId=..., afterSequence=0, limit=1000)`, passing back `nextAfterSequence` until `more` is
   false, then `runtime_stop_job(jobId=...)`. Each item's base is `effectiveAddress` minus the displacement; with an
   index, read the base register in its `context.registers` (taken before the instruction runs).
6. The player's item is the one whose `effectiveAddress` equals `{playerAddress}` resolved, or the one seen when only
   the player acts. Pick at least two other bases.
7. `structure_compare(groupA=["<player base>"], groupB=["<base 2>", "<base 3>"], size=1024, limit=128)`:
   `discriminator` rows are constant inside each group and differ between them (team id, player flag); `nextOffset`
   pages the rest. Put allies in `groupA` to filter by team.
8. `memory_get_address_info(addresses=["<player base>", "<base 2>"], includeRtti=true)`: a different `rttiClass`
   means the vtable pointer at offset 0 differs.
9. Confirm the field on fresh entities, after a level change and after a restart: repeat steps 3 to 7, or
   `memory_read(address="<base>+<offset>", valueType="int32")` on each base.
10. Offer `debugger_detach()` now: it refuses while this server holds any job, patch or other resource.
11. With consent, run [AOB injection](aob-injection.md) at `{instructionAddress}`: the cave starts with
    `cmp dword ptr [rbx+<offset>],<player value>` and `jne code`, so other entities run only the original code.

## Decisions

- One item only: the code is not shared; inject without a filter.
- The capture is refused (a `lea`, no or several memory operands, `fs:`/`gs:`): capture without
  `debugger_start_capture.groupByEffectiveAddress` and group the hits by the base register's value.
- No stable field: compare the base register with a stored player base
  ([capture a base pointer](injection-copy-base.md)).
- Allies and enemies need different effects (god mode for the team, one-hit kills for enemies): agree on them with
  the user; each effect gets its own branch.
- No items: the code did not run; have the user trigger it and start a new capture. `dropped` above 0 means more
  objects than `maximumHits`: the oldest were evicted.
- `debugger_detach` answers `busy`: find what is still held with `runtime_list_resources()` and release it first.

## Pitfalls

- An execute breakpoint on code that runs every frame slows the game: keep `lifetimeSeconds` short and stop the job
  once the hits are in.
- RTTI names exist only for C++ classes compiled with RTTI. A vtable is a module address that moves each run and, on
  x64, is too large for a `cmp` constant: load `<module>+<offset>` into a saved register and compare with it.
- A field that looks like a team id may be a counter or a state, and pointers change every run: confirm across
  restarts, and never compare with a fixed pointer.
- `cmp` changes flags: save them with `pushfq`/`popfq` when the code after the site reads them.
- Captures are jobs: stop them before switching target.

## Report

The base register and displacement, the discriminator (offset, size, values per group, number of samples, how it was
confirmed), the patch id if the injection was applied, and what still runs: capture jobs (`runtime_stop_job`), the
debugger (`debugger_detach`), the patch (`asm_release_patch`). Background: [structures](../Documents/structures.md),
[Auto Assembler](../Documents/auto-assembler.md) and [debugger](../Documents/debugger.md).
