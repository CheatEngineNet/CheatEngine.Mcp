# Repository Guidelines

## Project

`CheatEngine.Mcp` is a Windows x64, .NET 10 / C# 14 managed Cheat Engine 7.7 plugin built on the CheatEngine.Client
NuGet package, with a stdio MCP gateway. Each CE process enables its own authenticated loopback HTTP backend. One
gateway
routes requests by immutable instance ID. JSON configuration replaces the old WPF/menu flow. Disabling withdraws
discovery and stops admission before Client-owned cleanup.

## Layout and conventions

- `libs/CheatEngine.Mcp.Core/`: the shared domain library and the only project that references `CheatEngine.Client`
  (transitively for the others). `Composition/` holds the builder, primitive manifest, catalog, validator and schema
  options; `Execution/ToolExecution`, `Targets/TargetResources`, `Lua/LuaToolRuntime` and `Runtime/McpRuntimeInfo`.
- `srcs/CheatEngine.Mcp.Tools/`: the tool classes, their helpers and `AddTools()`.
- `srcs/CheatEngine.Mcp.Resources/`, `srcs/CheatEngine.Mcp.Prompts/`: `AddResources()` and `AddPrompts()`; empty today.
- `srcs/CheatEngine.Mcp.Hosting/`: `Backend/` (the per-activation web host and its registration), `Configuration/`
  (options, generated validators, the `MCP_*` source), `Discovery/` (instance registry) and `Gateway/` (stdio routing).
- `srcs/CheatEngine.Mcp.Plugin/`: the CE plugin, its composition root, lifecycle module, status indicator, NLog sink,
  feature options and shipped `appsettings.json`.
- `srcs/CheatEngine.Mcp.Gateway/`: the gateway executable's composition root.
- `tests/CheatEngine.Mcp.Tests/`: the single xUnit v3 project on Microsoft.Testing.Platform, with folders mirroring the
  product (`Core/`, `Tools/`, `Hosting/`, `Plugin/`) plus `Architecture/`, `Contract/`, `NativeLua/`,
  `LiveQualification/` and `Support/`. `tests/CheatEngine.Mcp.LiveTarget/` is the disposable live target.
- `CheatEngine.Client`: NuGet package pinned centrally in `Directory.Packages.props`; no source checkout or submodule is
  required.
- `tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure/`: local test helpers adapted from Client, with
  provenance in `NOTICE.md` and the full license under `licenses/`.
- `skills/cheatengine-mcp/`: shipped operator skill and exact tool catalog.
- `artifacts/`: build/test output.
- `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`: centralized
  build/package/SDK policy and the repository guards.

Each project has a short `README.md` with its scope and allowed dependencies. Core is referenced by Tools, Resources,
Prompts and Hosting; only Plugin and Gateway compose. Hosting never references Tools, Resources, Prompts, the Client or
NLog: it sees primitives only through the Core manifest. Only the plugin project sets `CheatEngineClientPluginProject`
and references `CheatEngine.SDK` directly (guards CEMCP002-003). `InternalsVisibleTo` targets only the tests.
`Architecture/ArchitectureTests` enforces these rules.

Follow `.editorconfig`, adapted from CheatEngine.Client: tabs in C#, explicit types, file-scoped namespaces, braces, C#
14 extension blocks for builders, nullable analysis, and warnings as errors. Preserve licenses and unrelated user work.

## Architecture

`CheatEngineMcpPlugin : CheatEngineClientPlugin` composes one activation:
`builder.AddCheatEngineMcp(environment).AddTools().AddResources().AddPrompts()`. The gateway composes the same
primitives with `AddCheatEngineMcpGateway(...)`, so both advertise one manifest. In `Backend` mode the builder registers
each primitive type as an activation-scoped service; in `Catalog` mode (gateway, schema tests) it records the manifest
only and never constructs a type. Declare primitives only through `AddToolType/AddResourceType/AddPromptType` in the
owning project's builder; never use `WithTools<T>`, `*FromAssembly`, or `ActivatorUtilities`. Duplicate tool names,
prompt names or resource templates fail validation instead of being dropped.

