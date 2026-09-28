# CheatEngine.Mcp.Core

CheatEngine.Mcp.Core is the shared kernel for the CheatEngine.Mcp product.
It defines the MCP composition model, contract rules, execution boundary, target lifetime model, jobs, fixed Lua support, file policy, value codecs, and runtime identity used by the plugin and the gateway.

This project is a library for product contributors.
For installation, client configuration, and end-user workflows, start with the [repository README](../../README.md).

## Responsibilities and boundaries

| Area | Core owns | Other projects own |
|---|---|---|
| MCP composition | Primitive manifest, registration helpers, JSON resolver ordering, contract validation, catalog construction, and shared request filters | Concrete tools, resources, prompts, and the host composition roots |
| Public contract | Tool-name inventory, result error envelope, error kinds, host-effect semantics, and shared value representations | Domain-specific argument and result records |
| Cheat Engine work | Bounded dispatch, feature gates, fixed Lua execution rules, metrics, and Client failure mapping | The domain operations implemented through Client APIs or fixed Lua bodies |
| Activation state | Target resources, transition guards, state ledger, and bounded background jobs | The resources and jobs created by individual domains |
| Local files | Write-root policy and Windows-safe anchored operations | Plugin configuration binding and the tools that request a read or write |

Core references CheatEngine.Client and ModelContextProtocol.
It centralizes the shared Client integration, while the Plugin supplies activation-specific Client hosting.
It has no dependency on ASP.NET Core, the Cheat Engine SDK, a logging-provider implementation, a transport, a concrete primitive project, or the Cheat Engine UI.

Place code here when at least two product layers need it and it remains independent of a specific host and a specific tool domain.

## Project layout

| Directory | Purpose |
|---|---|
| [Composition](Composition) | Explicit primitive registration, manifests, schema catalogs, resource URI handling, completion support, contract validation, and MCP request filters |
| [Contract](Contract) | The frozen tool-name inventory and the versioned error contract shared by tools and hosts |
| [Execution](Execution) | The activation-scoped dispatch facade, admission limits, Client dispatch boundary, and execution measurements |
| [Features](Features) | Exposure switches and the feature-gate helpers used before host work begins |
| [Files](Files) | Host-file write policy, path validation, and Windows anchored file operations |
| [Jobs](Jobs) | Bounded managed and Lua-backed jobs with polling, stopping, expiry, and cleanup |
| [Lua](Lua) | Fixed-script construction, bounded JSON transfer, result copying, and unsafe-Lua result handling |
| [Targets](Targets) | Target-owned resource tracking, reverse-order release, transition guards, and the activation state ledger |
| [Values](Values) | Hex, AOB, paging, result-format, and MCP value codecs |
| [Tables](Tables) | Cheat-table format and inspection helpers |
| [Runtime](Runtime) | The plugin runtime identity made available to hosting |

## Composition model

Every product host composes the same declared primitive set through the fluent builders supplied by Tools, Resources, and Prompts.
Core records that declaration in an ordered manifest instead of scanning assemblies.

~~~text
Tools / Resources / Prompts
          |
          v
ICheatEngineMcpBuilder manifest
          |
          +--> Backend mode: activation-scoped primitive instances
          |
          +--> Catalog mode: schemas and metadata only
                     |
                     v
             Gateway routing catalog
~~~

The main extension points are:

| API | Use |
|---|---|
| CheatEngineMcpBuilder | Carries an IServiceCollection and the selected Backend or Catalog mode |
| AddToolType, AddResourceType, AddPromptType | Adds an explicitly declared MCP container type to the manifest |
| AddJsonTypeInfoResolver | Adds source-generated JSON metadata for a primitive project's request and result types |
| SetServerInstructions | Declares the initialize instructions for the backend and the gateway once per composition |
| AddExecutionServices | Registers the activation-scoped dispatch, gates, targets, jobs, and related services in Backend mode |
| WithCheatEnginePrimitives | Materializes a validated manifest in an MCP server with the shared serializer options and filters |
| McpPrimitiveCatalog.Create | Builds the schema-only catalog that the gateway uses before it accepts stdio traffic |

