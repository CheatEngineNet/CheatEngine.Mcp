# Development

This page is for contributors who change the code: how the solution is laid out, which references are allowed, how primitives are composed, and how to add a tool under the 2.0.0 contract.
The rules themselves are in [AGENTS.md](../AGENTS.md); this page explains them and points to the code that enforces them.
For the build commands and the pull request process, start with [CONTRIBUTING.md](../CONTRIBUTING.md); for tests see [Testing](testing.md), for the shipped files [Packaging](packaging.md), and for the runtime design [Architecture](architecture.md).

> **Status.** This page describes the 2.0.0 (v2) development model.
> The Core building blocks it names are in the code: the composition builders, the contract rules, the error contract, `ToolDispatch`, the capability gates, the job registry, the value helpers and the fixed-Lua runtime.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet.
> `CheatEngineToolNames` defines 173 names: the gateway-local `instance_list` and 172 backend tools. `AddTools()` registers every backend v2 name and no historic alias.
> Use the running `tools/list` schema for the installed build's argument shapes, output schemas, annotations, and descriptions.

## Repository layout

| Path | Contents |
|---|---|
| `libs/CheatEngine.Mcp.Core/` | The shared domain library, and the only project that references the `CheatEngine.Client` package: `Composition/`, `Contract/`, `Execution/`, `Features/`, `Files/`, `Jobs/`, `Lua/`, `Runtime/`, `Tables/`, `Targets/` and `Values/`. |
| `srcs/CheatEngine.Mcp.Tools/` | The tool classes, their helpers and `AddTools()`. |
| `srcs/CheatEngine.Mcp.Resources/`, `srcs/CheatEngine.Mcp.Prompts/` | `AddResources()` serves static knowledge documents, workflow bodies, and live runtime, process, modules, memory-regions, records, and structures projections; `AddPrompts()` serves the 22 workflow prompts. |
| `srcs/CheatEngine.Mcp.Hosting/` | `Backend/` (the per-activation web host), `Configuration/` (options, generated validators, the `MCP_*` source), `Discovery/` (the instance registry) and `Gateway/` (stdio routing). |
| `srcs/CheatEngine.Mcp.Plugin/` | The Cheat Engine plugin: composition root, `McpServerModule`, `McpStatusIndicator`, the plugin logger under `Logging/`, the per-activation settings and the shipped `appsettings.json`. |
| `srcs/CheatEngine.Mcp.Gateway/` | The gateway executable's composition root, `GatewayProgram`, and its publish profile. |
| `tests/CheatEngine.Mcp.Tests/` | The single test project; see [Testing](testing.md). |
| `tests/CheatEngine.Mcp.LiveTarget/` | The disposable process that live qualification attaches to. |
| `skills/cheatengine-mcp/` | The operator skill: `SKILL.md`, `references/` and `agents/openai.yaml`. |
| `docs/` | This documentation. |
| `eng/` | `Publish.ps1` and `Tests.props`, the shared test project settings. |
| `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses/` | The license texts that ship with the plugin and the gateway. |
| `artifacts/` | Build, publish and distribution output; never committed. |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, `nuget.config`, `.editorconfig` | Central build, package, SDK, source and style policy, and the repository guards. |

Each project has a short README with its scope and allowed dependencies: [Core](../libs/CheatEngine.Mcp.Core/README.md), [Tools](../srcs/CheatEngine.Mcp.Tools/README.md), [Resources](../srcs/CheatEngine.Mcp.Resources/README.md), [Prompts](../srcs/CheatEngine.Mcp.Prompts/README.md), [Hosting](../srcs/CheatEngine.Mcp.Hosting/README.md), [Plugin](../srcs/CheatEngine.Mcp.Plugin/README.md) and [Gateway](../srcs/CheatEngine.Mcp.Gateway/README.md).

## Dependency rules

| Project | May reference | Never |
|---|---|---|
| Core | `CheatEngine.Client` (which brings the `CheatEngine.SDK` compile assets), `ModelContextProtocol` | ASP.NET Core, a logging library, configuration loading, discovery, a concrete tool |
| Tools | Core, and the Client API through it | Hosting, configuration, ASP.NET Core, raw SDK Lua state |
| Resources | Core, Tools | ASP.NET Core, the Client directly. Live resources call the read-only tools they project. |
| Prompts | Core | ASP.NET Core, the Client directly. |
| Hosting | Core, ASP.NET Core, `ModelContextProtocol.AspNetCore` | Tools, Resources, Prompts, the Client, a logging library, Cheat Engine UI |
| Gateway | Core, Hosting, Tools, Resources, Prompts | The Plugin, or anything that creates a Client activation |
| Plugin | Every product project, `CheatEngine.Client` and a direct `CheatEngine.SDK` reference | A logging library: the plugin writes its own log file |

