# CheatEngine.Mcp.Hosting

CheatEngine.Mcp.Hosting provides the transport and multi-instance routing layer for CheatEngine.Mcp.
It hosts one authenticated loopback MCP backend inside each enabled Cheat Engine plugin activation and supplies the stdio gateway services that route client requests to the selected activation.

This project contains transport and discovery code.
Concrete tools, resources, and prompts remain in their own projects and are composed through the Core manifest.
For end-user deployment and AI-client configuration, see the [repository README](../../README.md).

## Role in the product

~~~text
Cheat Engine process A                 Cheat Engine process B
plugin activation                      plugin activation
      |                                      |
      v                                      v
authenticated loopback backend         authenticated loopback backend
      |                                      |
      +---------- instance registry ---------+
                         |
                         v
                 stdio gateway process
                         |
                         v
                    MCP client
~~~

Each backend has its own Client activation and target state.
The gateway identifies every routed operation with an explicit instanceId and never chooses a backend from its display name.

The project references Core, Microsoft.AspNetCore.App, and ModelContextProtocol.AspNetCore.
It has no reference to Tools, Resources, Prompts, CheatEngine.Client, the Cheat Engine SDK, a logging-provider implementation, or Cheat Engine UI code.

## Project layout

| Directory | Purpose |
|---|---|
| [Backend](Backend) | Per-activation Kestrel host, service registration, lifecycle bridge, and plugin-owned logging abstraction |
| [Configuration](Configuration) | Backend and discovery options, environment mapping, and generated validation |
| [Discovery](Discovery) | Registry records, authenticated identity model, publication lifecycle, source-generated JSON, and token-safe logging |
| [Gateway](Gateway) | Stdio routing, schema catalogs, instance listing, backend-client pool, resources, completions, and resource-change notifications |

## Backend host

### Composition

AddCheatEngineMcpBackend accepts the activation IConfiguration and returns an ICheatEngineMcpBuilder in Backend mode.
It binds the Mcp section, registers generated validators, registers the backend factory, and resolves the activation's primitive targets.

The composition root adds execution services and declares the concrete Tools, Resources, and Prompts containers.
McpBackendHost then applies the resulting Core manifest through WithCheatEnginePrimitives.
Primitive instances are borrowed from the activation and remain owned by the Client-hosted activation lifecycle.

The plugin supplies two activation services:

| Service | Responsibility |
|---|---|
| IMcpBackendLogging | Creates a provider backed by the plugin log and determines the minimum host log level |
| McpRuntimeInfo | Supplies the plugin identity, version, content root, and server metadata |

### Listener and lifecycle

McpBackendHost uses an empty production WebApplication builder.
That isolates the server from the Cheat Engine process's ambient hosting configuration, command-line settings, appsettings files, and ASPNETCORE environment variables.

The backend:

- Binds only to 127.0.0.1.
- Uses port 0 by default so each activation receives an available local port.
- Uses stateless MCP-over-HTTP transport.
- Limits each HTTP request body to 8 MiB.
- Exposes MCP at the host's mapped endpoint.
- Exposes an authenticated GET /instance endpoint used only by the gateway to verify identity.
- Publishes its discovery record only after the listener starts successfully.
- Rejects new requests with HTTP 503 after StopAccepting begins.
- Stops and disposes asynchronously with a five-second shutdown bound.

StartAsync accepts exactly one start attempt.
StopAsync shares one shutdown task across repeated calls.
A failed start withdraws publication, releases the host, and releases its logging provider before the error reaches the plugin module.

ActivationHostLifetime prevents the embedded web host from responding to console signals or controlling Cheat Engine process lifetime.

### Backend configuration

The plugin adds its normal JSON configuration and then calls AddCheatEngineMcpEnvironment.
The MCP environment variables therefore override values from JSON for the current activation.

| Setting | Default | Environment variable | Validation |
|---|---|---|---|
| Mcp:Host | 127.0.0.1 | MCP_HOST | Literal loopback address |
| Mcp:Port | 0 | MCP_PORT | Integer from 0 through 65535 |
| Mcp:ServerName | CheatEngine.Mcp | None | Required |
| Mcp:InstanceName | Cheat Engine followed by the process ID | MCP_INSTANCE_NAME | Required, maximum 128 characters |
| Mcp:InstanceDirectory | LocalAppData CheatEngine.Mcp instances directory | MCP_INSTANCE_DIRECTORY | Absolute path |

The default instance directory is %LOCALAPPDATA%\CheatEngine.Mcp\instances.
Use the same directory for the plugin and the gateway when overriding it.
MCP_PORT parses before configuration binding so an invalid value reports its variable name clearly.

The backend also receives Core execution, feature, and file-policy settings from the plugin composition root.
Those settings are documented in the [Core README](../../libs/CheatEngine.Mcp.Core/README.md).

## Instance discovery

InstanceRegistry stores one small JSON record for every enabled plugin activation in the configured per-user directory.
The record contains the immutable instance ID, display name, activation ID, Cheat Engine process identity, local endpoint, access token, and plugin version.

InstancePublication creates a fresh activation ID and a cryptographically random token each time a plugin is enabled.
Its instance ID has the stable form ce-processId-activationId.
Display names can repeat, so callers must use instanceId for routing.

Publishing creates a temporary file and atomically moves it into the registry.
Disposal removes only the publication that owns its own record.
The reader accepts only:

- Active process records whose process ID and start time still match.
- Records with a valid instance ID and a 64-character hexadecimal access token.
- Literal loopback HTTP endpoints with an explicit port and no path, query, fragment, or user information.
- Files no larger than the registry safety bound.

