# Change game speed

## Goal

Run the game at `{speed}` times its normal speed (for example 0.5 or 2), then restore it.

## Steps

1. Confirm the game is single-player or offline: speed changes desync or disconnect online games and are easy for
   anti-cheat to detect. See [safety](../safety.md).
2. `runtime_get_info()`: `gates.targetCodeExecution` must be true, otherwise `speedhack_set_speed` fails with
   `capability_disabled`.
3. `speedhack_get_state()`: keep the current `speed` to restore it; `hooksInstalled` tells whether CE's speedhack hooks
   are already in the game.
4. `process_get_current()` must show `paused=false`, and `debugger_get_status()` must show `broken=false`.
5. Explain the host effect: CE 7.7 has no speedhack DLL. The first activation compiles CE's helper library into the
   game, registers `speedhack_wantedspeed` and patches GetTickCount, GetTickCount64, timeGetTime and
   RtlQueryPerformanceCounter. On a Unity game it may instead start CE's Mono data collector (a DLL injection) and hook
   `UnityEngine.Time.set_timeScale`. The hooks stay until the game exits, even at speed 1, and a failure can show a CE
   dialog someone must dismiss.
6. With consent: `speedhack_set_speed(speed={speed})`; check the returned `speed` and `firstActivation`.
7. Ask the user whether the game runs at the expected speed.
8. When done: `speedhack_set_speed(speed=<previous speed>)`, normally 1.

## Decisions

- `{speed}` must be between 0.01 and 1000. Zero is not a pause: use `process_set_paused(paused=true)` for that, and
  resume afterwards.
- No visible effect: the game uses another clock or a fixed frame step, or on Unity CE's `set_timeScale` hook failed
  silently; report it instead of trying other tools.
- An error or dialog on the first activation: ask the user to dismiss the dialog, then read `speedhack_get_state()`;
  never repeat the call blindly.
- Physics or animation glitches at high speed: suggest a smaller factor.

## Pitfalls

- While the speed differs from 1, the speedhack is an owned resource and blocks target switching; restore it first.
- The readback is the configured multiplier, not a measurement.
- An error whose `hostEffect` is not `not_started` or `not_applied` may have changed the speed: call
  `speedhack_get_state()` before any retry.
- Another CE instance attached to the same game can change its speed too.

## Report

Previous and current speed, whether the hooks were newly installed, and, while the speed still differs from 1, the owned
speedhack resource and its restore call `speedhack_set_speed(speed=1)`. See [speedhack](../speedhack.md).
