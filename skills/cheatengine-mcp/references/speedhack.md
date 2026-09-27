# Speedhack

The speedhack changes how fast time passes for the target process by hooking its time functions. It affects only the local process: servers, other players and wall-clock time are unchanged.

## What CE 7.7 installs

On a Windows x86 or x64 target, CE 7.7's `SpeedhackV3.lua` autorun script implements the speedhack; CE 7.7.0.10621 ships no speedhack DLL. The first activation:

- compiles CE's small C helper library (celib) into the target with CE's built-in C compiler;
- allocates the float `speedhack_wantedspeed` in the target and registers it as a symbol;
- on Unity targets (the target exports `mono_thread_attach` or `il2cpp_thread_attach`), unless the user disabled this in CE's Mono settings, starts CE's Mono data collector, which injects a DLL, and looks up `UnityEngine.Time.set_timeScale`. When the method is found, CE hooks its entry and stops there: the time functions below are not patched, and every later speed change also calls `set_timeScale` in the target;
- otherwise patches the entry points of `kernel32.GetTickCount64`, `kernel32.GetTickCount`, `timeGetTime` (when kernel32 exports it) and `ntdll.RtlQueryPerformanceCounter`, which backs `QueryPerformanceCounter`, with jumps to replacement functions that scale the elapsed time.

Consequences:

- This is code execution in the target, so `speedhack_set_speed` requires the `EnableTargetCodeExecution` gate. The gate is on by default and is not a sandbox.
- The hooks are never removed. CE "turns the speedhack off" by setting the speed back to 1; the patched entry points stay until the target exits.
- If a step fails, CE can show a modal error dialog on the user's screen. A failed `set_timeScale` hook is silent: CE reports success without patching the time functions, so confirm the effect in game.
- Anything the hooks do not cover is not scaled: `RDTSC`, `GetSystemTimeAsFileTime`, audio device clocks, vsync, and any server time. On the Unity path only Unity's scaled game time changes. Games that time themselves another way ignore the speedhack, fully or in part.

## Tools

| Tool | Does | Returns |
|---|---|---|
| `speedhack_get_state` | reads CE's last configured speed and whether the hooks exist | `speed`, `hooksInstalled` |
| `speedhack_set_speed(speed)` | installs the hooks on first use, then sets the multiplier | `speed`, `hooksInstalled`, `firstActivation` |

- `speed` is a finite multiplier in 0.01..1000: `0.5` is half speed, `2` double, `1` normal.
- 0 is not a pause. Use `process_set_paused(paused=true)` to stop the target.
- Preconditions are checked before anything runs: a process is attached, it is not paused, the debugger is not stopped on a breakpoint, and the target is x86 or x64. A refusal has `hostEffect` `not_started` and is safe to fix and retry.
- The returned `speed` is CE's configured value, not a measurement of the game. Confirm the effect in game, or read an in-game timer twice with `memory_read` and compare its change with the real time between reads.

## Workflow

1. Confirm the game is single-player or offline and that the user wants a speed change (see [safety](safety.md)).
2. `speedhack_get_state` and remember `speed` as the previous value.
3. `speedhack_set_speed(speed=0.5)`. The first call reports `firstActivation: true`; `hooksInstalled` should now be true.
4. Ask the user to confirm the effect, or measure it as above.
5. Restore when done: `speedhack_set_speed` with the previous value, or `1`.

## Ownership and restore

- While the speed differs from 1, MCP holds a `speedhack` resource. It shows in `runtime_list_resources`, blocks switching targets, and `runtime_release_resources` sets the speed back to 1.
- The speed itself is CE state. A plugin disable leaves it as it is; the next activation reports the leftover entry (marked orphaned) in `runtime_list_resources`, and releasing it restores 1.
- Do not use the speedhack from two CE instances on the same target. Each instance keeps its own symbols, so each would try to install hooks over the other's.

## Errors

- `capability_disabled`: `EnableTargetCodeExecution` is off. Tell the user; do not work around it with Lua or Auto Assembler.
- A failure during the first activation can leave part of the hooks installed (`hostEffect` `started` or `unknown`). Read `speedhack_get_state` and ask the user whether CE showed a dialog before any retry. Never repeat the call blindly.
- A timeout or lost response leaves the outcome unknown. Read the state first (see [errors-and-recovery](errors-and-recovery.md)).

## Side effects and risks

- Physics: at high speed each frame covers more time, so objects can pass through collisions, jumps overshoot and simulations become unstable.
- Low speeds make some games stutter or hang, especially those that mix hooked and unhooked clocks.
- Audio, video and cutscenes can drift out of sync with the scaled game clock.
- Frame-locked games (logic advanced per frame, not per elapsed time) barely react.
- Every thread in the process is affected, including network heartbeats and timeouts.
- Online: the server keeps real time. Expect rubber-banding, desync, disconnects and account flags. Never use it in multiplayer.
- Anti-cheat: patched API entry points fail integrity checks, a scaled clock disagrees with unhooked clocks or the server, and the Unity path injects a DLL. Each is detectable and can lead to a ban.
- Kernel-level system-wide time scaling exists in CE but is never exposed through MCP (see [kernel](kernel.md)).

## Sources

- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Speedhack
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/autorun/SpeedhackV3.lua (older than the copy installed with CE 7.7.0.10621, which adds the Unity `set_timeScale` path)
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/autorun/celib.lua
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/bin/celua.txt
- https://forum.cheatengine.org/viewtopic.php?t=611172
- https://learn.microsoft.com/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter
- https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64
