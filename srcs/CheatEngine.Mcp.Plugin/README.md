# CheatEngine.Mcp.Plugin

The Cheat Engine plugin (`CheatEngine.Mcp.Plugin.dll`) and the backend composition root. It is the only project with the
CheatEngine.Client plugin profile and a direct `CheatEngine.SDK` reference.

- `CheatEngineMcpPlugin`: `AddCheatEngineMcp(...).AddTools().AddResources().AddPrompts()`.
- `McpServerModule`: starts the backend on enable, withdraws it on disable, and never blocks CE's main thread.
- `McpStatusIndicator`: the CE menu status item, the only caller of the SDK UI exception.
- `Lua/`: `PluginLuaToolRuntime` and `PluginFixedLuaExecutor`, the fixed Lua bridge that Core reaches through
  `IFixedLuaExecutor`, which stays here because it works on the SDK's protected Lua state, and `PluginLuaJsonWriter`,
  which copies a Lua result into bounded JSON (depth 16, 8 MiB).
- `PluginLog` and `Logging/`, `McpPluginEnvironment`, the settings read once per activation (feature switches, log
  level, `Mcp:Files` for the Core `McpFilePaths` policy, `Mcp:Execution` limits) and the shipped `appsettings.json`.

**Depends on:** every product library, the Client and SDK packages.

**Belongs here when** it is about loading into Cheat Engine, the activation lifecycle or per-process settings.
