# Lua in CheatEngine.Mcp

Cheat Engine embeds Lua 5.3 with its own API (celua.txt). CheatEngine.Mcp uses Lua in two very different ways; know
which one you are calling.

## Fixed-script tools versus `lua_execute`

|                    | Fixed-script tools                                                                                                               | `lua_execute(source, chunkName?)` |
|--------------------|----------------------------------------------------------------------------------------------------------------------------------|-----------------------------------|
| Who writes the Lua | The plugin (reviewed, fixed bodies)                                                                                              | You                               |
| Your input         | Data arguments only, never spliced into source                                                                                   | Arbitrary source                  |
| Gate               | The capability they touch (`EnableAutoAssembler`, `EnableTargetCodeExecution`, `EnableKernelAccess`), checked at one choke point | `EnableUnsafeLua`                 |
| Output             | Typed record with a schema, bounded                                                                                              | Bounded copy of return values     |
| Cleanup            | Tracked (resources, jobs, leases)                                                                                                | Not tracked                       |

Always prefer a dedicated tool: it validates arguments, guards CE's main thread, reports `hostEffect`, and registers
anything it leaves behind. Reach for `lua_execute` only for a documented CE feature no tool covers, and say so to the
user.

## `lua_execute`

- Runs your `source` as one chunk through the Client's unsafe Lua capability; `chunkName` only labels errors.
- Returns `ok`, `phase`, `error` and `hostEffect` with its bounded values.
  `ok:true` reports `hostEffect:"completed"`, a compile error reports `not_applied`, and a runtime error reports `unknown`.
- Compile and runtime errors are normal `ok:false` tool outcomes.
  They use `isError` only if result copying or the Client dispatch itself fails.
- Returns `returnValues[]`: every value your chunk returns, in order. Tables are copied recursively.
- Functions, userdata and CE objects (memory records, forms, streams) cannot cross MCP: they are dropped and counted in
  `droppedOpaqueCount`. Return their fields instead (`mr.Description`, `mr.Address`).
- Copies are bounded (about 4 MiB of string data, 65,536 elements, nesting depth 16, 8 MiB of JSON).
  A larger result returns `limit_exceeded` after the chunk has already run, and no partial result is returned.
  Return summaries, not dumps.
- Format addresses yourself as uppercase hex: `string.format('%X', addr)`. In Lua source, hex literals need `0x`
  (`base + 0x1A2B3C`), unlike Auto Assembler.
- A syntax or runtime error returns `isError`. Anything the chunk did before the error stays done: read state before
  running it again.

## Not a sandbox

- The chunk runs with CE's full Lua: file I/O, `os`, `autoAssemble`, `executeCode*`, `injectLibrary`, `dbk_*`, `dbvm_*`,
  CE settings. With `EnableUnsafeLua` on, the other gates are advisory; they only bind the dedicated tools.
- Never use Lua to do what a gated tool refused. On `capability_disabled`, report which gate is off and stop; do not
  rephrase the action as a script.
- Never read the MCP instance registry, access tokens, CE settings files or the user's files from Lua.
- Stay within the user's authorized target and task. Lua can change the host as well as the game.

## celua.txt and `lua_find_api`

- The authoritative API reference is `celua.txt` in the user's Cheat Engine folder, matching the installed build (7.7).
  It is read locally, never redistributed.
- `lua_find_api(query, limit?, offset?)` searches it and returns matching lines.
  It searches at most 4 MiB and 65,536 source lines, returning the bounded prefix with `truncated:true` and no
  `nextOffset` when either source-file bound is reached.
  Check the exact signature and argument order before writing a call; do not rely on memory or on other CE versions.
- Where MCP has verified a mismatch, the tool wins over the text: for example the column order of
  `splitDisassembledString` on CE 7.7 differs from celua.txt, so use `code_disassemble` rather than parsing disassembly
  strings.

## CE's main thread

`lua_execute` runs synchronously on CE's GUI thread. While it runs, CE's window freezes and every other MCP call to that
instance waits. Keep chunks short, well under a second.

- No `sleep`, `waitTillDone`, `thread.waitfor` or other waits for symbols, scans or threads.
- No `processMessages`: it lets timers, breakpoint callbacks and other scripts run in the middle of your chunk.
- No `showMessage`, `messageDialog`, `inputQuery`, forms or any `*_dialog` helper: a modal blocks until a human clicks.
- No whole-address-space loops or unbounded scans; use `aob_find`, `scan_*` or module-scoped ranges.
- No Mono calls from Lua: use the `mono_*` tools, which guard the pipe ([mono-and-dotnet](mono-and-dotnet.md)).
- Do not replace global hooks such as `debugger_onBreakpoint`, `MainForm.OnProcessOpened` or registered AA commands, and
  do not touch the plugin's own globals (`mcp`, `__cheatengine_mcp_*`).
- Long work belongs in an MCP job. If Lua must run asynchronously, a `createTimer` callback doing small steps is the
  pattern, but it is state you must clean up.

## State created by Lua outlives the plugin

Globals, timers, threads, breakpoints (`debug_setBreakpoint`), registered symbols and AA commands, allocations, memory
records, structures and patches made from Lua live in CE's Lua state and the target. MCP does not track them:
`runtime_list_resources` does not list them, and plugin disable or re-enable does not remove them (CE's Lua state
normally lives until CE closes).

- Prefix your globals uniquely and `nil` them when done.
- Destroy what you create in the same chunk when possible (`sl.destroy()`, `t.destroy()`), and remove breakpoints and
  symbols you added.
- Tell the user what remains when you cannot clean it up.

## Error-safe pattern

```lua
local ok, result = pcall(function()
  local base = getAddressSafe('game.exe')
  if base == nil then error('game.exe is not loaded') end
  local value = readInteger(base + 0x1A2B3C)
  if value == nil then error('read failed') end
  return { address = string.format('%X', base + 0x1A2B3C), value = value }
end)
if not ok then return { ok = false, error = tostring(result) } end
return { ok = true, data = result }
```

- `pcall` turns a CE error into data you can report; use `error()` for your own checks.
- Prefer the `*Safe` lookups (`getAddressSafe`) that return `nil` instead of raising.
- Free objects inside the protected function and again on the error path when they would leak (string lists, memory
  streams, disassemblers).
- When embedding text into another language (an AA script string), quote it with `string.format('%q', text)`.
- Each CE instance has its own Lua state: nothing crosses instances, and handles from one are meaningless in another.

## Sources

- https://wiki.cheatengine.org/index.php?title=Lua
- https://www.lua.org/manual/5.3/manual.html#pdf-pcall
- https://www.lua.org/manual/5.3/manual.html#6.1
- https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/LuaHandler.pas
