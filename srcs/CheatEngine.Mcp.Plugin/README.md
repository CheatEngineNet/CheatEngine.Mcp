# CheatEngine.Mcp.Plugin

The Cheat Engine plugin (`CheatEngine.Mcp.Plugin.dll`) and the backend composition root. It is the only project with the
CheatEngine.Client plugin profile and a direct `CheatEngine.SDK` reference.

- `CheatEngineMcpPlugin`: `AddCheatEngineMcp(...).AddTools().AddResources().AddPrompts()`.
- `McpServerModule`: starts the backend on enable, withdraws it on disable, and never blocks CE's main thread.
- `McpStatusIndicator`: the CE menu status item, the only caller of the SDK UI exception.
- `PluginLog`, `McpFeatureOptions`, `McpPluginEnvironment` and the shipped `appsettings.json`.

**Depends on:** every product library, the Client and SDK packages, NLog.

**Belongs here when** it is about loading into Cheat Engine, the activation lifecycle or per-process settings.
