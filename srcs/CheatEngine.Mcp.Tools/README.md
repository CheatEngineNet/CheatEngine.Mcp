# CheatEngine.Mcp.Tools

`CheatEngine.Mcp.Tools` defines the Cheat Engine tool surface of the MCP
server. It contains the public MCP tool containers, their domain models,
source-generated JSON contexts, fixed Lua scripts, and the one composition
entry point:

```csharp
ICheatEngineMcpBuilder AddTools()
```

It is a product library, not a server host. The Plugin supplies the active
Cheat Engine Client and the fixed Lua runtime; Hosting starts the loopback
backend and routes gateway requests; Core provides composition, dispatch,
target ownership, feature gates, and common contracts. See the
[repository README](../../README.md) for product usage and
[`../CheatEngine.Mcp.Plugin/README.md`](../CheatEngine.Mcp.Plugin/README.md)
for the Cheat Engine activation boundary.

## Composition model

`CheatEngineToolsBuilderExtensions.AddTools()` declares every v2 tool domain in
a stable catalog order. Each domain registration adds its
`JsonSerializerContext` and one or more public tool container types through
`AddToolType<T>()`.

The same call runs in two modes:

- **Backend mode** registers each instance tool container as an
  activation-scoped service. Client Hosting resolves the containers before the
  plugin enables, and the backend borrows those fixed instances for requests.
- **Catalog mode** registers only primitive metadata and generated JSON
  resolvers. It never constructs a tool container or touches Cheat Engine, so
  the gateway can safely describe the exact surface it will route.

`AddTools()` is the only public aggregate entry point. Domain-level
`Add<Domain>Tools()` methods are public so a focused composition can be built
for tests or a future host, but product composition must use `AddTools()` to
preserve the complete catalog and its ordering.

## Tool domains

The current v2 catalog contains 190 backend tools in 21 domains.

| Folder and registration | Tools | Scope |
| --- | ---: | --- |
| `Runtime/` - `AddRuntimeTools()` | 6 | Server and capability information, resource cleanup, and jobs. |
| `Processes/` - `AddProcessTools()` | 9 | Process discovery, target selection, pause and resume operations. |
| `Memory/` - `AddMemoryTools()` | 19 | Reads, writes, protection, allocations, files, snapshots, and samples. |
| `Scan/` - `AddScanTools()` | 8 | Main value-scanner lifecycle, criteria, results, and named scanners. |
| `Aob/` - `AddAobTools()` | 3 | Array-of-bytes search and signature generation. |
| `Pointer/` - `AddPointerTools()` | 14 | Pointer chains, maps, references, scans, and persisted scan files. |
| `Modules/` - `AddModuleTools()` | 5 | Module inventory, PE exports and imports, and patch inspection. |
| `Symbol/` - `AddSymbolTools()` | 10 | Symbol resolution, search, sources, modules, and registrations. |
| `Speedhack/` - `AddSpeedhackTools()` | 2 | Speedhack state and speed changes. |
| `Util/` - `AddUtilTools()` | 2 | Typed value conversion and 64-bit expression evaluation. |
| `Code/` - `AddCodeTools()` | 14 | Disassembly, decoding, code analysis, graphs, comments, and jobs. |
| `Asm/` - `AddAsmTools()` | 8 | Assembly, script checks, patches, injection, and API-hook templates. |
| `Record/` - `AddRecordTools()` | 14 | Address-list records, grouping, scripts, dropdowns, and cleanup. |
| `Table/` - `AddTableTools()` | 3 | Cheat-table loading, saving, and file listing. |
| `Structures/` - `AddStructureTools()` | 15 | Structure Dissect definitions, elements, values, comparison, and C headers. |
| `Debugger/` - `AddDebuggerTools()` | 18 | Debugger sessions, breakpoints, threads, context, capture, traces, and stepping. |
| `Exec/` - `AddExecTools()` | 6 | Native and managed injection, calls, and C compilation. |
| `DotNet/` - `AddDotNetTools()` | 10 | Managed-runtime domains, types, methods, objects, and instance searches. |
| `Mono/` - `AddMonoTools()` | 15 | Mono attachment, metadata, fields, invocation, objects, and instance searches. |
| `Kernel/` - `AddKernelTools()` | 7 | DBVM, address translation, physical memory, and watches. |
| `Lua/` - `AddLuaTools()` | 2 | Caller-authored Lua execution and Cheat Engine Lua API lookup. |

The live descriptions and exact input/output schemas are the source of truth
for a client. The repository's human-readable index is the
[tool map](../CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md).

## State and lifetime rules

Most tool containers are stateless facades over Core services. A small set of
domain services is activation-scoped in backend mode because it owns state that
must not cross Cheat Engine activations:

| Service | Registered by | Ownership |
| --- | --- | --- |
| `AllocationRegistry` | `Memory/` | Named allocations created through MCP. |
| `MemorySnapshotStore` | `Memory/` | In-memory named snapshots. |
| `MappedMemoryOverride` | `Scan/` and `Aob/` | Shared `MEM_MAPPED` scanner override. |
| `PointerStore` | `Pointer/` | Pointer maps and scan state. |
| `SymbolRegistrations` | `Symbol/` | Symbols registered by MCP. |
| `MainScannerTransitionGuard` | `AddTools()` | Prevents unsafe target changes while the main scanner is active. |

Core owns the other activation resources: the dispatch ledger, target-resource
leases, jobs, feature gate, and target-transition coordinator. Mono attachment
state and Mono instance-search jobs are also released through that Core
lifecycle. Structure Dissect data, debugger state, address-list records, and
speedhack state are owned by Cheat Engine itself and can outlive a plugin
activation; tools expose explicit cleanup or recovery operations where the
underlying host API permits it.

