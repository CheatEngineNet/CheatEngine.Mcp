# Speedhack

Cheat Engine's (CE's) speedhack makes time pass faster or slower inside one target process by hooking the clocks that
process reads. Read this page before the first `speedhack_set_speed` call in a process: the first activation changes
code in the target for the rest of its life, and speed 1 does not undo it. The guided version is
[speedhack](../Workflows/speedhack.md).

## Responsible use and consent

- Use it only on single-player or offline software that the user owns or may modify. Never use it on online,
  multiplayer, competitive or anti-cheat-protected games. The server keeps real time, so the game desyncs or
  disconnects, and changing such a game breaks its terms and can get the account banned. For those games the answer is
  "not with this tool" (see [safety](safety.md)).
- A single-player game with online parts (leaderboards, synced achievements, a cloud economy) counts as online unless
  the user confirms that a fully offline mode is in use.
- Before the first speed change in a process, tell the user:
  - what it does: CE compiles its helper code into the game and hooks its clocks, and in a Mono or IL2CPP game (most
    Unity games) it also injects CE's Mono data collector DLL. All of it stays until the game exits;
  - what can break: physics, audio and network timeouts (see Side effects below);
  - how to undo it: set speed 1, then restart the game to get a clean process.

  Wait for a yes. Confirm each later change, and ask again after switching processes. Suggest an in-game save first.
- CE's graphical tutorial `gtutorial-x86_64.exe`, in CE's folder, is a safe target to try it on. Its source times the
  game with `GetTickCount64`, one of the clocks CE hooks (see [CE tutorial](ce-tutorial.md)).

## Tools

| Tool or resource                   | Does                                   | Returns                              |
|------------------------------------|----------------------------------------|--------------------------------------|
| `speedhack_get_state()`            | reads CE's configured speed and symbol | `speed`, `hooksInstalled`            |
| `speedhack_set_speed(speed=...)`   | sets it; other than 1 may activate it  | also `firstActivation`, `resourceId` |
| `cheatengine://instance/speedhack` | live copy of `speedhack_get_state`     | the same fields                      |

- Through the gateway the live resource is `cheatengine://instances/{instanceId}/speedhack`. The gateway lists it once
  it has confirmed that instance, for example after `instance_list`.
- `speed` is a finite multiplier from 0.01 through 1000: 0.5 is half speed, 2 is double and 1 is normal. Any other
  value is refused with `invalid_argument`. 0 is not a pause: stop the target with `process_set_paused(paused=true)`,
  which MCP tracks as a pause resource, and resume it with `process_set_paused(paused=false)`.
- Every `speedhack_set_speed` call, speed 1 included, requires the `target_code_execution` gate
  (`Mcp:EnableTargetCodeExecution`, on by default and shown in `runtime_get_info.gates`; see
  [configuration](configuration.md)). `speedhack_get_state` is read-only and ungated.
- `speedhack_set_speed` has the dispatch class `may_prompt`: the first activation can show CE dialogs that wait for a
  person, and the call lasts until they are closed.
- Every `speedhack_set_speed` call needs an attached process. A speed other than 1 is then refused, in this order,
  while the debugger is stopped at a breakpoint, while the target is paused, and on a target that is neither x86 nor
  x64. These refusals are `not_started` (see Errors). Speed 1 never activates the speedhack (see Restore normal speed).
- A new speed first releases the previous MCP restore resource, which sets the speed to 1, so a change from 2 to 3
  passes through 1 for a moment. If the new speed is then refused, even with `not_started`, the speed stays at 1.
- The returned `speed` is what CE reports as configured, not a measurement. CE keeps it as a 32-bit float, so 1.1 reads
  back as 1.100000023841858. A larger difference means that CE did not apply the request: after a failed activation
  CE clears its "Enable Speedhack" checkbox and reports 1, and the call still succeeds. Read `speedhack_get_state` and
  ask the user whether CE showed a dialog.

## Use it

