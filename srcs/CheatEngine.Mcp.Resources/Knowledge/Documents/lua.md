# Lua in CheatEngine.Mcp

Cheat Engine 7.7 embeds Lua 5.3 with its own API, documented in `celua.txt`. CheatEngine.Mcp uses Lua in two very
different ways: reviewed scripts inside the tools, and your own chunks sent through `lua_execute`. Read this page before
you write any Lua. The API itself is in the [Lua API cheat sheet](lua-api.md); the step-by-step procedure (check, draft,
get consent, run, report) is the [write_lua_script workflow](../Workflows/write-lua-script.md).

## Dedicated tools versus `lua_execute`

| | Dedicated tools | `lua_execute` |
|---|---|---|
| Who writes the code | The plugin: Client calls and fixed, reviewed Lua bodies | You |
| Your input | Validated values passed as data, never spliced into code | Any source, run as written |
| Gates checked | The tool's own gates, plus a scan of each fixed Lua body | `unsafe_lua` only; the chunk is not read |
| Output | A typed result with a schema and bounds | A bounded copy of the values you return |
| What it leaves behind | Resources listed by `runtime_list_resources`; records and structures stay in CE | Untracked |
| Dispatch class | Declared per tool, such as `short` or `host_scan` | `may_prompt`: it may show a dialog and wait |

Before a fixed body runs, the server scans its identifiers (comments included) and refuses with `capability_disabled`
when the matching gate is off: `autoAssemble` and `autoAssembleCheck` need `auto_assembler`; `executeCode…`,
`inject…`, `executeMethod`, `compile`, `compileCS`, `LaunchMonoDataCollector`, the Lua function `mono_invoke_method`
and `speedhack_setSpeed` need `target_code_execution`; `dbk_…` and `dbvm_…` need `kernel_access`; `loadTable` needs
`unsafe_lua`. Nothing scans a `lua_execute` chunk.

Choose in this order:

1. A dedicated tool: look it up in the [tool map](tool-map.md). It validates arguments, bounds its time on CE's main
   thread, reports `hostEffect` and registers the resources it leaves behind.
2. Several tools in sequence, for example `symbol_resolve` and then `memory_read_batch`.
3. `lua_execute`, only for a documented CE feature that no tool covers. Tell the user so, show the source and get
   consent before running it.

## How `lua_execute` runs a chunk

`lua_execute` needs `Mcp:EnableUnsafeLua`, which is on by default ([configuration](configuration.md)).
`runtime_get_info.gates` reports it as `unsafeLua`; a change applies after the plugin is disabled and enabled again.
The whole call is one dispatch on CE's main thread, in two stages:

1. **Stage A** passes your `source` (at most 1,048,576 UTF-8 bytes) as a string to Lua's `load` in text mode, so
   precompiled bytecode is refused. The chunk gets your `chunkName` (1 to 256 UTF-8 bytes, default `=lua_execute`) and
   is called under `pcall`. Stage A then puts back the globals `rawget`, `rawset`, `type`, `_ENV` and `getTickCount`
   as they were before the chunk, because the fixed scripts need them, and stores the outcome, with a per-call token,
   in a private global.
2. **Stage B** is a fixed script. It reads and clears that global and copies the outcome into the tool result. It runs
   even when stage A failed, so the global never stays behind.

The chunk runs in CE's shared Lua state. `local` names stay inside the chunk; any other assignment creates or replaces a
global that lasts until CE closes (except the five globals stage A puts back). Line numbers in errors match your
source. A chunk name that starts with `=` is shown verbatim: `lua_execute(source="...", chunkName="=hp-probe")` reports
errors as `hp-probe:3: …`; any other name is shown as `[string "name"]:3: …`.