Never store activation state in a static field. Never obtain or call
`ICheatEngineClient` from a tool constructor. Constructors run while the
activation is built, before the backend accepts requests. Work that touches
Cheat Engine belongs inside a tool call and travels through `ToolDispatch` or
the relevant Core resource service.

## Execution, Lua, and feature gates

Tools use Core's `ToolDispatch` to enter the Cheat Engine activation and main
thread boundary. Fixed Lua scripts are data-driven implementation details: the
Plugin supplies the protected Lua executor, while this project supplies script
text and strongly typed input/output records. Tools must not reference the
Cheat Engine SDK's protected Lua state directly.

Every tool publishes a dispatch class in its MCP metadata. Classes distinguish
short calls, potentially prompt-producing calls, host scans, and blocking
native work so the execution layer can apply the correct admission and
reporting behavior. A request may therefore be refused as busy before Cheat
Engine work begins. Long native calls can be synchronous and cannot always be
cancelled after they have started.

Tools that require elevated capabilities are annotated with `RequiresFeature`.
The activation's `Mcp` feature switches gate unsafe Lua, Auto Assembler,
target code execution, and kernel access before argument binding. The options
and their default values are owned by Core and configured by the Plugin; this
library has no settings file and no environment variables of its own.

File-writing tools use Core's `McpFilePaths` policy. The Plugin supplies that
policy from `Mcp:Files:AllowedRoots`, which is empty by default. Do not add
ad-hoc path checks or create directories outside the policy. Cheat-table paths
are governed by the Client's separate `CheatEngineClient:AllowedTableRoots`
setting.

## Public contract requirements

The v2 tool names live in Core's
[`CheatEngineToolNames`](../../libs/CheatEngine.Mcp.Core/Contract/CheatEngineToolNames.cs).
Names, schemas, output shapes, titles, descriptions, dispatch metadata, and
feature annotations are public MCP contract. The primitive validator and the
contract snapshots treat an unintended change as a regression.

When adding or changing a tool:

1. Put it in the closest existing domain. Create a new domain only when its
   state, Client API, and user-facing vocabulary justify a separate boundary.
2. Add its frozen name to `CheatEngineToolNames` and declare the public method
   on a public `[McpServerToolType]` container with an explicit MCP name, title,
   description, read-only flag, dispatch metadata, and any required feature.
3. Use records and the domain's source-generated JSON context for every public
   argument and result shape. Register that context and container in the
   matching `Add<Domain>Tools()` method.
4. Use Core contracts for success, validation failures, host effects, jobs,
   target transitions, and path policy. Do not expose SDK objects, exceptions,
   raw Lua values, or arbitrary dictionaries as the public result.
5. Add focused tests and intentionally regenerate the affected golden files as
   described in [CONTRIBUTING.md](../../CONTRIBUTING.md#golden-files).

Avoid changing an existing v2 name or removing a field. Prefer an additive
parameter, result field, or a clearly named new tool when evolution is needed.

## Dependency boundary

The project directly references only
[`CheatEngine.Mcp.Core`](../../libs/CheatEngine.Mcp.Core/README.md). It must not
reference Hosting, Plugin, logging providers, configuration sources, or the raw
SDK bridge. This keeps the gateway catalog host-independent and prevents tool
code from creating a second Cheat Engine activation or HTTP server.

Core supplies the composition abstractions, contracts, generated manifest
validation, target resources, execution limits, feature gates, and the
Client-facing dispatch layer. The Plugin contributes the actual Client,
`IFixedLuaExecutor`, file policy, configuration, logging, and runtime identity.
This dependency direction is checked by the architecture tests.

The project targets `net10.0`, follows the repository's C# 14 and nullable
settings, and inherits x64 deterministic builds, analyzers, locked package
restore, and AOT/trim analysis from
[`../../Directory.Build.props`](../../Directory.Build.props). It suppresses the
Client's experimental API diagnostics because value scanning, allocations,
instruction APIs, and Auto Assembler functionality intentionally use those
reviewed Client APIs.

## Tests and local verification

Tool tests live in
[`../../tests/CheatEngine.Mcp.Tests/Tools`](../../tests/CheatEngine.Mcp.Tests/Tools).
They cover each domain's input validation, dispatch behavior, fixed Lua script
generation, retained-state lifecycle, and result records using deterministic
Client doubles. `ToolConstructionTests` additionally verifies that every tool
container can be resolved without touching the Client during activation.

The cross-project contract suite in
[`../../tests/CheatEngine.Mcp.Tests/Contract`](../../tests/CheatEngine.Mcp.Tests/Contract)
checks unique explicit names, descriptions, DI construction shape, generated
schemas, backend and gateway parity, and checked-in golden MCP snapshots.

Build the project after a locked restore:

```powershell
dotnet build srcs/CheatEngine.Mcp.Tools/CheatEngine.Mcp.Tools.csproj --no-restore
```

Run the portable tool-focused tests with:

```powershell
dotnet test --project tests/CheatEngine.Mcp.Tests --no-restore --filter "FullyQualifiedName~CheatEngine.Mcp.Tests.Tools"
```

Run the contract tests after an intentional surface change:

```powershell
dotnet test --project tests/CheatEngine.Mcp.Tests --no-restore --filter "FullyQualifiedName~CheatEngine.Mcp.Tests.Contract"
```

The portable suite does not prove behavior against a native Cheat Engine host.
Use the opt-in live and Native Lua qualification paths in the
[repository README](../../README.md#verification) when a change needs that
level of evidence.
