# Lua execution

`lua_execute(instanceId, source, chunkName?)` uses the selected instance's Client IUnsafeLuaClient, enabled by default.
Setting `Mcp:EnableUnsafeLua` to `false` explicitly disables it at plugin enable, and a call then returns a structured failure before executing source.
Auto Assembler is also enabled by default through `Mcp:EnableAutoAssembler`.
Existing explicit `false` overrides take precedence over bundled defaults, and settings changes apply after disabling and re-enabling the plugin.
Lua globals and CE objects belong to that instance, so do not reuse another instance's handles or route a failed call elsewhere.

The Client owns protection, dispatch, and activation validity.
No raw Lua state or reference crosses the MCP boundary.
`lua_execute` returns bounded JSON copies of the chunk's return values, and functions, userdata, threads and Cheat Engine objects are dropped and counted.
An `ok:true` result has `hostEffect:"completed"`.
A compile error has `ok:false`, `phase:"compile"` and `hostEffect:"not_applied"`.
A runtime error has `ok:false`, `phase:"runtime"` and `hostEffect:"unknown"`, because the chunk may have already changed host or target state.
An oversized copied result returns a `limit_exceeded` MCP error after the chunk has run, rather than a partial result.

Prefer dedicated tools and Client high-level APIs. Features absent from Client use fixed implementation-owned scripts through its typed Lua operation boundary. Arguments are UTF-8 encoded data; protected results are copied before returning, with no raw Lua state escaping. Direct calls preserve multiple returns and nil slots; empty Lua tables are represented as arrays. Do not use arbitrary Lua to bypass capability/policy failures. Source can alter the host as well as its target, so keep execution within the user's authorized task.

For Cheat Engine Lua semantics, consult the installed build's celua.txt. Discover the installed path rather than assuming a fixed directory. Keep machine-specific notes in local-cheat-engine.md, which is excluded from version control and packaged output.

Debugger captures and step traces install fixed Lua callbacks and timers through this same Client boundary. Their bounded scalar results are polled later; no managed callback or opaque Lua object crosses MCP. Explicit stop is retryable, automatic expiry retains results for 30 seconds, and active jobs block interfering MCP debugger/target transitions. See [scanning-and-debugging.md](scanning-and-debugging.md) for trap-IP interpretation and cleanup details.