| Outcome | `ok` | `phase` | `hostEffect` | What it means |
|---|---|---|---|---|
| Success | `true` | none | `completed` | The chunk ran to its end; `returnValues` holds what it returned. |
| Compile error | `false` | `compile` | `not_applied` | Nothing ran. Fix the source and send it again. |
| Runtime error | `false` | `runtime` | `unknown` | What ran before the error stays done. Check state before a rerun. |

Compile and runtime errors are normal tool results, with Lua's message in `error`; they are not `isError`. The call
fails with `isError` only in these cases:

- The gate is off: `capability_disabled`, and nothing ran.
- An argument is out of bounds (source too long, empty or oversized chunk name, invalid text): `invalid_argument`, and
  nothing ran.
- The instance is saturated: `busy`, and nothing ran. Retry after the running calls finish.
- The Client could not run stage A: its own kind and `hostEffect`, for example `host_refused` with `started`.
- The return values cannot be copied: `limit_exceeded` or `internal`, with `hostEffect` `completed`. The chunk has
  already run; do not resend it only to get the values.
- Stage A left no result: `internal`, with `hostEffect` `unknown`.
- The gateway stopped waiting: `timeout`, with `hostEffect` `unknown` (see CE's main thread, below).

Never resend a chunk after `started` or `unknown`, or after a failure with `completed`, without first reading the state
it touches: a second run doubles its effects. The error kinds are explained in [errors and
recovery](errors-and-recovery.md).

## What comes back

Only `return` values come back, in order, in `returnValues`: a chunk that returns nothing gives `[]`, and `return nil`
gives `[null]`. `print` writes to CE's Lua Engine window (which CE may open), not to the result.

| Lua value | JSON value |
|---|---|
| `nil`, boolean | `null`, boolean |
| Integer | Integer, all 64 bits |
| Finite float | Number |
| NaN or an infinity | Refused: return `tostring(x)` instead |
| String | String; invalid UTF-8 bytes become U+FFFD, so hex-encode binary data |
| Table with keys exactly `1..n` | Array; an empty table is `[]` |
| Table with string keys only | Object |
| Result of `table.pack(...)` | Array of length `n`, with holes as `null` |
| Function, userdata (every CE object), thread | Dropped: `null` in an array, omitted from an object |

- Dropped values are counted in `droppedOpaqueCount`. Return an object's fields instead (`mr.Description`,
  `mr.Address`), not the memory record, form or stream itself.
- These shapes are refused, and the call fails with `internal` after the chunk has run: NaN or infinity, a cyclic
  table, a table that mixes string and integer keys, integer keys that are not `1..n` (a sparse array, or one that
  starts at 0), keys that are neither strings nor integers, and keys that are not valid UTF-8.
- A table whose only string key is `n`, holding an integer, is read as a `table.pack` result: `{n = 5}` becomes an
  array of five nulls. Name such a field `count`.
- Bounds for the whole copy: 4 MiB of string data (keys included), 65,536 values, 16 nested table levels (the result
  envelope uses two of them) and 8 MiB of JSON. Beyond any of them the call fails with `limit_exceeded` and returns no
  partial result. Return summaries, not dumps, and page long lists (see Safe script patterns, below).
- JSON readers may round integers above 2^53. Return addresses and 64-bit values as strings:
  `string.format('%X', addr)` gives the uppercase hex without `0x` that every tool uses.
- In Lua source, hex literals need `0x` (`base + 0x1A2B3C`), unlike Auto Assembler. CE reads an address given as a
  string as hex or a symbol, never as decimal: `'100'` means 0x100.

## Not a sandbox

A chunk runs with CE's full Lua: files (`io`), the host (`os`, `shellExecute`, `runCommand`), `autoAssemble`,
`executeCode…`, `inject…`, `dbk_…` and `dbvm_…`, `speedhack_setSpeed`, CE's settings and every form. The server checks
only `Mcp:EnableUnsafeLua` and never reads your chunk. While that setting is on, the other gates bind only the dedicated
tools, so keeping them is your job:

- **Never use Lua to do what a gate refused.** When a tool returns `capability_disabled`, tell the user which
  `Mcp:Enable…` setting is off (the message names it; `runtime_get_info.gates` shows all four) and stop. Do not
  rephrase the action as a chunk, an Auto Assembler `{$lua}` block (in a script or in a record's script), a cheat
  table, an address expression or a call to another instance. Only the user changes a setting.
- Gated routes to Lua: `asm_apply` refuses a script with `{$lua}` or `luacall` while `Mcp:EnableUnsafeLua` is off
  (`{$luacode}` also needs `Mcp:EnableTargetCodeExecution`), and `table_load` refuses a table that carries a Lua script
  or forms, or one it cannot inspect, such as a `.CETRAINER`. `asm_check` refuses such a script as `unsupported`
  whatever the switches say, because CE runs parts of a script while it checks it ([Auto Assembler](auto-assembler.md),
  [cheat tables](cheat-tables.md)).
- Routes MCP does not gate: record scripts are not classified, so `record_set_active` runs a record's `{$lua}` block
  with only `Mcp:EnableAutoAssembler`. CE's symbol handler runs Lua from an address expression (a `$name` token, or any
  unknown name through CE 7.7's `autorun/luasymbols.lua`) in every tool that takes an address, read-only ones included
  ([Lua inside expressions](address-expressions.md#lua-inside-expressions)). Read a record's script before activating
  it, and keep Lua out of addresses.
- Show the full source and say what it reads and changes. Get the user's consent before any chunk that writes target
  memory, changes CE or the host, touches files or runs code in the target. Show a read-only probe too.
- Use it only on single-player or offline software that the user owns or may modify. Never use it on online or
  competitive games or on titles protected by anti-cheat. Never write Lua for anti-cheat bypass, DRM or licence
  cracking, or detection evasion ([safety](safety.md)).
- Never read the MCP instance registry or its access tokens, CE's settings files, or user files unrelated to the task.
- Never call `resetLuaState`, `closeCE` or `loadPlugin`. `resetLuaState` replaces CE's Lua state, and the plugin then
  refuses every Lua-backed tool until it is disabled and enabled again; `closeCE` closes CE; `loadPlugin` loads a
  native DLL into CE.

## CE's main thread

`lua_execute` holds CE's main (GUI) thread for the whole chunk, and a running dispatch cannot be interrupted, not even
by cancelling the request. While it runs, CE's window freezes, and other calls to that instance wait behind it or
return `busy` once `Mcp:Execution:MaxConcurrentDispatches` calls (4 by default) are admitted. Through the gateway, a
call that outlasts its timeout (45 s by default, see [configuration](configuration.md)) returns `timeout` with
`hostEffect` `unknown` while the chunk may still be running: read the state before doing anything else. Keep chunks
well under a second.

- No `sleep`, `waitTillDone`, `waitfor` or other waits for symbols, scans or threads.
- No `processMessages`: it lets timers, GUI events and other scripts run in the middle of your chunk.
- No `showMessage`, `messageDialog`, `inputQuery`, forms or other dialogs: a modal waits for a human click.
- No need for `synchronize`: the chunk already runs on the main thread.
- No loops over the whole address space and no unbounded scans. Use `aob_find`, `scan_first` or ranges bounded by a
  module.
- No Mono calls from Lua: use the `mono_…` tools. Before they touch CE's Mono pipe they check that the collector is
  attached, that the target is neither paused nor stopped at a breakpoint and that CE's Mono timeout dialog is not
  showing, and they reconnect a failed pipe ([Mono guard](mono-and-dotnet.md#mono-guard-and-recovery)). CE's own Mono
  functions do none of this, and a stalled call opens that modal dialog.
- Long work belongs in a tool built for it (such as `code_start_search` or `debugger_start_trace`, which run as jobs),
  or in several short chunks that each return a cursor.
- Timers (`createTimer`) and threads (`createThread`) outlive the call and cannot return values. Use them only when the
  user wants a lasting effect, and give them a way to stop (see below).

## Leave MCP's own state alone

The plugin's fixed scripts run in the same Lua state as your chunk, next to CE's own autorun scripts.

- Declare everything `local`. Never assign to a standard or CE global (`string`, `table`, `math`, `io`, `pcall`,
  `pairs`, `type`, `getAddressSafe`, `readInteger` and so on) and never give `_G` a metatable: every tool on that
  instance uses them, and the change lasts until CE closes.
- Do not touch globals whose names start with `__cheatengine_mcp`: they hold MCP's jobs, the resources it records in
  Lua (breakpoints, pause, speed), its Mono attachment and the `lua_execute` result.
- Do not flip CE-wide switches such as `errorOnLookupFailure`: it changes how every script, CE's own included, resolves
  string addresses. If a chunk must, restore the previous state that the call returns before the chunk ends.
- Leave `setSpecialScanOptionsOverride` alone: its scan-region override is CE-wide, and a named scanner's
  `scan_first.includeMapped`, `aob_find.includeMapped` and `aob_find_value.includeMapped` end every override, yours
  included, when their scan ends. Use those parameters to scan mapped memory ([emulator memory](emulators.md)).
- Do not replace global hooks. `debugger_start_trace` installs `debugger_onBreakpoint` while a trace runs and refuses
  while any other hook is installed; CE's Mono script chains `MainForm.OnProcessOpened` and its Java script chains
  `onOpenProcess`; CE's autorun scripts register Auto Assembler commands such as `USEMONO` ([debugger](debugger.md)).
- Do not change the target from Lua (`openProcess`, `openFileAsProcess`, `createProcess`). `process_attach`,
  `process_create` and `process_open_file` refuse while an MCP resource holds the current target and check the table's
  Uses Mono option first; a switch from Lua skips both, and releases that need the old process, such as MCP's pause or
  speed, then end as `cleanup_failed` ([releasing owned resources](errors-and-recovery.md#releasing-owned-resources)).
- Change what MCP owns only through its tools: patches with `asm_release_patch`, allocations with `memory_free`, named
  scanners with `scan_delete`, symbols with `symbol_unregister`, breakpoints with `debugger_delete_breakpoint`, the
  speed with `speedhack_set_speed(speed=1)`, its pause with `process_set_paused(paused=false)`, the Mono attachment
  with `mono_detach()`, jobs with `runtime_stop_job`, or all of them with `runtime_release_resources`. Freeing,
  resuming or detaching them from Lua leaves MCP's bookkeeping wrong.

## State created by Lua outlives the call

Globals, timers, threads, breakpoints (`debug_setBreakpoint`), registered symbols and Auto Assembler commands,
allocations, memory records, structures, patches, a pause, a speed or a Mono attachment made from Lua stay in CE and in
the target. MCP does not track them: `runtime_list_resources` does not list them, `runtime_release_resources` does not
release them, and disabling or re-enabling the plugin leaves them in place.

- Give your globals a unique prefix and set them to `nil` when you are done.
- Destroy what you create in the same chunk when you can (`list.destroy()`, `timer.destroy()`), and remove the
  breakpoints (`debug_removeBreakpointByID` with the id `debug_setBreakpoint` returned, or `debug_removeBreakpoint`)
  and symbols (`unregisterSymbol`) you added.
- When something must stay, tell the user what remains and how to remove it.

## celua.txt and `lua_find_api`

- The reference is `celua.txt` in the connected instance's Cheat Engine folder, so it matches that build. The server
  reads it inside CE and never returns it whole; an installation without it gives `not_found`.
- `lua_find_api(query="getAddressSafe")` returns the matching lines with their line numbers in `lines`, plus `total`.
  The match is a literal, case-insensitive substring. A page holds up to 100 lines (`limit`, default 20); pass
  `nextOffset` as the next `offset`: `lua_find_api(query="createTimer", limit=50, offset=...)`. A query of more than
  256 UTF-8 bytes, a `limit` outside 1 to 100 or an `offset` outside 0 to 65,536 fails with `limit_exceeded`.
- Search an exact function or property name; a broad word such as `address` matches hundreds of lines. Only the
  matching lines come back, not the section around them.
- The search reads at most 4 MiB and 65,536 lines and stops at a line longer than 8,192 bytes. At a bound it returns
  what it found, with `truncated` true and no `nextOffset`. If it runs out of dispatch budget it fails with `timeout`
  (a read: nothing changed): narrow the query and retry.
- Check the exact signature and argument order before every call; do not rely on memory or other CE versions. Many CE
  functions take positional arguments, so never drop an empty string or `false` from the middle of an argument list.
- Functions defined by CE's autorun scripts, such as the Mono helpers in `monoscript.lua`, are not in `celua.txt`.
- The text can disagree with the binary. `celua.txt` documents `splitDisassembledString` as returning address, bytes,
  opcode and extra, but CE's source pushes them in reverse, and CE 7.7's own `autorun/pseudocodediagram.lua` reads the
  third value as the bytes. Likewise CE 7.7's `setMemoryProtection` reads the keys `R`, `W` and `X`, not the lower-case
  ones `celua.txt` shows ([Lua API cheat sheet](lua-api.md)). Prefer `code_disassemble` to parsing disassembly text,
  and `memory_set_protection` to changing protection from Lua.

## Safe script patterns

An error-safe read that returns one table:

```lua
local ok, result = pcall(function()
  local base = getAddressSafe('game.exe')
  if base == nil then error('game.exe is not loaded') end
  local value = readInteger(base + 0x1A2B3C)          -- unsigned; readInteger(address, true) is signed
  if value == nil then error('read failed') end
  return { address = string.format('%X', base + 0x1A2B3C), value = value }
end)
if not ok then return { ok = false, error = tostring(result) } end
return { ok = true, data = result }
```

A bounded loop that returns a cursor, so long work becomes several short calls:

```lua
local first, cap = 0, 256          -- first: the nextIndex of the previous call
local list = getAddressSafe('game.exe+2F4E10')
if list == nil then return { ok = false, error = 'list base not found' } end
local step = targetIs64Bit() and 8 or 4
local items, index = {}, first
while index < first + cap do
  local entry = readPointer(list + index * step)
  if entry == nil or entry == 0 then break end
  items[#items + 1] = string.format('%X', entry)
  index = index + 1
end
return { ok = true, items = items, nextIndex = index, finished = index < first + cap }
```

- `pcall` turns a CE error into data you can report. Raise your own failures with `error('text')`: a non-string error
  value is reported only by its type.
- Prefer `getAddressSafe`, which returns `nil`, to `getAddress`, which raises by default. Reads return `nil` on
  failure: check them before any arithmetic.
- Cap every loop and every returned list, and return a cursor or a `truncated` flag.
- Free CE objects (string lists from `createStringlist()`, memory streams, disassemblers) inside the protected function
  and again on the error path.
- Before a mutating chunk, read and report the current value; afterwards, read it back.
- Each CE instance has its own Lua state: nothing crosses instances, and a handle from one means nothing in another.
  Through the gateway, send the chunk with the `instanceId` of the instance whose target it concerns.

## Sources

- Cheat Engine 7.7 `celua.txt`, `autorun/pseudocodediagram.lua`, `autorun/luasymbols.lua` and `autorun/monoscript.lua`
  in the installation folder (read locally, not redistributed).
- https://wiki.cheatengine.org/index.php?title=Lua
- https://www.lua.org/manual/5.3/manual.html#pdf-load
- https://www.lua.org/manual/5.3/manual.html#pdf-pcall
- https://www.lua.org/manual/5.3/manual.html#lua_Debug
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/symbolhandler.pas
