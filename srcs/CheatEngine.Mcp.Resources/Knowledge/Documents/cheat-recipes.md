# Cheat recipes by goal

Read this when the user names a goal (infinite health, money, no cooldown, teleport) rather than a technique. Each
section gives where the value usually lives, the workflows to run in order, how to verify, and the pitfalls.
[plan_cheat](../Workflows/plan-cheat.md) turns one goal into a plan the user approves.

Scope: single-player or offline software the user owns or may modify. Never online, co-op, ranked or leaderboard play,
anti-cheat-protected titles, values kept on a server, or paid content behind a licence or purchase check
([safety](safety.md)). Every recipe changes the target: have the user save the game and copy the save folder first,
then explain each write, freeze, patch, debugger attach or speed change and get consent before it.

## Start every recipe the same way

1. [attach_and_orient](../Workflows/attach-and-orient.md): the right process, the gates (`runtime_get_info.gates`
   shows which of the four gate settings are on), and what earlier work left.
2. [engine_triage](../Workflows/engine-triage.md) when the engine is unknown: it predicts the storage (Unreal Engine 5
   positions and GameMaker numbers are doubles; see [value types](value-types.md)).
3. Unity or .NET: naming the field is often the fastest route and survives game updates:
   [unity_mono_recon](../Workflows/unity-mono-recon.md), [unity_il2cpp_recon](../Workflows/unity-il2cpp-recon.md) or
   [dotnet_recon](../Workflows/dotnet-recon.md). Once `mono_attach()` has injected the collector
   (Mcp:EnableTargetCodeExecution; ask first), `mono_get_object(address="<scan hit>")` names the Mono or IL2CPP object
   that holds a hit and lists its fields: the one at `mono_get_object.offsetInObject` holds the value.
4. To practise first: [ce_tutorial_walkthrough](../Workflows/ce-tutorial-walkthrough.md)
   ([CE tutorial](ce-tutorial.md)). Tutorial step 5 is a NOP patch, step 7 an injection, step 9 shared code (god mode
   for your team only).

## Choose the technique

| Technique | Fits when | Tools | Undo |
|---|---|---|---|
| One write | the value changes rarely (money, items) | `memory_write` | write `memory_write.previous` back as bytes |
| Freeze | the value drains slowly (health, a timer) | `record_create`, `record_set_active` | deactivate the record |
| NOP patch | one instruction lowers the value, and only for the player | `asm_apply_code_patch` | `asm_release_patch` |
| Branch patch | a check decides (ready, unlocked, affordable) | `asm_apply_code_patch` | `asm_release_patch` |
| Injection | shared code, a condition, a multiplier | `asm_generate_injection`, `asm_check`, `asm_apply` | `asm_release_patch` |
| Speedhack | the whole game clock | `speedhack_set_speed` | `speedhack_set_speed(speed=1)` |

- A freeze is an active value record that Cheat Engine rewrites on a timer (every 100 ms by default; CE's "Freeze
  interval" setting). The game runs in between, so one big hit can still kill: then take the code route. A record CE
  cannot freeze does not fail the call: it comes back inactive with a `record_set_active.failure` whose reason says
  why, such as `inaccessible`.
- Scans, one write and a freeze need no gate. [find_writer](../Workflows/find-writer.md) attaches the debugger (ask
  first). Patches and injections need Mcp:EnableAutoAssembler, the speedhack Mcp:EnableTargetCodeExecution.
  `capability_disabled` means a setting is off: report the one it names; never replace a patch with `memory_write` or
  Lua.
- An MCP patch stays until `asm_release_patch` or `runtime_release_resources`, blocks target switching (`busy`) and
  is not saved in a table: see "Keep what works".

## Infinite health, and god mode that spares enemies

- **Stored as:** often a float in Unity and Unreal games, often an int32 elsewhere; a bar without a number needs an
  unknown-value scan. In Unreal games built on the Gameplay Ability System an attribute has a base and a current value
  (floats), and the game recalculates the current one from the base and the active effects, so a write to one can be
  undone: write both and check after the next hit ([Unreal Engine](unreal-engine.md)).
- **Quick:** [find_known_value](../Workflows/find-known-value.md) (or
  [find_unknown_value](../Workflows/find-unknown-value.md), [find_float_value](../Workflows/find-float-value.md)), then
  [freeze_value](../Workflows/freeze-value.md).
- **Robust:** [find_writer](../Workflows/find-writer.md) while the player takes damage; keep the damage writer, not
  the heal or respawn writers. It usually serves every character, so run
  [shared_code_filter](../Workflows/shared-code-filter.md), then [aob_injection](../Workflows/aob-injection.md) that
  skips the write for the player. [nop_patch](../Workflows/nop-patch.md) only when the code writes the player alone:
  `debugger_start_capture(address="<writer address>", trigger="execute", groupByEffectiveAddress=true)` collects one
  item per address the instruction writes (read them with `debugger_poll_capture`), so a single item while both sides
  take hits means one object.
- **Verify:** small and large hits, falls and hazards; enemies must still lose health.

The filter goes above `code:` (the original bytes) in the scaffold from `asm_generate_injection`. In tutorial step 9
(x64) the writer is `movss [rbx+08],xmm0` and the team id is the int32 at `+14`, 1 for your team:

```
newmem:
  cmp dword ptr [rbx+14],1   // your team?
  jne code                   // no: original damage write
  jmp return                 // yes: skip it
code:
  db <original bytes>
  jmp return
```

- A freeze loses to one-shot damage: in gtutorial level 2 the homing bombs deal the player's health plus one.
- Falls, hazards and scripted kills may have their own writers: capture during each.
- A skipped `sub` no longer sets the flags that a following `jle` death check reads, and the filter's `cmp` leaves its
  own (equal, so `jle` is taken). Recreate them, or check that the next original instruction sets its own
  ([x64 injection](x64-injection.md)).
- When the overwritten bytes hold more than the write, run the other instructions on the player path too. A copied
  RIP-relative operand or relative jump breaks in the cave: write its instruction text instead of the bytes.

## One-hit kill and damage multipliers

- **Run:** [find_known_value](../Workflows/find-known-value.md) on an enemy's health (or reuse the player's shared
  damage writer), [find_writer](../Workflows/find-writer.md), [shared_code_filter](../Workflows/shared-code-filter.md),
  then [aob_injection](../Workflows/aob-injection.md) with the change on the enemy path.