1. Get consent as described above.
2. `speedhack_get_state()`: note `speed`, the value before your change, and `hooksInstalled` (see below).
3. `speedhack_set_speed(speed=2)`. Keep the returned `resourceId`. Check `speed`, `hooksInstalled` and
   `firstActivation`, and after a first activation ask whether CE showed a dialog.
4. Confirm the effect with the user, or measure it (see Measure the effect below).
5. When done, first resume a pause you made with `process_set_paused(paused=false)`, then call
   `speedhack_set_speed(speed=1)`. It does nothing when CE's and the target's speed are already 1. Restore an earlier
   speed other than 1 only if the user asks; it is then tracked again.

## What the first activation does

For a speed other than 1, MCP calls CE's Lua `speedhack_setSpeed`. If the "Enable Speedhack" checkbox in CE's main
window is clear, CE ticks it, and ticking it activates the speedhack whatever the speed, 1 included. That is why MCP
calls it for speed 1 only in a process that already has the symbol `speedhack_wantedspeed`. On an x86 or x64 Windows
target, CE 7.7's autorun script `SpeedhackV3.lua` does the work (CE 7.7 ships no speedhack DLL):

1. If the symbol `speedhack_wantedspeed` already resolves, CE treats the process as hooked and hooks nothing.
2. CE compiles its C helper library (celib) into the target with its built-in C compiler. If that fails, CE shows
   error dialogs, clears the checkbox and stops before step 3, so the next speed other than 1 tries again.
3. CE allocates the float `speedhack_wantedspeed` (1.0) and the int `speedhack_unityspeedhackmethod` (1) in the target
   and registers both as CE symbols. From this point CE never retries hooking in this process.
4. **Unity path.** If `mono_thread_attach` or `il2cpp_thread_attach` resolves in the target, CE starts its Mono data
   collector, which injects a DLL, and looks up `UnityEngine.Time.set_timeScale`. A hidden CE setting skips this step:
   the value `SkipMonoSpeedhack` set to `1` under CE's `MonoExtension` settings, which no CE window shows. When CE
   finds the method's code:
   - it hooks the method's entry, so every timeScale the game sets is multiplied by the speed;
   - it patches no Windows time function;
   - every speed change, the first included, also calls `set_timeScale` with the speed in the target.

   When CE does not find the method, it continues with step 5.
5. **Windows path.** CE writes jumps at the entries of these functions:
   - `kernel32.GetTickCount64` and `kernel32.GetTickCount`;
   - `kernel32.timeGetTime`, if kernel32 exports it (checked on Windows 11 build 26200, where winmm's `timeGetTime`
     also resolves to it);
   - `ntdll.RtlQueryPerformanceCounter`, which `QueryPerformanceCounter` forwards to.

   The replacement functions scale the time elapsed since the last speed change. They re-base at each change, so the
   clock stays continuous.
6. Every later change only writes the new speed into `speedhack_wantedspeed`, plus the `set_timeScale` call on the
   Unity path.

The patches are private to the target process. Other processes and the system clock are not affected.

## What hooksInstalled and firstActivation mean

- `hooksInstalled` only says that `speedhack_wantedspeed` resolves. It does not prove that any clock is hooked:
  - A failed time-function hook shows a CE error dialog, but the script still reports success, because it returns the
    result of the allocation instead of the results of the hooks. One clock can end up scaled while another is not.
  - On the Unity path, a failed `set_timeScale` hook is silent.
  - Once the symbol exists, CE never retries hooking in that process. Only a restart of the target makes CE try again.
- `hooksInstalled` false after an activation means CE stopped before the allocation, for example because the helper
  library failed. The next `speedhack_set_speed` with a speed other than 1 tries again.
- `firstActivation` true means the symbol was absent before the call, so CE made its hook attempt. Speed 1 never
  activates the speedhack and always reports false.
- If `hooksInstalled` is already true before any MCP change, CE's speedhack already ran in this process: CE's checkbox
  or hotkeys, a cheat table script, an earlier plugin activation, or another CE instance attached to the same process
  whose symbols CE synchronizes. CE will not hook this process again.
