# CheatEngine.Mcp.Hosting

The MCP transport, independent of any concrete primitive.

- `Backend/`: `McpBackendHost`, one authenticated loopback web host per activation, built from an empty ASP.NET Core
  builder so ambient settings never apply; `AddCheatEngineMcpBackend(configuration)` registers it in an activation.
- `Configuration/`: `McpBackendOptions` and `McpDiscoveryOptions` with generated validators, and the `MCP_*` source.
- `Discovery/`: the per-user instance registry, its token-bearing records (whose `ToString` redacts the token),
  `HostingJsonContext` (the source-generated JSON of the registry record, the `/instance` identity and the
  `instance_list` result) and `TokenSafeLogging`, which holds `Microsoft.AspNetCore`, `System.Net.Http` and
  `ModelContextProtocol` logs at Information or above.
- `Gateway/`: `AddCheatEngineMcpGateway(...)`, the stdio router. It adds the gateway-local `instance_list` tool and a
  required `instanceId` (first in every routed schema), verifies the backend identity before every call, reuses one
  pooled backend client per verified record (`BackendConnectionPool`, 32 entries, 5-minute idle lifetime, backend hop
  pinned to protocol 2025-06-18), forwards only W3C trace context from `_meta` (`GatewayMeta`), bounds each call with
  `--call-timeout-seconds`/`MCP_GATEWAY_CALL_TIMEOUT_SECONDS` (45 s by default) and reports its own failures in the v2
  error envelope. A backend's own result passes through unchanged; nothing is ever re-sent.

Both hosts advertise the `initialize` instructions the composition declared in the Core manifest: the backend its
`BackendInstructions`, the gateway its `GatewayInstructions`.

**Depends on:** Core, ASP.NET Core, `ModelContextProtocol.AspNetCore`. Never Tools, Resources, Prompts, the Client, NLog
or Cheat Engine UI: it sees primitives only through the Core manifest and borrows their instances.

**Belongs here when** it concerns how MCP requests reach a backend, not what a primitive does.
