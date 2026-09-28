# CheatEngine.Mcp.Gateway

CheatEngine.Mcp.Gateway is the Windows x64 Native AOT executable that exposes Cheat Engine MCP over standard input and output.
An MCP client starts this executable once, and the gateway discovers enabled local Cheat Engine plugin instances and routes each operation to the instance selected by instanceId.

The gateway is the client-facing composition root.
For plugin installation, Cheat Engine runtime preparation, and AI-client setup, use the [repository README](../../README.md).
For the shared routing implementation, see the [Hosting README](../CheatEngine.Mcp.Hosting/README.md).

## Runtime behavior

GatewayProgram builds a generic host, configures it with AddCheatEngineMcpGateway, composes the same Tools, Resources, and Prompts chain as the plugin, and runs until the MCP stdio client disconnects.

~~~text
MCP client
    |
    | JSON-RPC over stdin/stdout
    v
CheatEngine.Mcp.Gateway.exe
    |
    | reads per-user instance records and verifies identity
    v
one selected plugin backend per operation
~~~

The gateway composes primitives in Catalog mode.
It builds their schemas and metadata while avoiding construction of concrete primitive types and avoiding a CheatEngine.Client activation.
GatewayCatalogWarmup resolves the tool and resource-template catalogs before the stdio transport opens, so a schema problem fails startup before a client can use a partial server.

The gateway advertises serverInfo.name as CheatEngine.Mcp.Gateway and uses the executable assembly version.
The composed server instructions, static knowledge documents, workflow bodies, and prompts come from the same manifest used by the plugin.

### Instance routing

The gateway offers instance_list as its local tool.
It verifies active registry candidates and returns the available instance IDs, names, Cheat Engine process IDs, and plugin versions.
The result excludes discovery tokens and backend endpoints.

Every composed backend tool has a required instanceId argument added by the gateway.
For a routed call, the gateway reads the current registry, checks the authenticated /instance identity endpoint, connects to that exact backend, removes instanceId, and forwards the request once.

The gateway keeps the instance boundary strict:

- A duplicate, withdrawn, or missing registry record produces an unavailable-instance response before a backend call.
- A failed identity check removes the confirmation and pool entry for that instance.
- A request is never redirected to a different instance.
- A request is never replayed after an error or timeout.
- Only traceparent, tracestate, and baggage travel from upstream _meta to the backend hop.
- The backend hop uses MCP protocol version 2025-06-18 independently of the upstream negotiated version.

Routed resources use cheatengine://instances/{instanceId}/ paths.
The gateway serves documents and workflow prompts locally, then verifies and forwards reads of instance-specific resources.
It rewrites returned live resource URIs back into gateway form.

### Timeouts and connection reuse

The default routed-call timeout is 45 seconds.
When it expires, the gateway evicts the pooled client and reports a timeout with an unknown host effect.
It does not issue a second call.

The backend-client pool uses one stateless MCP client for a verified record.
Its key includes the instance ID, activation ID, endpoint, and a SHA-256 token fingerprint.
The pool has a 32-client capacity and a five-minute idle lifetime.
It creates a connection and performs the backend initialize handshake within ten seconds, with zero reconnection attempts.

The gateway uses stderr for diagnostics and reserves stdout for JSON-RPC traffic.
Token-safe logging rules keep bearer credentials out of transport diagnostics.

## Command-line configuration

The executable requires no configuration for the default local installation.
Each setting resolves in this order: command-line argument, environment variable, default.
If the same command-line option appears more than once, its final occurrence wins.

| Option | Environment variable | Default | Requirements |
|---|---|---|---|
| --instance-directory absolute-path | MCP_INSTANCE_DIRECTORY | %LOCALAPPDATA%\CheatEngine.Mcp\instances | Fully qualified and non-empty path |
| --call-timeout-seconds seconds | MCP_GATEWAY_CALL_TIMEOUT_SECONDS | 45 | Whole number from 5 through 3600 |

The selected instance directory must match the directory configured for every plugin instance.
The directory can be absent when the gateway starts and is read when instances are listed or routed.

Examples:

~~~powershell
# Use the normal per-user registry and the default 45-second timeout.
& 'C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe'

# Use a shared custom registry for this gateway process.
& 'C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe' --instance-directory 'D:\CheatEngine\McpInstances'

# Set a longer routed-call deadline for this process.
& 'C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe' --call-timeout-seconds 120
~~~

Unknown options, missing values, relative paths, blank registry paths, and malformed or out-of-range timeouts stop the process during host construction.
The environment timeout treats an empty or whitespace value as absent.

Do not run the executable by double-clicking it as a standalone configuration step.
Your MCP client should launch it through its stdio server configuration.
The [repository README](../../README.md#4-connect-an-ai-client) contains client configuration examples.

## Build and publish

The project targets net10.0 and declares win-x64 as its runtime identifier.
It is the sole Native AOT publish target in the product graph.
The referenced Hosting, Tools, Resources, and Prompts projects remove Native AOT and runtime-identifier properties so the executable owns the publish decision.

The gateway disables reflection-based JSON by default.
Every payload supplied by this executable and every payload reachable through its composed catalog requires source-generated JSON metadata.

Standalone.pubxml defines:

| Property | Value |
|---|---|
| RuntimeIdentifier | win-x64 |
| PublishAot | true |
| StripSymbols | true |
| Native AOT lock file | packages.aot.lock.json |
| Publish output | artifacts/publish/CheatEngine.Mcp.Gateway/configuration-nativeaot |

The portable project restore uses [packages.lock.json](packages.lock.json).
The Native AOT publish restore uses [packages.aot.lock.json](packages.aot.lock.json) because the compiler package is an implicit Native AOT dependency.

From the repository root, publish the complete distribution through the repository script:

~~~powershell
pwsh -NoProfile -File eng/Publish.ps1
~~~

The script builds the plugin deployment, publishes this executable with the Standalone profile, copies CheatEngine.Mcp.Gateway.exe into artifacts/dist/release, and runs a stdio smoke test.
The smoke test checks initialization, instance_list, resource listing, resource-template listing, prompt listing, JSON-RPC-only stdout, and graceful exit after stdin closes.

For a debug distribution:

~~~powershell
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Debug
~~~

Publishing Native AOT on Windows requires the Visual Studio Build Tools or Visual Studio C++ desktop workload and the Windows SDK.
The repository pins .NET SDK 10.0.401 with roll-forward disabled.

## Contributor guidance

Keep GatewayProgram small.
It should remain the composition root that calls AddCheatEngineMcpGateway followed by AddTools, AddResources, and AddPrompts.

Put transport-independent gateway routing behavior in [Hosting](../CheatEngine.Mcp.Hosting).
Put shared schema and contract behavior in [Core](../../libs/CheatEngine.Mcp.Core).
Add a concrete tool, resource, or prompt in its corresponding primitive project.

Changes that affect tool schemas, resource templates, prompts, or initialization require review of the contract golden files.
Changes that affect Native AOT payloads require generated JSON metadata and an AOT publish verification.

## Verification

The standard portable test suite includes gateway option parsing, multi-instance routing, identity verification, connection pooling, error mapping, resource routing, resource-list notifications, completions, token-safe logging, catalog parity, and executable completion coverage.

Run it from the repository root after restore and build:

~~~powershell
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
~~~

Run the publish script for the Native AOT executable smoke check:

~~~powershell
pwsh -NoProfile -File eng/Publish.ps1
~~~

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for the complete verification gates, formatting rules, lock-file workflow, and contract snapshot process.
