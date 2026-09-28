# CheatEngine.Mcp.Core

The shared library every other project builds on, and the only one that references the `CheatEngine.Client` package.

- `Composition/`: `ICheatEngineMcpBuilder`, `AddToolType`/`AddResourceType`/`AddPromptType`/`AddJsonTypeInfoResolver`,
  the primitive manifest, `WithCheatEnginePrimitives` (serializer options from `CheatEngineMcpJson`, the error,
  feature-gate, argument-repair, resource-error, resource-gate and prompt-error filters and the completion bound),
  `McpLocalPrimitives` (the gateway's Local documents and prompts with the same filters), the schema-only catalog, the
  startup validator with the v2 naming and metadata rules (`McpContractRules`), the declared server instructions
  (`SetServerInstructions`) and schema options. Prompts and completions also get:
  - Argument titles: every prompt argument is listed with the `[Display(Name = "...")]` of its parameter as its
    `title`, which the SDK never fills; the validator refuses an argument without a sentence-case title of at most 60
    characters, or with the title of another argument of its prompt.
  - The completion bound (`McpCompletions.Bound`): every `completion/complete` answer of a backend, the catalog or the
    gateway keeps at most `McpCompletions.MaximumValues` (100) values, including the `[AllowedValues]` the SDK appends
    without a bound; a longer answer keeps its first values, reports the whole count as `total` and sets `hasMore`.

  Resources also get:
  - `[McpSourceTool(typeof(ToolType), toolName)]`, required on every live (`cheatengine://instance/...`) resource: it
    is published in `_meta` as `cheatengine/sourceTool`, the validator requires the tool to be a frozen backend tool
    that is read-only, closed-world, ungated and `short` (and served, when the server serves tools), and the read
    filter re-checks the tool's switches and any `[RequiresFeature]` on the resource itself. A Local
    (`cheatengine://docs/...`) resource declares neither, because the gateway serves it without any activation gate.
  - `[McpResourceAnnotations(Role.Assistant, Role.User, Priority = 0.5, LastModified = "2026-09-27T00:00:00Z")]`: the
    MCP annotations of a resource or template, copied into its listing entry at creation (like `size`), so backends, the
    schema-only catalog and the gateway list the same values. The validator refuses a priority outside 0 to 1, a
    `lastModified` without an offset, and an attribute that sets nothing.
  - `McpResourceQuery`: parses URI template variables (`Number`, `RequiredNumber`, `Required`) into `invalid_argument`
    refusals, never an `OverflowException`, and builds the canonical content URI (`Segment`, `WithQuery`). A resource
    read maps `invalid_argument`, `ArgumentException`, `FormatException` and `OverflowException` to `-32602`.
- `Contract/`: `CheatEngineToolNames` (the 187 reviewed v2 tool names: 186 backend tools and the gateway's
  `instance_list`) and the v2 error contract: `CheatEngineToolException`, `ToolError` and its `{"error":{...}}`
  envelope, `ToolErrorResults`, the Client failure mapping, the `snake_case` `ContractEnumConverter` and
  `CoreJsonContext`.
- `Values/`: contract value text: `HexFormat`, `HexParse`, `AobPattern`, `McpValueType`, `McpValueCodec`, `Paging`
  and `ResultFormat`.
- `Execution/ToolExecution`: runs one compound operation inside a Client dispatch and shapes `{ success, ... }` results.
- `Targets/TargetResources`: activation-owned target leases, released in reverse creation order.
- `Lua/LuaToolRuntime`: the protected, bounded Lua adapter and the plugin status script.
- `Runtime/McpRuntimeInfo`: the loaded plugin identity.

**Depends on:** `CheatEngine.Client`, `ModelContextProtocol`. Never ASP.NET Core, NLog, configuration loading,
discovery or a concrete tool.

**Belongs here when** two layers need it and it has no transport, host or UI concern.
