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
  error envelope. A backend's own result passes through unchanged, except that `cheatengine://instance/...` resource
  links gain the instance prefix; nothing is ever re-sent.
  Resources and prompts: the Local documents, workflow template and prompts are served by the gateway itself
  (`GatewayLocalPrimitivesSetup` over `McpLocalPrimitives`). `GatewayResourceRouter` adds `cheatengine://instances` (the
  instance list, never tokens, annotated for the assistant and the user with priority 0.6 and published with
  `instance_list` as its `cheatengine/sourceTool`) and the gateway form `cheatengine://instances/{instanceId}/...` of
  every backend live resource: a read is a prefix swap, refused without contacting any backend when the path is
  unknown, verified like a tool call through `GatewayBackendConnector`, and its content URIs are rewritten back.
  `resources/list` also lists the concrete live resources (process, runtime, jobs...) of every instance whose identity
  the gateway confirmed (`instance_list`, a read of `cheatengine://instances`, a routed call or read) while its registry
  record stays active, titled with the instance name and CE process id; `GatewayLiveInstances` reads them from the
  registry and the verifier's confirmed identities, never with an identity check, so the list is private with `ttlMs` 0.
  `GatewayResourceListMonitor` polls that set every second once a client listed resources and, when it differs from
  any listing served since the last notification and lasted one poll, raises `notifications/resources/list_changed`
  once through the resource collection, so the SDK delivers it exactly as it advertises `resources.listChanged`
  (session broadcast before 2026-07-28, `subscriptions/listen` streams after, never on a stateless transport).
  `GatewayCompletionRouter` completes a routed template's `instanceId` from the identities verified in the last 10 s
  (ids only), its `[AllowedValues]` variables from the gateway's own catalog, and its `[McpCompletion]` variables
  (module, structure, scanner and pointer scan names) by forwarding `completion/complete` in the backend's template
  form to the instance named by `context.arguments.instanceId`: only a single active record verified in the last 10 s,
  never an identity probe per keystroke, within 2 s (or the call timeout when shorter), and no values on any failure;
  only a transport failure evicts the pooled connection.

Both hosts advertise the `initialize` instructions the composition declared in the Core manifest: the backend its
`BackendInstructions`, the gateway its `GatewayInstructions`. The backend serves every composed resource and prompt
through `WithCheatEnginePrimitives`, like the tools, which also installs its `completion/complete` handler for the
`[McpCompletion]` variables of the live templates (cached per target-selection epoch, never blocking a keystroke).
The handler is one singleton of the web container, because the stateless HTTP transport configures new server options
for every request: its cache, its rate limit and its single running completion dispatch span every request.
Both hosts advertise the `completions` capability explicitly.

**Depends on:** Core, ASP.NET Core, `ModelContextProtocol.AspNetCore`. Never Tools, Resources, Prompts, the Client, NLog
or Cheat Engine UI: it sees primitives only through the Core manifest and borrows their instances.

**Belongs here when** it concerns how MCP requests reach a backend, not what a primitive does.
