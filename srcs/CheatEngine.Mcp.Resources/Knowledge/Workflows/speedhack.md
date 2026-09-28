# Change game speed

## Goal

Run the attached game at `{speed}` times its normal speed (0.5 is half speed, 2 is double), confirm the effect with the
user, then restore normal speed. Only for single-player or offline games that the user owns or may modify.

## Steps

1. Ask whether the game is single-player or offline and free of anti-cheat; stop otherwise. A server keeps real time,
   so an online game desyncs or disconnects, and changing it can get the account banned
   ([safety](../Documents/safety.md)).
2. `speedhack_get_state()`: keep `speed`, the value to restore, and `hooksInstalled`. If `hooksInstalled` is already
   true, CE's speedhack already ran in this process (its checkbox or hotkeys, a table script, an earlier session), and
   CE will not hook it again.
3. To measure the effect later, find a value that follows the game clock ([find a timer](find-timer.md)) and sample it
   now: `memory_read_samples(addresses=["<timer address>"], valueType="float", durationMs=5000)`, with the timer's
   real type. Skip this when the user will judge by eye.
4. Explain the host effect, suggest an in-game save and wait for a yes. The first speed other than 1 in a process
   compiles CE's helper code into the game and hooks its clocks (GetTickCount, GetTickCount64, timeGetTime,
   QueryPerformanceCounter); on a Unity game CE may instead inject its Mono data collector and hook `set_timeScale`.
   The hooks stay until the game exits, even at speed 1, and a failure can show a CE dialog that a person must close.
5. `speedhack_set_speed(speed={speed})`. Keep `resourceId`, check that the returned `speed` is `{speed}` (CE stores a
   float, so 1.1 reads 1.100000023...) and read `firstActivation`; after a first activation, ask whether CE showed a
   dialog. A returned 1 instead means the activation failed: CE cleared its checkbox, yet the call succeeds.
6. Ask the user whether the game runs at the expected speed, or repeat the call of step 3: (`last` - `first`) /
   `elapsedMs` now, divided by the same from step 3, should be close to `{speed}`.
7. When the user is done: resume a pause you made with `process_set_paused(paused=false)`, then
   `speedhack_set_speed(speed=1)`, which ends the resource and never hooks. Restore an earlier speed other than 1 only
   if the user asks; that speed is then tracked again.

## Decisions

- `{speed}` of 1 only restores and never activates the speedhack, so it needs no step 4; it does nothing while CE's
  and the target's speed are both 1. Its `invalid_state` naming another speed that CE reports means that speed
  belongs to another process: relay the error's hint to the user.
- `invalid_state` with `not_started`: no process is attached (attach first), the target is paused, or the debugger is
  stopped (`debugger_get_status()` shows `reportedBroken`). Resume with `process_set_paused(paused=false)` or
  `debugger_continue()`, then retry. Zero is not a pause: `process_set_paused(paused=true)` pauses, as a resource MCP
  tracks, and step 7 resumes it.
- `capability_disabled`: `Mcp:EnableTargetCodeExecution` is off. Tell the user and stop; never reach the speedhack
  through Lua, Auto Assembler or a cheat table.
- `unsupported`: the target is neither x86 nor x64.
- No visible effect: the game times itself with a clock the hooks do not cover (such as the CPU timestamp counter or
  the system time), advances per frame, or a hook failed. Report it. Once `hooksInstalled` is true, CE never hooks
  that process again until a game restart; while it is false, the next call retries.
- Physics glitches at high speed, or frozen timers and animations at low speed: suggest a factor closer to 1.

## Pitfalls

- `hooksInstalled` only says that CE's symbol `speedhack_wantedspeed` exists. It is also true after a failed hook and
  on the Unity path, so it does not prove that any clock is hooked.
- The returned `speed` is CE's configuration, not a measurement.
- A new speed first restores 1, then applies the new one; if the new one is refused, the speed stays at 1.
- While the speed differs from 1, the speedhack resource blocks target changes and `debugger_detach`.
- `partial_effect`: MCP could not restore 1 first, usually because CE has another process open. The new speed was not
  applied, and later calls fail the same way: follow [its recovery](../Documents/speedhack.md#errors).
- `host_refused`, a timeout or a lost response may already have changed the speed, often while a CE dialog waits. Ask
  the user to close any dialog, then read `speedhack_get_state()` and `runtime_list_resources()` before any retry.
- Never run the speedhack from two CE instances on the same game.

## Report

A table of the previous speed, the requested speed, the returned `speed` and `firstActivation`, plus what the user
observed or measured. What remains: the hooks (and on the Unity path CE's Mono data collector) until the game exits;
while the speed differs from 1, the resource `resourceId`, undone by `speedhack_set_speed(speed=1)` or by
`runtime_release_resources()`, which releases everything this session holds. Background:
[speedhack](../Documents/speedhack.md) and [safety](../Documents/safety.md).
