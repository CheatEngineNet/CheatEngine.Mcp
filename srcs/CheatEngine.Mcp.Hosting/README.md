# CheatEngine.Mcp.Hosting

The MCP transport, independent of any concrete primitive.

- `Backend/`: `McpBackendHost`, one authenticated loopback web host per activation, built from an empty ASP.NET Core
  builder so ambient settings never apply; `AddCheatEngineMcpBackend(configuration)` registers it in an activation.
- `Configuration/`: `McpBackendOptions` and `McpDiscoveryOptions` with generated validators, and the `MCP_*` source.
- `Discovery/`: the per-user instance registry and its token-bearing records.
- `Gateway/`: `AddCheatEngineMcpGateway(...)`, the stdio router that adds `instanceId` and `list_instances`.

**Depends on:** Core, ASP.NET Core, `ModelContextProtocol.AspNetCore`. Never Tools, Resources, Prompts, the Client, NLog
or Cheat Engine UI: it sees primitives only through the Core manifest and borrows their instances.

**Belongs here when** it concerns how MCP requests reach a backend, not what a primitive does.
