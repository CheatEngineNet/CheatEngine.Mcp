# Plan a cheat from a goal

## Goal

Turn the goal `{goal}`, in the user's words {description}, into a plan the user approves: the data or code route, the
workflows to run in order, how to verify the result and the robust form to keep. Planning changes nothing; each
workflow it starts asks consent for its own changes.

## Steps

1. Scope: the game must be single-player or offline and free of anti-cheat, and the value must live on this machine,
   not on a server (online currencies, ranked stats). Otherwise stop ([safety](../Documents/safety.md)).
2. `runtime_get_overview()`: `process.isOpen` false means no target: run [attach and orient](attach-and-orient.md).
   `runtime_get_overview.runtime.gates` shows which Mcp:Enable… switches are on: plan with those. A non-zero
   `resourceCount` or `jobCount` means earlier work remains: `runtime_list_resources()` shows it.
3. Engine unknown: [identify the engine](engine-triage.md); it predicts the value type (UE5 positions and GameMaker
   numbers are doubles). Unity or .NET: naming the field is often the fastest, most robust route:
   [Unity Mono](unity-mono-recon.md), [Unity IL2CPP](unity-il2cpp-recon.md) or [.NET](dotnet-recon.md) recon. On Unity,
   once the collector is attached, `mono_get_object(address="<scan hit>")` names the object and field holding a hit.
4. Read the section for `{goal}` below and present the plan: the data route first when it fits (no gate, easy to
   undo), the code route when a freeze loses or the game recomputes the value. For each workflow say what it changes,
   the gate it needs and how it is undone; suggest a save backup; let the user choose.
5. Run the chosen workflows in order. Keep every id they return: record ids, `patchId`, `jobId`, scanner names.
6. Verify as the section says, then make it survive a restart: [build a robust table](build-robust-table.md).

## Decisions

- Gates: scans, one write and a freeze (an active value record) need none; [find what writes](find-writer.md)
  attaches the debugger (ask first). Patches, injections, the test patch in [trace the logic](trace-logic.md) and the
  robust table's script check need Mcp:EnableAutoAssembler; the speedhack and a Mono attach
  Mcp:EnableTargetCodeExecution; a Lua script Mcp:EnableUnsafeLua. A switch turned on applies after a plugin re-enable.
- `capability_disabled`: report the setting its `hint` names; never replace a patch with a raw write or Lua.
- A freeze rewrites the value about every 100 ms (Cheat Engine's default), so one large hit can still kill: take the
  code route then.
- No scan result: recheck the type (step 3) or switch route. Other failures: [explain the error](explain-error.md).

### goal: infinite_health

- Storage: often a float in Unity and Unreal games, often an int32 elsewhere; a bar without a number needs an unknown
  scan.
- Data route: [find a known value](find-known-value.md) (or [an unknown value](find-unknown-value.md),
  [a float](find-float-value.md)), then [freeze it](freeze-value.md).
- Code route: [find what writes](find-writer.md) while the player takes damage; keep the damage writer, not the heal
  or respawn writers. That code usually serves every character: [filter shared code](shared-code-filter.md), then
  [an AOB injection](aob-injection.md) that skips the write for the player only. [A NOP patch](nop-patch.md) only
  when the code writes the player alone.
- Verify: small and large hits, falls and hazards; enemies must still lose health.

### goal: infinite_ammo

- Storage: an int32 per weapon (sometimes int16, byte or float); clip and reserve are separate, and the display may be
  derived from a shot counter.
- Data route: [find a known value](find-known-value.md) on the clip, one shot between scans, then
  [freeze it](freeze-value.md).
- Code route: [find what writes](find-writer.md) while firing, then [a NOP patch](nop-patch.md) on the decrement; when
  enemies fire through the same code, [filter shared code](shared-code-filter.md) and
  [an AOB injection](aob-injection.md).
- Verify: fire several weapons and reload; leave the reload writer alone.

### goal: currency

- Storage: usually int32, sometimes int64; a double in GameMaker; a double or a tagged integer (2n, 2n+1) in
  JavaScript and RPG Maker games; sometimes scaled (cents).
- Data route: [find a known value](find-known-value.md), earning or spending between scans, then one write with
  consent, `memory_write(address="<address>", valueType="int32", value="50000")`; keep `memory_write.previous` to undo
  it.
- Code route for free purchases: [find what writes](find-writer.md) while buying, then [a NOP patch](nop-patch.md) on
  the subtraction.
- Verify: buy something and change area. Stay well below the game's cap and the type's maximum (2147483647 for int32):
  a later gain can wrap it negative and corrupt a save. Server-side and real-money currencies are out of scope.

### goal: one_hit_kill

- Route: [find a known value](find-known-value.md) on an enemy's health (or reuse the player's damage writer),
  [find what writes](find-writer.md), [filter shared code](shared-code-filter.md), then
  [an AOB injection](aob-injection.md) on the enemy path that makes the damage equal the current health, so the game's
  own death logic runs.