There are two containers per activation. The Client activation provider is the domain container: tools,
`TargetResources` and `LuaDebuggerCaptureGuard` are scoped to the activation, and `McpPrimitiveTargets` resolves every
primitive instance when Client Hosting constructs `McpServerModule`, so a constructor or options failure fails the
enable. Tool constructors must not dispatch Client work. `McpBackendHost`'s web application is the transport container:
it binds the borrowed instances with the instance overload of `McpServerTool.Create` and registers no Client service,
façade or primitive type, so HTTP never creates another Client activation and never disposes tools. The web host starts
from `WebApplication.CreateEmptyBuilder` in `Production`, with Kestrel core, routing core and a no-op host lifetime:
CE's
ambient `appsettings`, `ASPNETCORE_*`/`DOTNET_*`/`Kestrel__*` variables and hosting startup assemblies never apply, and
`app.Urls.Add` is the only address source.

Disable order: the Client's stopping token makes the backend answer 503; `McpServerModule.OnDisabling` withdraws the
discovery record and shows the disabled status; Client drains its lease stack; disposing the activation scope then
disposes the tools on CE's thread; `McpServerModule.Dispose` starts the HTTP shutdown without waiting. Never
synchronously join HTTP workers on CE's main thread.

`McpStatusIndicator` updates one CE-owned menu item for backend enable, disable, and startup failure. Its click handler
is pure Lua and its passive disabled state survives until CE closes. Client 1.0 has no UI API; SDK dispatch admission
opens after enable and closes before disable callbacks, while protected SDK Lua admission spans those callbacks.
`LuaToolRuntime.ShowPluginStatus` is a narrow, main-thread-only SDK UI exception called only by the plugin's indicator;
disabling only updates an existing status item. Never extend it to tool calls, target work, or arbitrary lifecycle
scripts. Keep the native live indicator checks when changing this boundary.

Tools are public instance classes with DI constructors and `McpServerToolType`. Every tool method and input parameter
needs a description. `SchemaTransform` protects nullable/numeric compatibility of generated schemas. Gateway
schema-only discovery must not construct Client tools.

Gateway schemas add required `instanceId` to all CE tools; `list_instances` is gateway-local. Never introduce a shared
selected-instance setting, automatic fallback, or mutation retry. Display names may duplicate. Registry records bind
activation identity, PID/start time, endpoint, and token; verify backend identity before routing and never expose tokens
in tool results or test receipts. Disable proxy/redirect following for backend connections.

Run each compound CE operation through `ToolExecution.Run`, which dispatches the entire body through Client and passes
its stopping token. Keep target inspection and use in the same dispatch. Prefer typed Client APIs whenever available.
For
additional documented celua.txt features use LuaToolRuntime: fixed implementation-owned scripts and separately encoded
a[] arguments through ICheatEngineClient.Lua. Only the runtime adapter may access SDK Lua state; never interpolate
caller
input as source or expose opaque Lua objects. Keep Lua result copies bounded.

The pinned Client 1.0 / SDK 2.0 disassembly columns are misordered on the installed CE 7.7 line. Retain typed
instruction bytes and lengths, but obtain named display columns through protected Client Lua using CE's actual
`extra, opcode, bytes, address` return order. Keep the native Lua column assertions and live known-instruction probe
when
changing this correction.

Value-scan tools share `scannerName`: `main` (default) operates CE's currently visible scan tab; other names retain
independent Client sessions. `MainScanner` uses fixed Client-dispatched Lua to drive native scan controls and read the
borrowed found list. Never destroy/reinitialize GUI scan objects, retain them across calls, replace CE scan callbacks,
or
wait on UI scans. Poll status instead. Main survives disable; independent scans retain Client lease cleanup. A running
main scan blocks MCP target changes. Keep native UI result/count/manual-scan/isolation checks when changing this
boundary.

Track target leases in `TargetResources` with bounded recovery descriptors. Refuse process switching while resources
remain, except a repeated selected PID. Explicit release runs in reverse creation order, stops on incomplete cleanup,
retains retryable handles, and reports manual recovery.

Responses use `success` plus result fields. Client failures include kind, operation, and hostEffect. Do not report
success for an incomplete release or hide partial side effects. Addresses use uppercase hexadecimal.

## Configuration and delivery