Only the Plugin and the Gateway compose; the other projects only declare.
Hosting sees primitives only through the Core manifest and borrows their instances.

These rules are enforced, not just documented:

| Check | What it enforces |
|---|---|
| `ArchitectureTests.ProjectGraph_Assembly_ReferencesOnlyItsAllowedLayers` | The table above, from the compiled assemblies' references to `CheatEngine.*` and `NLog*`. |
| `ArchitectureTests.ProjectGraph_DomainLibraries_NeverReferenceAspNetCore` | Core, Tools, Resources and Prompts never reference `Microsoft.AspNetCore.*`. |
| `ArchitectureTests.InternalsVisibleTo_ProductAssemblies_OnlyExposeInternalsToTheTests` | `InternalsVisibleTo` names only `CheatEngine.Mcp.Tests`. |
| `ArchitectureTests.State_ProductAssemblies_HoldNoMutableStaticFields` | All state belongs to a dependency-injection lifetime, never to a static field. |
| `ArchitectureTests.PluginStatus_SdkUiException_IsCalledOnlyByThePluginIndicator` | `LuaToolRuntime.ShowPluginStatus`, the one SDK UI exception, has no caller other than `McpStatusIndicator`. |
| CEMCP002 and CEMCP003 in `Directory.Build.targets` | Only the project that sets `McpPluginProject` carries the Client plugin profile (`CheatEngineClientPluginProject`), and only it references `CheatEngine.SDK` directly. |
| CEMCP005 in `Directory.Build.targets` | Every project under `libs/` and `srcs/` keeps `IsAotCompatible`; only a project with a `FrameworkReference` to `Microsoft.AspNetCore.App` (Hosting, Plugin and Gateway) may turn off reference verification, in its own project file, with a comment. |