A primitive project should follow this pattern in its public builder extension:

~~~csharp
public static ICheatEngineMcpBuilder AddExamplePrimitives(this ICheatEngineMcpBuilder builder)
{
    return builder
        .AddJsonTypeInfoResolver(ExampleJsonContext.Default)
        .AddToolType<ExampleTools>()
        .AddResourceType<ExampleResources>();
}
~~~

The plugin composes these builders in Backend mode and resolves one target set for its activation.
The gateway composes the same builders in Catalog mode and constructs schemas without constructing primitive containers or a CheatEngine.Client activation.

Register a resolver for every serializable public payload.
The gateway disables reflection-based JSON and publishes Native AOT, so a payload that lacks generated metadata is a product defect.

### Registration and ownership rules

- Register primitives through AddToolType, AddResourceType, and AddPromptType.
- Keep the declarations explicit and deterministic.
- Use source-generated JsonSerializerContext metadata for public payloads.
- Backend-mode instance containers are activation-scoped and are borrowed by the MCP host.
- The MCP host never constructs or disposes an activation-owned primitive instance.
- Static local resources and prompts remain unconstructed.
- Let startup validation reject duplicate identifiers and invalid contracts before a server begins serving requests.

## MCP contract rules

Core validates the public contract when the manifest is resolved for a backend or a catalog.
The validation keeps the gateway and each backend aligned and catches an invalid addition during startup or schema generation.

The important rules for new primitives are:

- Tool names belong in [CheatEngineToolNames.cs](Contract/CheatEngineToolNames.cs), which is the reviewed v2 inventory.
- A tool declares its title, description, input schema, output schema, annotations, dispatch class, and host-effect behavior.
- Tool, resource, and prompt identifiers use the repository's v2 naming rules and remain unique across the composed set.
- Live resources use the cheatengine://instance/ URI space and declare McpSourceTool for the read-only, closed-world, ungated, short tool result they project.
- Gateway-local documents use the cheatengine://docs/ URI space and static methods.
- The gateway reserves cheatengine://instances and adds its instanceId route segment itself.
- Resource template variables and prompt arguments are strings with descriptions.
- Prompt arguments carry a Display Name that becomes the client-visible title.
- Resource annotations validate audience, priority, and timestamp data before publication.
- Completion values are bounded to the MCP maximum of 100 entries, including values that the SDK adds for AllowedValues.

McpPrimitiveCatalog, WithCheatEnginePrimitives, and McpLocalPrimitives share these rules.
Do not create a gateway-only schema by copying a backend schema manually.

### Errors and request filters

The tool error model is a structured envelope with an error kind, an optional operation, a host-effect state, retryability, optional details, and a caller-safe hint.
CheatEngineToolException carries that model across domain and host layers.
ToolFailureMapping translates CheatEngine.Client failures into the same contract.

WithCheatEnginePrimitives installs the common filters:

- Feature checks run before a gated call starts.
- Argument normalization accepts compatible serialized objects, arrays, nullable values, and boolean values before binding.
- Tool, resource, and prompt failures use the consistent MCP error mapping.
- Unexpected internal faults receive a random errorId that ties the client-visible error to a token-safe log event.
- Resource reads recheck the source tool and resource feature requirements.
- Completion responses are capped at 100 values.

When implementing an operation, report its real host effect.
Use notStarted when rejection happened before work began, partial when only part of a compound change completed, and unknown when a timeout or interruption leaves the final state uncertain.

## Execution, features, and target lifetime

### ToolDispatch

Tools inject ToolDispatch to execute one complete Cheat Engine operation through the Client dispatch boundary.
The facade admits a bounded number of requests, measures queue and execution time, maps Client failures, and ensures an unexpected exception becomes a caller-safe internal error.

Every implementation-owned Lua body passes through ToolDispatch.
The body is scanned for sensitive APIs, checked against the relevant feature switches, built with caller values supplied as data, and executed with bounded JSON result handling.
Caller-authored Lua follows the separate unsafe-Lua path and requires its explicit feature switch.

Bind the execution section in the composition root:

| Setting under Mcp:Execution | Default | Valid range | Meaning |
|---|---:|---:|---|
| DispatchBudgetMilliseconds | 100 | 1 to 10000 | Warning threshold for time spent on Cheat Engine's main thread |
| MaxConcurrentDispatches | 4 | 1 to 64 | Requests admitted at one time |
| MaxJobs | 16 | 1 to 64 | Jobs held by one activation |
| JobDefaultTtlSeconds | 120 | 1 to 300 | Lifetime used when a caller supplies no job lifetime |
| JobMaxTtlSeconds | 300 | 1 to 300 | Largest permitted job lifetime |
| JobBufferLimit | 4096 | 1 to 65536 | Buffered job items retained for polling |

The dispatch budget logs an overrun and allows the current operation to finish.
Fixed scripts observe the budget cooperatively through the MCP Lua prelude.

### Feature gates

McpFeatureOptions is bound from the flat Mcp section and defaults every switch to enabled.
The plugin reads the options once per activation and uses the same values for Client opt-ins and MCP request gates.
Changes take effect when the plugin is disabled and enabled again.
When a switch is disabled, each covered operation is refused before argument binding or Cheat Engine work starts.

| Setting | Controls |
|---|---|
| EnableUnsafeLua | Caller-authored Lua execution and tables carrying Lua |
| EnableAutoAssembler | Caller-supplied Auto Assembler checks and patches |
| EnableTargetCodeExecution | Target or Cheat Engine code execution, injection, Mono operations, compilation, and speedhack |
| EnableKernelAccess | DBK and DBVM operations, including physical and kernel memory access |

These settings control MCP request admission.
They do not reduce the privileges of features that remain enabled.

### Resources and jobs

TargetResources tracks activation-owned effects such as scanners, symbols, patches, breakpoints, and leases.
It releases resources in reverse creation order during target transitions and activation shutdown.
TargetTransitionGuards coordinate that release with other domain guards before a process changes.

JobRegistry owns long-running managed and Lua-backed jobs.
Jobs have validated time-to-live values, bounded polling buffers, explicit stop paths, and cleanup that runs when a job expires or the activation ends.
Long-running work belongs in a job instead of occupying an MCP request indefinitely.

### File policy

McpFileOptions binds from Mcp:Files.
AllowedRoots is empty by default, so tools refuse all host-file writes until the plugin configuration lists absolute local directories.
The policy rejects UNC paths, device paths, alternate data streams, reserved device names, duplicate roots, and paths escaping their approved root.
The MCP data directory and the instance registry remain excluded from writes.

## Adding or changing Core code

1. Keep Core independent of ASP.NET Core, UI code, concrete primitive projects, and logging providers.
2. Add a public tool name through the reviewed inventory and update the public contract tests and golden snapshots when the contract changes.
3. Add generated JSON metadata with the payload type that needs it.
4. Route every live operation through ToolDispatch and return a complete success or error contract.
5. Track every target-owned effect through TargetResources or JobRegistry and provide a cleanup path.
6. Use fixed implementation-owned Lua with typed arguments and a generated result type.
7. Extend the manifest validator when a new cross-project invariant is needed.

## Verification

Core behavior is covered primarily by [Core tests](../../tests/CheatEngine.Mcp.Tests/Core), [architecture tests](../../tests/CheatEngine.Mcp.Tests/Architecture), and [contract tests](../../tests/CheatEngine.Mcp.Tests/Contract).
The tests exercise composition parity, contract validation, routing metadata, error mapping, values, file paths, state cleanup, jobs, Lua wrappers, and feature gates.

Run the repository's normal portable verification from the repository root:

~~~powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
~~~

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for the complete build gates, golden-file process, NativeLua test setup, and formatting rules.

## Project references

| Dependency | Reason |
|---|---|
| CheatEngine.Client | Client dispatch, failure types, target lifecycle, and supported Cheat Engine operations |
| ModelContextProtocol | MCP primitive attributes, server contracts, filters, and protocol types |

The project exposes internals to the plugin and test projects so the composition root and focused tests can verify internal integration points.