- A stale symbol from an earlier target has the same effect. CE deletes its registered symbols when it opens a
  different process only while "Synchronize symbols" and "Clear all symbols when a new process is opened" are ticked
  in CE's Settings > Symbols, as they are by default. With either one clear, a symbol left by an earlier target makes CE
  skip hooking the new one.

## Measure the effect

1. Find a value that follows the game clock, such as an in-game timer ([find a timer](../Workflows/find-timer.md)).
2. At speed 1, sample it:
   `memory_read_samples(addresses=["<timer address>"], valueType="float", durationMs=5000)`, using the timer's
   actual type. The rate is the change from `first` to `last` divided by `elapsedMs`.
3. Change the speed and sample again. The ratio of the two rates should be close to the speed. A ratio near 1 means
   that the game reads a clock the hooks do not cover, that it advances per frame, or that a hook failed. On the Unity
   path, a ratio near the square of the speed would confirm the squared timeScale described under Side effects.

## Restore normal speed

- While the speed differs from 1, MCP holds a resource. `runtime_list_resources` shows it with kind `speedhack`,
  category `lua_state`, and the id that `speedhack_set_speed` returned as `resourceId`. It blocks `process_attach`,
  `process_create`, `process_open_file` and `debugger_detach`; they are refused with `busy`.
- `speedhack_set_speed(speed=1)` is the normal restore, and it is safe to call when unsure:
  - With MCP's resource, it releases that resource, which sets 1.
  - It does nothing while CE's speed and the target's own speed are both 1, and it never hooks a process.
  - When it must restore a speed that MCP does not track, for example one set by CE's hotkeys or a table script, it is
    refused while the debugger is stopped or the target is paused, because on the Unity path CE's restore calls into
    the game. Resume first.
  - It is refused with `invalid_state` when CE reports another speed but the opened process has no
    `speedhack_wantedspeed` symbol: that speed belongs to another process (see the next section).
- `runtime_release_resources()` releases everything this activation holds, newest first, patches and allocations
  included, so call it only at the end of cleanup or when the user agrees to release everything. It is ungated, so it
  also restores the speed while `Mcp:EnableTargetCodeExecution` is off. Its speedhack release:
  - fails with `cleanup_failed` ("The sped-up process is no longer the opened process") while CE has another process
    open;
  - does nothing when CE reports 1 and the target has no symbol or its own speed is 1;
  - otherwise sets 1 and checks that CE reads back 1.
- At the end of a session, resume before restoring the speed. The full order is in
  [clean up this session](../Workflows/cleanup-session.md).
- The speed is CE state, not MCP state, and a plugin disable does not restore it. The next activation lists the entry
  with `orphaned` true. The orphan also blocks target changes, and `runtime_release_resources(includeOrphans=true)`
  restores the speed to 1, together with every other resource and orphan, so ask the user first.
- Do not run the speedhack from two CE instances on the same target. Each instance keeps its own speed, and whether
  the second one sees the first one's `speedhack_wantedspeed` depends on CE's symbol synchronization. If it does not,
  it patches the already patched entries again.

## When the speed changed outside MCP

- `speedhack_get_state` reports CE's configured speed, not the target's. Read it before assuming the current speed.
  While `hooksInstalled` is true, `memory_read(address="speedhack_wantedspeed", valueType="float")` reads the value
  the target's hooks use.
- Clearing CE's "Enable Speedhack" checkbox sets the speed to 1. CE's speedhack hotkeys or a table script can set any
  other speed.
- Opening another process never restores the old one, which keeps its speed:
  - When a script, a plugin or CE's auto-attach opens it, CE clears "Enable Speedhack" and then reports 1.
  - When the user opens it from CE's process list, the checkbox stays ticked and CE keeps reporting the old process's
    speed in the new one. `speedhack_set_speed(speed=1)` is refused there rather than hooking the new process, and
    CE's own Apply button would hook it.