- A damage multiplier scales the input before the write; when crits or armour compute it earlier,
  [trace the logic](trace-logic.md).
- Verify: normal enemies and bosses (phases, a second health object); agree with the user whether allies count.

### goal: no_cooldown

- Storage: seconds counting down (float), elapsed time counting up, a frame counter, or an end time compared with a
  game clock.
- Data route: [find a timer](find-timer.md), then [freeze it](freeze-value.md) at the ready value. Tell candidates
  apart while the cooldown runs:
  `memory_read_samples(addresses=["<a1>", "<a2>"], valueType="float", durationMs=3000)`.
- Code route: [find what writes](find-writer.md) on the store that starts the cooldown, then
  [a NOP patch](nop-patch.md); or [trace the logic](trace-logic.md) and [force the branch](patch-branch.md) on the
  ready check.
- Verify: several abilities; enemy cooldowns must stay, because that code is usually shared.

### goal: teleport

- Storage: three consecutive floats (doubles in Unreal Engine 5); the up axis differs by engine.
- Route: [find coordinates](find-position.md). Save with `memory_read(address="<x>", valueType="float", count=3)`
  (`double` in UE5). Load with consent: `process_set_paused(paused=true)`, one
  `memory_write_batch(items=[{address="<x>", valueType="float", value="<x0>"}, ...])` with an item per axis
  (`<x>+4`, `<x>+8`; +8 and +16 for doubles), then `process_set_paused(paused=false)`: MCP's pause is a tracked
  resource that blocks a target switch, so always resume.
- Anchor the object with [a pointer scan](pointer-scan.md) or
  [an injection that copies the base](injection-copy-base.md) so it survives a level load.
- Verify: the character moves and stays; physics, render and camera copies snap back. Add a little height, and save
  first: skipped triggers can break a quest.

### goal: game_speed

- Whole game: the [speedhack](speedhack.md) workflow (Mcp:EnableTargetCodeExecution). The first speed other than 1
  hooks the game, and the hooks stay until it exits (on Unity CE can also inject the Mono collector and overwrite the
  game's time scale); speed 1 restores normal speed but never removes them. `speedhack_get_state.speed` is the
  configured speed, not a measurement.
- Only the player: a movement speed or multiplier, often a float: [find a float](find-float-value.md) while sprinting,
  then one write or [a freeze](freeze-value.md).
- Verify: timers and physics; high factors break physics, and the speedhack cannot pause the game.

### goal: toggle_feature

- Storage: a byte 0 or 1, an int32, one bit of a flags field, or an enum.
- Data route: [find a flag](find-flag.md), toggling several times, then one write or [a freeze](freeze-value.md); in a
  flags field change only that bit.
- Code route when the game recomputes it: [find what accesses it](find-writer.md) with the access trigger,
  [trace the logic](trace-logic.md), then [force the branch](patch-branch.md). A message such as "Locked" leads to the
  check: [find the code that uses a text](find-code-by-string.md).
- Verify: toggle both ways; a branch patch affects every caller.

### goal: unlock_items

- Storage: per-item flags, counts in an inventory array, or item ids, often in a profile object the game saves.
- Route: [find a flag](find-flag.md) for one item or [a known value](find-known-value.md) for a count;
  [dissect the structure](dissect-structure.md) or [compare snapshots](compare-snapshots.md) to map the neighbours;
  [a group scan](group-scan.md) finds an item by several fields. Code route:
  [find the code that uses a text](find-code-by-string.md), then [force the branch](patch-branch.md).
- Verify on a backed-up save: the game saves what you change, and an invalid id or a count above the cap can corrupt
  it. Paid DLC, achievements and online unlocks are out of scope.

### goal: other

- Restate the goal as a value the user can watch change, or a behaviour to stop.
- A visible number: [find a known value](find-known-value.md); a bar or hidden value:
  [find an unknown value](find-unknown-value.md); unknown mechanics near a known object:
  [compare snapshots](compare-snapshots.md); many similar objects: [find the entity list](find-entity-list.md); an
  address to explain: [identify an address](identify-address.md).
- Nothing fits: [a Lua script](write-lua-script.md) (Mcp:EnableUnsafeLua), with the user's review.

## Pitfalls

- Co-op, ranked or leaderboard play, DRM and licence checks are out of scope too, whatever the goal.
- A HUD copy, a profile copy or the client copy of a server value snaps back: keep the address the game logic writes.
- An MCP patch stays until `asm_release_patch` or `runtime_release_resources`, blocks target switching and is not
  saved in a table: build the robust form.
- Never repeat a change after a `hostEffect` of `started` or `unknown`; inspect the state first.

## Report

A table of the plan: step, workflow, what it changes, gate, undo. Then what the user chose, what remains active
(records, patches, speed) and how to undo it: [clean up](cleanup-session.md), after
[a session report](session-report.md) if the user wants one. Background: [session rules](../Documents/workflows.md),
[cheat recipes](../Documents/cheat-recipes.md).