- **One-hit kill:** make the damage equal the current health (for `sub [rbx+<hp>],eax`, load `eax` from `[rbx+<hp>]`
  on the enemy path first), so the game's own death logic runs; writing 0 from outside the damage code may skip it.
- **Multiplier:** scale the damage on the enemy path before the original write: `imul eax,eax,#3` (`#` marks decimal;
  plain numbers are hex), or for a float `mulss xmm0,[factor]` with `factor:` and `dd (float)2` after the cave's last
  `jmp return`, declared with `label(factor)` ([Auto Assembler](auto-assembler.md)).
- **Pitfalls:** when the write stores the new health (`mov`) instead of subtracting a damage, the damage was computed
  earlier (crits, armour): find it with [trace_logic](../Workflows/trace-logic.md) and scale it there. Bosses may have
  phases or a second health object. Agree with the user whether allies count as enemies. `cmp` and `imul` change the
  flags; save and restore any register you borrow.

## Infinite ammo

- **Stored as:** an int32 per weapon (sometimes int16, a byte or a float); clip and reserve are separate. The display
  can be derived: gtutorial level 1 shows 5 minus a shot counter that counts up.
- **Quick:** [find_known_value](../Workflows/find-known-value.md) on the clip, one shot between scans, then
  [freeze_value](../Workflows/freeze-value.md).
- **Robust:** [find_writer](../Workflows/find-writer.md) while firing, then [nop_patch](../Workflows/nop-patch.md) on
  the decrement (`dec`, `sub`, or an `inc` or `add` on a shot counter); if enemies fire through it,
  [shared_code_filter](../Workflows/shared-code-filter.md) and [aob_injection](../Workflows/aob-injection.md).
- **Pitfalls:** a fast weapon can fire several shots between two freeze rewrites; the NOP stops every decrement. Leave
  the reload writer alone. Weapons often share one fire routine, so the code route can cover them all.

## Money and resources