- Back in the sped-up process, if `speedhack_get_state` shows `hooksInstalled` true, `speedhack_set_speed(speed=1)`
  sees the target's own speed and restores it without hooking again. Otherwise restart the game.
- These switch cases come from CE's source and MCP's code; they have not been tested live.

## What stays after speed 1

Speed 1 does not mean "unhooked". CE's speedhack never disables itself; it only sets the speed to 1. CE's source says
why: not every game handles a clock that goes back. Until the target exits, these remain:

- the jumps at the time functions, or the `set_timeScale` hook, together with the replacement code, so every clock read
  still passes through CE's code;
- the helper library and the two allocations, with their CE symbols;
- on a Mono or IL2CPP target, CE's Mono data collector, so `mono_get_status` then reports `attached` true.
  `mono_detach` closes only an attachment that MCP made with `mono_attach`
  ([Unity (Mono) and .NET](mono-and-dotnet.md));
- the ticked "Enable Speedhack" checkbox in CE's main window.

Removing the hooks while the game runs is unsafe, for two reasons:

- The scaled clock would jump to real time, backward if the game ran faster than 1, and Microsoft documents that
  `QueryPerformanceCounter` never goes backward.
- Threads may be executing inside the replacement code at that moment.

To get a clean process, save the game and restart it.

## Side effects

- **Physics.** At high speed each frame covers more game time. Objects pass through collisions, jumps overshoot, and
  fixed-step simulations can stall. Low speeds can freeze timers and animations.
- **Audio and video.** The audio device keeps its own clock. Sound stays at normal speed or drifts out of sync, and
  cutscenes desync.
- **Clocks that are not scaled.** The hooks do not cover these clocks:
  - `Sleep` and wait timeouts;
  - window timers and multimedia timer callbacks;
  - `GetSystemTimeAsFileTime` (the system time);
  - `GetTickCount` and `GetTickCount64` reached through KernelBase, for example through an `api-ms-win-core-sysinfo`
    import: KernelBase has its own copies, which CE does not patch (checked on Windows 11 build 26200);
  - direct CPU timestamp reads (`rdtsc`);
  - vsync.

  A game that mixes these with hooked clocks stutters or partly ignores the speedhack. A game that advances its logic
  per frame barely reacts.
- **Network.** Every thread sees the scaled clock, so keep-alives and timeouts fire early above 1 and late below 1. A
  "single-player" game that talks to a server can drop its connection, for example for DRM check-ins, cloud saves or
  telemetry.