Precedence: option defaults, the plugin folder's `appsettings.json` (shipped defaults), the user `appsettings.json`,
then `MCP_HOST`/`MCP_PORT`/`MCP_INSTANCE_NAME`/`MCP_INSTANCE_DIRECTORY`. Data/log path: `%APPDATA%/CheatEngine.Mcp`, or
the absolute `MCP_DATA_DIRECTORY` override. An activation reads its configuration once; there is no reload and no legacy
configuration fallback. Hosting owns `McpBackendOptions` and `McpDiscoveryOptions` (`[OptionsValidator]` validators,
generated configuration binding); the plugin owns `McpFeatureOptions`, read while CE configures the activation.
Validation runs when the backend factory is constructed and throws `OptionsValidationException`; a malformed `MCP_PORT`
throws `FormatException`. Registry defaults to `%LOCALAPPDATA%/CheatEngine.Mcp/instances`; gateway and plugins must
agree
on overrides.

Backends require literal 127.0.0.1, default port zero, and per-activation bearer authentication. Lua execution, Client
Auto Assembler patches, and dedicated mutation tools are enabled by default. Explicit configuration can disable Lua or
Auto Assembler; these flags are not a sandbox. Table files need allowed roots. Bound transport, reads, scans,
identifiers, and retained resources.

Run `pwsh -NoProfile -File eng/Publish.ps1` (Release by default) to create `artifacts/dist/<configuration>/`:

- `CheatEngine.Mcp/`: the CheatEngine.Client staged plugin folder (`CheatEnginePluginOutputPath`, validated by
  CECLIENT010-016): `CheatEngine.Mcp.Plugin.dll` with its `deps.json`, `runtimeconfig.json`, dependencies, native Lua
  bridge and `appsettings.json`, plus README, LICENSE and `licenses/`. CE loads `CheatEngine.Mcp.Plugin.dll` through the
  SDK-generated entry point; keep the folder intact and the file name unchanged.
- `CheatEngine.Mcp.Gateway.exe`: a self-contained single executable.
- `skills/cheatengine-mcp/`: the separate operator skill, for optional installation into the AI client; never include
  it in the plugin folder.

The Client deployment copies only the top-level `*.dll`, `*.json` and `*.pdb` files of the plugin output. CEMCP001 fails
the build when a package would ship an app-local copy of a shared-framework assembly (the framework names are captured
before package conflict resolution can remove them); CEMCP004 fails it when a dependency asset would land in a
subdirectory (`runtimes/<rid>/`, culture satellites) that the deployment would drop. The publish script rebuilds the
plugin output from an empty folder and then checks that the plugin folder holds every asset its `deps.json` names.

Never reintroduce a hand-written loader, extracting wrapper or Costura packaging, and never claim Native AOT loading.
The
CE host still needs .NET 10 ASP.NET Core and Windows Desktop runtimes. Keep the direct SDK package reference alongside
the Client package in the plugin project only: it supplies the entry point generator and native bridge assets.

## Commands

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
pwsh -NoProfile -File eng/Publish.ps1
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

Tests use xUnit v3 Facts/Theories and behavior-oriented Subject_Condition_Outcome names. Prefer Client contract doubles
and real loopback transport checks over mocking CE internals. `Contract/` compares the gateway and backend `tools/list`,
`initialize`, and the plugin output file list with golden snapshots; set `CHEATENGINE_MCP_UPDATE_GOLDEN=1` only for an
intended, reviewed contract change. `LiveQualification/` follows the Client's C# serial fixture and fail-fast
acknowledgement pattern: set `CHEATENGINE_MCP_LIVE_QUALIFICATION=I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET`,
publish the matching configuration first, then run
`dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on`.
It automatically installs the published plugin folder into two private CE copies, launches separate owned targets,
routes both through one stdio gateway, and restores CE user settings using the locally adapted Client backup/recovery
helpers. Optional `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` overrides the installed source; never edit that
source. Reports/backups stay outside the checkout under `%LOCALAPPDATA%/CheatEngine.Mcp.LiveQualification/runs`. No
other CE instance may be running. The live scenario covers memory, address-list/freeze and speedhack readback, not full
Client host qualification. NativeLua tests require the documented DLL fixture and are excluded from portable CI, rather
than skipped. Do not equate offline tests with native host verification.

Update the skill/catalog when tool contracts or configuration change. Never package or commit
references/local-cheat-engine.md. No commit/push/deployment without user authorization.