The package rules are in [CONTRIBUTING.md](../CONTRIBUTING.md#packages-and-lock-files), and the packaging guards in [Packaging](packaging.md#build-guards).

## Composition

One builder composes one set of primitives in two modes.

- The plugin composes each activation with `CheatEngineMcpPlugin.ComposePrimitives(builder.AddCheatEngineMcp(...))`, which chains `AddTools().AddResources().AddPrompts()`, in `Backend` mode.
- The gateway composes the same chain after `AddCheatEngineMcpGateway(args, version)`, in `Catalog` mode.
- Inside each `Add*()` method, a project declares its types with `AddToolType<T>()`, `AddResourceType<T>()` and `AddPromptType<T>()`, and its source-generated JSON metadata with `AddJsonTypeInfoResolver(...)`. Declaration order is catalog order.
- `AddExecutionServices()` registers the activation-scoped services that v2 primitives inject: `ToolDispatch`, `DispatchStatistics`, `McpFeatureGate`, `McpStateLedger`, `TargetResources`, `TargetTransitionGuards` and `JobRegistry`.
- In `Backend` mode each declared type is an activation-scoped service; in `Catalog` mode only the manifest is recorded and no primitive is ever constructed.
- Never use `WithTools<T>`, the SDK's `*FromAssembly` scanners or `ActivatorUtilities`; `ArchitectureTests.Composition_Sources_RegisterPrimitivesOnlyThroughTheBuilders` rejects them.
- `McpPrimitiveValidator` fails the startup on a duplicate tool name, prompt name or resource template instead of dropping it, and applies `McpContractRules` to every v2 tool.
- `CompositionParityTests` check that the plugin and the gateway executable compose the same primitives, in the same order, with the same JSON resolvers.

Each activation has two containers.
The Client activation provider is the domain container: it constructs the tools, and `McpPrimitiveTargets` resolves every primitive when Client Hosting constructs `McpServerModule`, so a constructor or options failure fails the enable.
The backend's web application is the transport container: it binds the borrowed instances and registers no Client service, so HTTP never creates another activation and never disposes a tool.
A tool constructor must therefore be cheap and must never dispatch Client work.
[Architecture](architecture.md) describes the lifecycle and the disable order.

## Execution rules

- Cheat Engine's main thread runs every Client call. Run each compound operation, including the inspection and the use of a target, as one `ToolDispatch.Run(operation, body, cancellationToken)` dispatch.
- Prefer the typed Client APIs. When a documented `celua.txt` feature has no typed API, use a fixed, implementation-owned Lua body through `ToolDispatch.RunLua<T>(operation, body, jsonTypeInfo, cancellationToken, arguments)`, or `ToolDispatch.ExecuteLua<T>` inside a `Run` body that also makes Client calls.
- Never block the main thread: no waits, sleeps or joins, and never synchronously join HTTP work from Cheat Engine's thread. Work that can take more than about a second is a job.
- `ToolDispatch` admits at most `Mcp:Execution:MaxConcurrentDispatches` dispatches (4 by default) and refuses the next one as `busy`; a body that holds the main thread longer than `Mcp:Execution:DispatchBudgetMilliseconds` (100 ms by default) is logged as event 3001.
- Target leases live in `TargetResources`: a process switch is refused while resources remain, and every registered `ITargetTransitionGuard` is consulted.
- Addresses are uppercase hexadecimal without `0x`; the value formats are in `Core/Values` (`HexFormat`, `HexParse`, `AobPattern`, `McpValueType`, `McpValueCodec`, `Paging`).
- Never log or return a token, and never put caller input into Lua source.

## Adding a tool (v2)

Follow these steps for every new tool; [Architecture](architecture.md#tool-contract-v2) states the contract they implement.

### 1. Name it

- The name has the form `<domain>_<verb>[_<object>]`, matches `^[a-z][a-z0-9]*(_[a-z0-9]+)+$` and has at most 40 characters.
- The domain and the verb come from the reviewed lists in `McpContractRules` (`libs/CheatEngine.Mcp.Core/Composition/McpContractRules.cs`); adding a domain or a verb is a contract change that needs a review.
- Add the name as a public constant in `libs/CheatEngine.Mcp.Core/Contract/CheatEngineToolNames.cs` and use the constant everywhere, so a rename breaks the build instead of a prompt.

### 2. Place it

- Put the tool class in its domain folder, `srcs/CheatEngine.Mcp.Tools/<Domain>/`, for example `Memory/`.
- Declare it with `AddToolType<T>()` in that domain's `Add<Domain>Tools()` method, which `AddTools()` calls in catalog order.

### 3. Shape the class

- A public instance class marked `[McpServerToolType]`, with a dependency-injection constructor that only stores its dependencies.
- Inject `ToolDispatch`, and `TargetResources`, `JobRegistry` or `McpFeatureGate` when the tool needs them.
- The class is activation-scoped: Client Hosting disposes it with the activation, on Cheat Engine's thread.

### 4. Declare the method

```csharp
[McpServerTool(Name = CheatEngineToolNames.MemoryRead, Title = "Read memory", ReadOnly = true,
	Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
[Description("Reads bytes from the attached process.")]
public MemoryReadResult Read(
	[Description("Address or symbol to read.")] string address,
	[Description("Number of bytes, 1 to 4096.")] int size,
	CancellationToken cancellationToken)
{
	return _dispatch.Run(CheatEngineToolNames.MemoryRead, token => ..., cancellationToken);
}
```

The example shows the attributes; the tool, its result and its limits are illustrative.

- `Title` is unique, in sentence case, and at most 60 characters.
- Set all four annotations explicitly. A tool is never both read-only and destructive; a poll is read-only and idempotent; a stop or release is idempotent; `OpenWorld = true` is reserved for the reviewed list of tools that reach beyond the target and Cheat Engine, such as the host file system. `open-world-tools.txt` is the reviewed snapshot of that list.
- `UseStructuredContent = true` publishes an output schema derived from the return type; the validator requires it to be an object.
- `[McpMeta(McpDispatchClass.MetaKey, ...)]` declares how long the tool may hold the main thread: `McpDispatchClass.Short`, `HostScan`, `BlockingNative` or `MayPrompt`. A `BlockingNative` or `MayPrompt` tool states its worst case, or its possible dialog, in the description.
- Describe the method (at most 1,024 characters) and every parameter; the validator rejects a parameter without a description, and no tool may declare `instanceId`, which only the gateway adds.
- Add `[RequiresFeature(McpFeature.X)]`, once per gate, when the tool needs `UnsafeLua`, `AutoAssembler`, `TargetCodeExecution` or `KernelAccess`. The requirement is copied into the tool's `_meta` under `cheatengine/requires`, and the `EnforceFeatures` filter refuses the call before binding. When the gate depends on an argument, call `McpFeatureGate.Require(feature, toolName)` before the first host effect instead.

### 5. Return a record

- Return a `public sealed record` with an object shape, never `object`, an anonymous type or a `success` field.
- Describe each property with `[property: Description("...")]` on the positional parameter and an XML `<param>` comment, as `InstanceListResult` in Hosting does.
- Use `string` for addresses, 64-bit integers and memory values, `int` for counts, sizes and indexes, and the `Core/Values` helpers to format them.
- An enum in a contract is one of ours, marked `[JsonConverter(typeof(ContractEnumConverter<T>))]` so it is `snake_case` and rejects integers; a Client or SDK enum never appears in a signature.
- Paged results take `limit` and `offset` and return `nextOffset`, omitted when the list is complete.

### 6. Register the JSON metadata

- Each tool domain owns a public `JsonSerializerContext` in its folder, for example `Memory/MemoryJsonContext.cs`, listing its argument and result types with `[JsonSerializable]`.
- Use `[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]`, as `CoreJsonContext` does.
- Register it with `builder.AddJsonTypeInfoResolver(MemoryJsonContext.Default)` in `Add<Domain>Tools()`, so the backend and the gateway catalog serialize and describe the types identically without reflection.

### 7. Report failures

- Throw `CheatEngineToolException`, through its factories: `InvalidArgument`, `LimitExceeded`, `InvalidState`, `NotFound`, `NotAttached`, `Busy`, `Unsupported`, `Timeout`, `PartialEffect`, `FromFailure` and `Internal`.
- The Core call-tool filter turns it into `isError: true` with the `{"error":{kind,message,operation,hostEffect,retryable,hint,details?}}` envelope and no structured content.
- Never report a partial effect or a timeout as a success: use `PartialEffect` with `details`, or `Timeout` with `hostEffect: unknown`.
- `ToolDispatch` turns any other exception from inside a dispatch into `internal` with `hostEffect: unknown`.

### 8. Write fixed Lua safely

- The body is a constant owned by the implementation; caller values reach it only through the `a` table, as the `arguments` of `RunLua` or `ExecuteLua`. Never interpolate caller input into source, and never return opaque Lua objects.
- A fixed body never contains `load(`, `loadstring(`, `dofile(`, `loadfile(`, `require(`, `_G[a` or `rawget(_G,a`; `LuaFixedScriptAssert` in the tests checks these and a few more fragments.
- The body can use the local `mcp` prelude: `mcp.hex`, `mcp.bytes`, `mcp.num` and `mcp.err`, which declares a failure; an omitted host effect is reported as `unknown`.
- `ToolDispatch` scans each body for sensitive Cheat Engine APIs, such as `autoAssemble`, `executeCode*`, `inject*`, `dbk_*`, `dbvm_*` and `loadTable`, and requires the matching gate. The scan is lexical and includes comments, so an ungated body must not even mention those names.
- Results are copied within the runtime's bounds: 4 MiB per string, 65,536 items and a depth of 16.
- The legacy disassembly column correction and the main scanner rules in [AGENTS.md](../AGENTS.md#architecture) still apply.

### 9. Long work is a job

- A tool that can exceed about one second starts a job instead: `*_start_*` returns a `jobId`, `*_poll_*` takes `jobId`, `afterSequence` and `limit`, and `runtime_list_jobs` and `runtime_stop_job` manage every job through `JobRegistry` and the Lua job kernel in Core.
- Job lifetimes are bounded by `Mcp:Execution:JobDefaultTtlSeconds` and `Mcp:Execution:JobMaxTtlSeconds`, and a poll never consumes results.

### 10. Test and document it

- Add a portable test through the Client double or `TestMcpPipeline`, a test that a disabled gate causes zero mutating Client calls, and a NativeLua test for each fixed script; see [Testing](testing.md).
- Regenerate and review the golden files, which now include your tool.
- Update `skills/cheatengine-mcp/references/tool-catalog.md`, the operator skill, and the [tool reference](reference/tools.md); the reference directs operators to the live schema while the frozen inventory and golden files record the reviewed contract.

## Documentation

- One fact has one home; link to it instead of repeating it.
- Markdown files are UTF-8 without a byte order mark, ASCII only, with CRLF line endings, one sentence per line and relative links.
- Mark an adopted design that the code does not implement yet as "(v2, in progress)", and remove the mark in the change that implements it.
- Update the skill and its tool catalog whenever a tool contract or a setting changes.
