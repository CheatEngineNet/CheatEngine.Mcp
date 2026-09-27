# CheatEngine.Mcp.Core

The shared library every other project builds on, and the only one that references the `CheatEngine.Client` package.

- `Composition/`: `ICheatEngineMcpBuilder`, `AddToolType`/`AddResourceType`/`AddPromptType`/`AddJsonTypeInfoResolver`,
  the primitive manifest, `WithCheatEnginePrimitives` (serializer options from `CheatEngineMcpJson`, the error,
  argument-repair and resource-error filters), the schema-only catalog, the startup validator with the v2 naming and
  metadata rules (`McpContractRules`), the declared server instructions (`SetServerInstructions`) and schema options.
- `Contract/`: `CheatEngineToolNames` (the 170 frozen v2 tool names) and the v2 error contract:
  `CheatEngineToolException`, `ToolError` and its `{"error":{...}}` envelope, `ToolErrorResults`, the Client failure
  mapping, the `snake_case` `ContractEnumConverter` and `CoreJsonContext`.
- `Values/`: contract value text: `HexFormat`, `HexParse`, `AobPattern`, `McpValueType`, `McpValueCodec`, `Paging`
  and `ResultFormat`.
- `Execution/ToolExecution`: runs one compound operation inside a Client dispatch and shapes `{ success, ... }` results.
- `Targets/TargetResources`: activation-owned target leases, released in reverse creation order.
- `Lua/LuaToolRuntime`: the protected, bounded Lua adapter and the plugin status script.
- `Runtime/McpRuntimeInfo`: the loaded plugin identity.

**Depends on:** `CheatEngine.Client`, `ModelContextProtocol`. Never ASP.NET Core, NLog, configuration loading,
discovery or a concrete tool.

**Belongs here when** two layers need it and it has no transport, host or UI concern.
