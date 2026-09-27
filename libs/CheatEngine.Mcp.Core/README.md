# CheatEngine.Mcp.Core

The shared library every other project builds on, and the only one that references the `CheatEngine.Client` package.

- `Composition/`: `ICheatEngineMcpBuilder`, `AddToolType`/`AddResourceType`/`AddPromptType`, the primitive manifest,
  `WithCheatEnginePrimitives`, the schema-only catalog, duplicate-name validation and schema options.
- `Execution/ToolExecution`: runs one compound operation inside a Client dispatch and shapes `{ success, ... }` results.
- `Targets/TargetResources`: activation-owned target leases, released in reverse creation order.
- `Lua/LuaToolRuntime`: the protected, bounded Lua adapter and the plugin status script.
- `Runtime/McpRuntimeInfo`: the loaded plugin identity.

**Depends on:** `CheatEngine.Client`, `ModelContextProtocol`. Never ASP.NET Core, NLog, configuration loading,
discovery or a concrete tool.

**Belongs here when** two layers need it and it has no transport, host or UI concern.