- **Stored as:** usually int32, sometimes int64; a double in GameMaker; a double or a tagged integer (2n, 2n+1) in
  JavaScript and RPG Maker games ([tagged numbers](value-types.md#tagged-and-engine-specific-numbers)); sometimes
  scaled (cents) or encoded ([when a scan finds nothing](troubleshooting-scans.md)).
- **Run:** [find_known_value](../Workflows/find-known-value.md), earning or spending between scans, then one write with
  consent, `memory_write(address="<address>", valueType="int32", value="50000")`; keep `memory_write.previous`, and
  add a record to set it again.
- **Free purchases:** [find_writer](../Workflows/find-writer.md) while buying, then
  [nop_patch](../Workflows/nop-patch.md) on the subtraction; every cost through that code becomes free.
- **Pitfalls:** keep the address the purchase writes, not a HUD or profile copy. Stay well below the game's cap and the
  type's maximum (2147483647 for int32): a later gain can wrap it negative and corrupt a save. A level load may copy
  the value back from a profile object. Server-side economies and real-money currencies are out of scope.

## No cooldown and frozen timers

- **Stored as:** seconds counting down to 0 (often a float), elapsed time counting up to a duration, a frame counter,
  or an end time, fixed when the cooldown starts and compared with a game clock.
- **Run:** [find_timer](../Workflows/find-timer.md), then [freeze_value](../Workflows/freeze-value.md) at the ready
  value (0 for a countdown, the duration for a count-up). For an end time, or when the freeze loses:
  [find_writer](../Workflows/find-writer.md) and [nop_patch](../Workflows/nop-patch.md) on the store that starts the
  cooldown, or [trace_logic](../Workflows/trace-logic.md) and [patch_branch](../Workflows/patch-branch.md) on the
  ready check.
- **Pick the candidate:** `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="float", durationMs=3000)` while
  the cooldown runs; the timer changes steadily and stops at the ready value.
- **One timer slower or faster**, not the whole game: [find_writer](../Workflows/find-writer.md) on the timer, then
  [aob_injection](../Workflows/aob-injection.md) that scales the frame step before the subtraction or addition
  (`mulss` with a factor stored in the cave, as for the damage multiplier above).
- **Pitfalls:** cooldown code usually serves every ability of every character, so an unfiltered patch frees enemies
  too. Mana and charges are separate values. A level timer may share its decrement with other timers: freeze its
  address instead. The [speedhack](../Workflows/speedhack.md) above 1 runs every clock-based timer down faster, and
  the rest of the game with it.

## Teleport and saved positions

- **Stored as:** usually three consecutive floats (doubles in Unreal Engine 5). Up is Y in Unity, Z in Unreal and
  Source: jump to find it.
- **Run:** [find_position](../Workflows/find-position.md). Save with
  `memory_read(address="<x>", valueType="float", count=3)` and keep `memory_read.values`. Load with consent:
  `process_set_paused(paused=true)`, then
  `memory_write_batch(items=[{address="<x>", valueType="float", value="<x0>"}, {address="<x>+4", valueType="float", value="<y0>"}, {address="<x>+8", valueType="float", value="<z0>"}], verify=true)`,
  then `process_set_paused(paused=false)`. In UE5 use `double` at +8 and +16. MCP tracks its pause as a resource
  (`process_set_paused.resourceId`) that blocks target switching until the game is resumed; resume right after the
  writes.
- Anchor the object with [pointer_scan](../Workflows/pointer-scan.md) or
  [injection_copy_base](../Workflows/injection-copy-base.md) so the position survives a level load.
- **Pitfalls:** the physics body, render transform and camera can each hold a copy, and writing a copy snaps back;
  keep the one that moves the character. Add a little height: a fall can kill. Skipped triggers can break a quest or
  strand the player: save first.

## Game speed

- **Whole game:** the [speedhack](../Workflows/speedhack.md) workflow, `speedhack_set_speed(speed=0.5)`, later
  `speedhack_set_speed(speed=1)`; it needs Mcp:EnableTargetCodeExecution. The first speed other than 1 activates the
  speedhack (`speedhack_set_speed.firstActivation` true): Cheat Engine hooks the process, and the hooks stay until the
  game exits; speed 1 never removes them. Once `speedhack_set_speed.hooksInstalled` is true, CE never hooks that
  process again, even after a failed hook; while it is false, a later call tries again. On Unity, CE loads its Mono
  data collector and hooks `Time.set_timeScale` instead of the Windows clocks; each speed change then sets the time
  scale, overriding a pause or slow motion the game had set ([speedhack](speedhack.md)).
- **Only the player:** a movement speed or multiplier, often a float:
  [find_float_value](../Workflows/find-float-value.md) while sprinting, then one write or
  [freeze_value](../Workflows/freeze-value.md).
- **Pitfalls:** speed 0 is refused (0.01 to 1000); pause with `process_set_paused(paused=true)` instead. Resume before
  changing the speed: a change, a restore to 1 included, is refused while the game is paused or stopped in the
  debugger. High factors break physics. A speed other than 1 blocks target switching until it is set back to 1 or
  released.

## Toggles and feature flags

- **Stored as:** a byte 0 or 1 (C++ `bool`), an int32 (Win32 `BOOL`), one bit of a flags field, or an enum.
- **Run:** [find_flag](../Workflows/find-flag.md), toggling several times, then one write or
  [freeze_value](../Workflows/freeze-value.md). When the game recomputes it: [find_writer](../Workflows/find-writer.md)
  with the access trigger, [trace_logic](../Workflows/trace-logic.md), then [patch_branch](../Workflows/patch-branch.md)
  on the jump that tests it. An on-screen message ("Locked") leads to the check via
  [find_code_by_string](../Workflows/find-code-by-string.md).
- **Pitfalls:** 0 and 1 match everywhere, so toggle many times. In a flags field change only that bit. A branch patch
  affects every caller.

## Unlocks and items

- **Stored as:** per-item flags (bytes or bits), counts in an inventory array, or item ids; progress often sits in a
  profile object the game saves.
- **Run:** [find_flag](../Workflows/find-flag.md) for one item, or [find_known_value](../Workflows/find-known-value.md)
  for a count; [dissect_structure](../Workflows/dissect-structure.md) or
  [compare_snapshots](../Workflows/compare-snapshots.md) map the neighbours; [group_scan](../Workflows/group-scan.md)
  finds an item by several fields. Code route: [find_code_by_string](../Workflows/find-code-by-string.md), then
  [patch_branch](../Workflows/patch-branch.md) on the unlock check.
- **Pitfalls:** the game saves what you change, and some unlocks cannot be undone: back up the save. An invalid item
  id or a count above the cap can crash the game or corrupt a save. Paid DLC, achievements and online unlocks are out
  of scope.

## Other goals

- Unknown mechanics: [compare_snapshots](../Workflows/compare-snapshots.md) around a known object; many similar
  objects: [find_entity_list](../Workflows/find-entity-list.md); engine specifics: [game engines](game-engines.md).
- A game in an emulator: [emulator_memory](../Workflows/emulator-memory.md). Guest RAM is often mapped memory, which a
  named scanner reaches with `scan_first.includeMapped`, and console values are often big-endian, which
  `memory_read.byteOrder` decodes ([emulators](emulators.md)).
- No tool covers it: [write_lua_script](../Workflows/write-lua-script.md) (Mcp:EnableUnsafeLua), with the user's
  review.

## Keep what works

- [build_robust_table](../Workflows/build-robust-table.md): a code cheat becomes an Auto Assembler record found by
  signature ([make_aob_signature](../Workflows/make-aob-signature.md)); a data cheat becomes a pointer record whose
  offsets are signed hexadecimal strings in dereference order, nearest the base first, as `pointer_list_paths`
  returns them:
  `record_create(records=[{description="Health", address="game.exe+1A2B30", variableType=4, offsets=["10","4C8"]}])`.
  Without a value the create writes nothing; record reads return the offsets in the same form.
- A value with a few named states (a toggle, a difficulty, an item id) can get a dropdown:
  `record_set_dropdown(id=<id>, items=[{value="0", description="Off"}, {value="1", description="On"}])`.
- Then `table_save(path="<absolute path under an allowed table root>/game.CT")` ([cheat tables](cheat-tables.md)). The
  roots come from the CheatEngineClient:AllowedTableRoots setting, empty by default, so every save is refused until
  the user adds one; an existing file also needs `table_save.overwrite` true.
- After a game update: [repair_after_update](../Workflows/repair-after-update.md).

## Undo and clean up

[cleanup_session](../Workflows/cleanup-session.md) has the full order. In short, with consent for each step:

1. `runtime_list_resources()` lists what MCP holds: patches, running jobs, MCP's pause and a speed other than 1.
2. `runtime_stop_job(jobId="<jobId>")` for each job still running.
3. `record_set_active(ids=[<id>], active=false)` ends freezes and script records (an Auto Assembler record needs
   Mcp:EnableAutoAssembler).
4. `asm_release_patch(patchId="<patchId>")`, newest first, restores the bytes.
5. `process_set_paused(paused=false)`, then `speedhack_set_speed(speed=1)`: a speed change is refused while the game
   is paused. The speedhack hooks stay until the game exits.
6. Records belong to Cheat Engine's address list, and `runtime_release_resources()` leaves them: delete them only
   with consent, `record_delete(ids=[<id>])`.
7. `runtime_release_resources()` for whatever MCP still holds, then `debugger_detach()` if the user wants the debugger
   gone; it is refused (`busy`) while MCP still holds a resource.

- Undo a write with `memory_write(address="<address>", valueType="bytes", value="<previous>")` unless the game has
  overwritten it since.
- After an error whose `hostEffect` is `started`, `cleanup_unconfirmed` or `unknown`, inspect (`asm_list_patches()`,
  `record_get(ids=[<id>])`) before any retry ([errors and recovery](errors-and-recovery.md)).

## Sources

- https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine/Tutorial (Unit6, Unit8 and Unit10.pas for
  steps 5, 7 and 9; `graphical/gametutorial1.pas` and `gametutorial2.pas` for levels 1 and 2)
- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine (MainUnit.lfm: `FreezeTimer` interval 100)
- Cheat Engine 7.7 `autorun/SpeedhackV3.lua` (hooks skipped once its symbol exists, Unity `set_timeScale` path)
- https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-attributes-and-attribute-sets-for-the-gameplay-ability-system-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/large-world-coordinates-in-unreal-engine-5
- https://docs.unity3d.com/ScriptReference/Time-timeScale.html