Expired records are cleaned up after a fresh read confirms that the same activation is dead.
Malformed, inaccessible, concurrent, and transient records are ignored rather than treated as connection instructions.

### Authentication and identity verification

The registry is discovery data, so it is followed by an authenticated identity check.
The gateway sends a bearer-authenticated GET to the backend's /instance endpoint and compares the instance ID, activation ID, process ID, process start time, and plugin version with the registry record.

Every routed tool call and resource read re-reads the registry and verifies the selected backend before connecting.
The gateway retains only a one-way fingerprint of the endpoint and token after confirmation.
A changed record, failed identity check, withdrawn record, or unreachable backend removes the corresponding cached confirmation.

The gateway output never includes an endpoint or access token.
InstanceDescriptor also redacts its token from diagnostic string output.

## Gateway services

AddCheatEngineMcpGateway configures a host as a stdio gateway and returns an ICheatEngineMcpBuilder in Catalog mode.
Catalog mode creates the composed schema and metadata without creating a primitive container or a CheatEngine.Client activation.

GatewayCatalogWarmup builds the tool and routed-resource catalogs before the stdio transport starts.
An invalid schema therefore prevents the gateway from accepting a client connection.

### Tool routing

GatewayToolCatalog derives every backend tool from the shared manifest and adds a required string instanceId as the first input property.
The gateway owns instance_list, which has no instanceId and returns only verified instance IDs, display names, Cheat Engine process IDs, and plugin versions.

For every other tool call, GatewayRouter:

1. Checks that the name belongs to the routed catalog and that instanceId is present.
2. Re-reads the registry and requires one active record for the selected ID.
3. Verifies the authenticated backend identity.
4. Leases a client for that exact record.
5. Removes instanceId and forwards the operation once.

The forwarder preserves only W3C trace context from _meta: traceparent, tracestate, and baggage.
It removes upstream session metadata, progress state, and vendor metadata before the backend hop.

The backend protocol is pinned to 2025-06-18.
The upstream client independently negotiates its own protocol version with the gateway.

Gateway failures distinguish between a call that never reached the selected backend and a call whose final host effect is unknown.
The router never falls back to another instance and never retries a call.

### Connection pool

BackendConnectionPool reuses stateless MCP clients after identity verification.
Each key includes the instance ID, activation ID, endpoint, and the SHA-256 hash of the bearer token.
The token itself is never retained in a readable key or log message.

| Behavior | Production value |
|---|---:|
| Backend connection and initialize deadline | 10 seconds |
| Backend protocol version | 2025-06-18 |
| Reconnection attempts | 0 |
| Pool capacity | 32 clients |
| Idle lifetime | 5 minutes |

The pool evicts a client after a changed record, failed identity check, transport failure, call timeout, idle expiry, or least-recently-used pressure.
An evicted client closes after its final lease returns.

### Resources, prompts, and completions

Static documents, workflow bodies, and prompts are served locally by the gateway from the Core manifest.
They do not require a running Cheat Engine instance.

GatewayResourceRouter adds:

- cheatengine://instances, a private zero-TTL resource that lists verified instances without endpoints or tokens.
- A routed form of each live resource under cheatengine://instances/{instanceId}/.
- Concrete live resources for confirmed active instances.

The router verifies a selected instance before a routed read and rewrites backend live content URIs into gateway form.
An unknown or malformed gateway URI is refused before contacting a backend.

GatewayResourceListMonitor polls the confirmed resource set once per second after a client requests resources.
It raises notifications/resources/list_changed when a change remains visible for two consecutive polls and the selected transport can deliver the notification.

GatewayCompletionRouter supplies instanceId values from identities verified during the previous ten seconds.
It completes AllowedValues locally.
It forwards a marked live-template completion only to the instance named in its completion context, after that instance has recently passed identity verification.
The completion path avoids an identity probe on every keystroke, uses the smaller of two seconds and the configured call timeout, returns no values on failure, and keeps the MCP 100-value limit.

## Gateway configuration API

GatewayOptions is the public configuration contract used by the executable project.
Each setting resolves from command line first, then environment, then its default.
When an option appears more than once on the command line, the final occurrence takes precedence.

| Command line | Environment variable | Default | Rules |
|---|---|---|---|
| --instance-directory absolute-path | MCP_INSTANCE_DIRECTORY | %LOCALAPPDATA%\CheatEngine.Mcp\instances | Absolute, non-empty path |
| --call-timeout-seconds seconds | MCP_GATEWAY_CALL_TIMEOUT_SECONDS | 45 seconds | Whole number from 5 through 3600 |

An unknown argument, missing value, relative directory, blank directory, or invalid timeout prevents startup before the host runs.
The gateway executable README has operational examples and Native AOT packaging details.

## Logging and token safety

HTTP, MCP transport, and System.Net.Http categories receive an Information floor even when a host requests a more verbose level.
That protects bearer tokens from low-level request and response diagnostics.

The embedded backend sends its logs to the plugin-owned provider.
The gateway clears default providers and sends logs to stderr.
Its stdout remains exclusively available for MCP JSON-RPC messages.

TokenSafeLoggingTests and GatewayTokenLeakTests cover the logging floor and token-safe failure paths.

## Verification

The [Hosting tests](../../tests/CheatEngine.Mcp.Tests/Hosting) cover backend startup and shutdown, environment parsing, discovery publication and cleanup, identity verification, authentication, multi-instance routing, error behavior, connection pooling, resource routing, list-change notifications, completion forwarding, and token-safe logs.

Contract and architecture tests additionally verify manifest parity, public schemas, generated JSON, and project boundaries.
Run the standard portable suite from the repository root:

~~~powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
~~~

The full contributor workflow and release checks are in [CONTRIBUTING.md](../../CONTRIBUTING.md).