- **Precision.** The Windows replacements multiply the elapsed ticks by a 32-bit float. The scaled clock therefore gets
  coarser the longer the speed stays unchanged, even at 1. With a 10 MHz performance counter the steps grow to about
  0.4 ms after an hour and 6.5 ms after a day (worked out from CE's code, not measured). A long session can start to
  stutter; restarting the target fixes it.
- **Unity path:**
  - Only Unity's scaled game time changes. Unscaled time, and anything driven by it, keeps real time.
  - CE's own `set_timeScale` call replaces the game's timeScale. A pause menu that set 0 can resume, slow motion is
    lost, and restoring speed 1 can unpause a paused game.
  - CE writes the new speed before its call, and the call passes through CE's own multiplying hook, so right after a
    change the timeScale may be the square of the speed (4 at speed 2) until the game sets it again. This has not been
    verified live, and neither has whether every Unity build accepts CE calling `set_timeScale` from outside Unity's
    main thread.

For engine detection and the Mono collector, see [game engines](game-engines.md),
[Unity (Mono) and .NET](mono-and-dotnet.md) and [Unity IL2CPP](unity-il2cpp.md).

## Errors

- `capability_disabled`: `Mcp:EnableTargetCodeExecution` is off. Tell the user. Never work around it with `lua_execute`,
  Auto Assembler or a cheat table.
- `invalid_argument`: `speed` is not finite, or it is outside 0.01 through 1000.
- `invalid_state` (`not_started`); fix the cause, then retry:
  - no process is attached: attach one first;
  - the debugger is stopped at a breakpoint, as CE reports it (`debugger_get_status` shows `reportedBroken`): continue
    with `debugger_continue()`;
  - the target is paused: resume it with `process_set_paused(paused=false)`;
  - speed 1 only: CE reports another speed, but the opened process has no `speedhack_wantedspeed` symbol, so setting 1
    would activate the speedhack there. See When the speed changed outside MCP.
- `unsupported` (`not_started`): a speed other than 1 on a target that is neither x86 nor x64.
- `host_refused`: CE did not return a finite speed, `not_started` before any change and `started` after a change or a
  restore. Read `speedhack_get_state` before doing anything else.
- `partial_effect` (`started`, not retryable): MCP could not restore the previous speed to 1, so the new speed was not
  applied. The error's details carry the old `resourceId`, which `runtime_list_resources` now shows as
  `cleanup_failed`. The usual cause is that CE has another process open.
  1. Restore that process by hand: open it again in CE. If `speedhack_get_state` then shows `hooksInstalled` true, make
     sure "Enable Speedhack" is ticked in CE, enter 1 and click Apply; otherwise save and restart the game.
  2. Today the tool keeps that failed restore, so every later `speedhack_set_speed` call, speed 1 included, fails the
     same way until the plugin is disabled and enabled again.
  3. The entry blocks target changes until it is acknowledged. At the end of cleanup, with the user's consent, call
     `runtime_release_resources(acknowledgeIds=["<old resource id>"])`, which also releases everything else MCP holds.
     If `runtime_list_resources` still lists the id, acknowledge it again.
- A timeout or a lost response leaves the outcome unknown, often because a CE dialog is waiting. Ask the user to close
  it, then read `speedhack_get_state` and `runtime_list_resources`. Never repeat the call blindly (see
  [errors and recovery](errors-and-recovery.md)).

## Not exposed through MCP

CE can also scale the whole machine's timestamp counter through DBVM (`dbvm_speedhack_setSpeed`). That changes the clock
of Windows and of every process. No MCP tool offers it, and it must not be reached through `lua_execute` (see
[kernel](kernel.md)). To undo everything a session changed, including the speed, use
[clean up this session](../Workflows/cleanup-session.md).

## Sources

- CE 7.7.0.10621 install: `autorun/SpeedhackV3.lua`, `autorun/celib.lua` (`injectCEHelperLib`), `autorun/monoscript.lua`
  (the `MonoExtension` settings, the collector DLL) and `celua.txt` (`speedhack_setSpeed`, `registerSpeedhackCallbacks`,
  `dbvm_speedhack_setSpeed`)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/speedhack2.pas (activation callbacks; the
  speed reads 1 without an active speedhack; freeing it restores 1 only in the process it was made for; restoring 1
  never removes the script)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/pluginexports.pas (setting a speed ticks the
  Enable Speedhack checkbox)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/MainUnit.pas (a failed activation clears the
  checkbox, clearing it restores 1; `openProcessEpilogue` clears it only for a script, plugin or auto-attach open, and
  clears registered symbols)
- The CE 7.7 source of `pluginexports.pas` and `LuaHandler.pas` (a plugin or Lua process open counts as auto-attach)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/formsettingsunit.lfm (symbol settings and
  their defaults)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas (`speedhack_setSpeed` takes
  a 32-bit float)
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/bin/autorun/SpeedhackV3.lua (older than the
  installed copy, which adds the Unity path)
- https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine/Tutorial/graphical (gtutorial timing)
- The export tables of `kernel32.dll`, `KernelBase.dll` and `winmm.dll` on Windows 11 build 26200
- https://wiki.cheatengine.org/index.php?title=Cheat_Engine:Speedhack
- https://learn.microsoft.com/windows/win32/sysinfo/acquiring-high-resolution-time-stamps
- https://learn.microsoft.com/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter
- https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-gettickcount64
