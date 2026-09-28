# Write and run a lua_execute script safely

## Goal

Write, show and run one bounded `lua_execute` chunk that does this: {purpose}. The declared effect is `{effect}`
(read_only reads only; mutating changes the game or Cheat Engine). Use Lua only for a Cheat Engine feature that no
tool covers, only on single-player or offline software the user owns or may modify.

## Steps

1. Look for a dedicated tool first in the [tool map](../Documents/tool-map.md), such as `record_set_dropdown`,
   `scan_get_status` (main's scan settings) or `mono_get_object`: tools validate arguments, report `hostEffect` and
   track what they leave. If one tool, or a few in sequence, covers the purpose, use it and stop.
2. `lua_find_api(query="<function>")` for every CE function the chunk calls: copy the exact signature and argument
   order of this install; page with `nextOffset`. Functions from CE's autorun scripts are not listed there.
3. Draft the chunk:
   - everything `local`; the work inside `pcall`, returning one table such as `{ ok = true, data = ... }`;
   - every loop capped, long lists paged with a returned cursor, addresses as `string.format('%X', a)`;
   - CE objects (string lists, MemScans, disassemblers) destroyed on the success and the error path;
   - no `sleep`, `processMessages`, dialogs or waits: it holds CE's main thread (and its window) until it returns;
   - no Mono functions (the Mono tools guard CE's pipe) and no `openProcess`: a target switch breaks MCP's releases;
   - read_only: only reading functions; mutating: read and return the old value, change it, read it back.
4. Show the full source and say what it reads, what it changes and what stays after it (globals, timers, breakpoints,
   symbols, allocations). With `{effect}` mutating, wait for explicit consent to this exact source; with read_only,
   still get a go-ahead.
5. `lua_execute(source="<the chunk>", chunkName="=mcp-script")`.
6. Read the result:
   - `ok` true, `hostEffect` `completed`: use `returnValues`. A `droppedOpaqueCount` above 0 means functions or CE
     objects were returned; return their fields instead.
   - `phase` `compile`, `hostEffect` `not_applied`: nothing ran; fix what `error` names and show the source again.
   - `phase` `runtime`, `hostEffect` `unknown`: what ran before the error stays done. Read the state it touches, for
     example with `memory_read(address="<address>", valueType="int32")`, before any re-run.
7. After a mutating chunk, confirm the change with a dedicated read tool, and remove what the chunk left unless the user
   wants it to stay.

## Decisions

- `capability_disabled`: `Mcp:EnableUnsafeLua` is off. Tell the user and stop. Never rephrase an action a setting
  refused as Lua, an Auto Assembler `{$lua}` block, a cheat table or an address expression (`$name` runs Lua).
- The user declines a mutating chunk: stop, or offer a read-only probe.
- A read_only draft that turns out to need a write is mutating: show it again and ask.
- Long work: split it into short chunks that each return a cursor, or use a tool that runs as a job.
- A lasting effect (timer, thread, breakpoint callback) only when the user wants one, with a chunk that stops it.
- Anti-cheat bypass, DRM or licence cracking and detection evasion: refuse.

## Pitfalls

- `print` output is not returned; only `return` values are.
- CE functions take positional arguments: never drop an empty string or `false` from the middle of a call.
- Never call `resetLuaState`, `closeCE` or `loadPlugin`, never assign CE or standard globals, and never replace global
  hooks that the tools use: every tool on this instance shares the Lua state. Leave `__cheatengine_mcp` globals and
  `setSpecialScanOptionsOverride` alone: includeMapped scans end that override.
- NaN, infinities, cyclic or mixed tables fail the copy after the chunk ran (`hostEffect` `completed`): return
  `tostring(x)` and plain tables, and do not resend a chunk only to get its values.
- Return 64-bit values and addresses as strings; JSON readers round integers above 2^53.
- A gateway `timeout` leaves the chunk possibly still running (`hostEffect` `unknown`): inspect before anything else.
- MCP does not track Lua state: `runtime_list_resources()` never lists it and a release never removes it.

## Report

The source (or a summary), `ok`, `phase` and `hostEffect`, the returned values as a table, and for a mutating chunk
the value before and after. List all state left behind (globals, timers, breakpoints, symbols, allocations, records)
with the chunk that removes it. Background: [Lua](../Documents/lua.md), [Lua API](../Documents/lua-api.md) and
[safety](../Documents/safety.md).
