# CheatEngine.Mcp.Resources

`CheatEngine.Mcp.Resources` owns the MCP resources exposed by CheatEngine.Mcp.
It packages the Markdown knowledge base used by clients and prompts, and it defines read-only resource views of a connected Cheat Engine instance.

The project is a .NET 10 class library that references [CheatEngine.Mcp.Core](../../libs/CheatEngine.Mcp.Core/README.md) and [CheatEngine.Mcp.Tools](../CheatEngine.Mcp.Tools/README.md).
It contains no host or UI composition code.

## Composition

`ICheatEngineMcpBuilder.AddResources()` is the project's only public composition entry point.
It registers every resource container explicitly through `AddResourceType<T>()`.
Do not introduce assembly scanning or an alternative registration path.

Backends expose the static documentation resources and the `cheatengine://instance/...` templates directly.
The gateway exposes the same static documentation and routes an instance resource as `cheatengine://instances/{instanceId}/...` after it has selected and verified that instance.

## Resource model

### Static knowledge resources

`Knowledge/Documents/` contains the reference documents and `Knowledge/Workflows/` contains the bodies of guided workflows.
The project embeds both sets of Markdown files exactly once, with stable logical names and `WithCulture="false"`.

`CheatEngineKnowledge` loads the embedded text once and rewrites its relative Markdown links to `cheatengine://docs/...` URIs.
`DocumentSlugs` is the deliberate reading order of the served documents, while `WorkflowNames` is derived from the embedded workflow files.

`Docs/CheatEngineDocResources` exposes each document as a local `text/markdown` resource at `cheatengine://docs/{slug}`.
It also exposes a `cheatengine://docs/workflows/{workflow}` template for the corresponding workflow body.
These resources are public-cacheable for one hour because their content changes only with the installed build.

When editing the knowledge base, keep links relative in source Markdown:

- Link one document to another as `slug.md`.
- Link a document to a workflow as `../Workflows/workflow-name.md`.
- Link a workflow to a document as `../Documents/slug.md`.

The embedded copy rewrites those links when it is served, while the source files remain readable on GitHub.

### Live instance resources

The classes in `Live/` publish JSON resource templates below `cheatengine://instance/`.
Each live resource is a projection of one source tool, declared with `[McpSourceTool]`, and returns that tool's structured result for the equivalent arguments.
The Core validator requires a read-only, closed-world, ungated source tool. A non-`short` source must name a separate
prepared projection: a unique public instance method with the same result type, explicit `short` metadata and no feature
gates. The method cannot itself be a tool. Invalid projection declarations fail startup even for a `short` source.

| Resource area | Resource templates | Source tools |
| --- | --- | --- |
| Runtime and process state | `runtime`, `process`, `threads`, `resources`, `jobs` | `runtime_get_overview`, `process_get_current`, `process_list_threads`, `runtime_list_resources`, `runtime_list_jobs` |
| Scanning and debugging | `scanners`, `scanners/{scannerName}`, `debugger`, `debugger/breakpoints{?limit}` | `scan_list_scanners`, `scan_get_status`, `debugger_get_status`, `debugger_list_breakpoints` |
| Memory and code | `regions{?offset,limit}`, `memory/{address}{?size}`, `disassembly/{address}{?count}`, `patches` | `memory_list_regions`, `memory_read`, `code_disassemble`, `asm_list_patches` |
| Modules and symbols | `modules{?offset,limit}`, `modules/{module}`, `symbols{?offset,limit}` | `module_list`, `module_get`, `symbol_list_registered` |
| Records and structures | `records{?offset,limit}`, `records/{recordId}`, `structures{?offset,limit}`, `structures/{structure}{?offset,limit}` | `record_list`, `record_get`, `structure_list`, `structure_get` |
| Pointer and speed state | `pointer-maps`, `pointer-scans`, `pointer-scans/{scanName}/paths{?offset,limit}`, `speedhack` | `pointer_list_maps`, `pointer_list_scans`, `pointer_list_paths`, `speedhack_get_state` |

Live resource responses are private and carry no client cache lifetime.
They are annotated for the assistant and carry their source tool name in `cheatengine/sourceTool` metadata.
Prepared reads also publish their method in `cheatengine/preparedProjection`; this is reviewed provenance, not proof of
the method's implementation. Explicit preparations can block CE for seconds during native enumeration.
Their URI variables are parsed before dispatch, so absent, malformed, or out-of-range values produce `invalid_argument` rather than reaching Cheat Engine.

Module and region navigation reads prepared data held by the activation for five seconds.
Call `module_list` without `processId` before browsing modules or completing a module name; call `memory_list_regions` before browsing regions.
For module details, call `module_get` with the same selector first: the resource reuses that exact successful result, also available by its canonical module name.
Only the most recently prepared detail is retained; refreshing module/map data invalidates earlier details.
Every navigation request checks the current target and snapshot expiry through dispatch admission.
A missing, expired or mismatched snapshot produces `invalid_state` with a preparation hint; completion offers no candidates.
The explicit source tools perform native enumeration. These prepared resource reads and module completion never trigger it as a side effect of browsing.

Thread, registered-symbol and breakpoint resources also require preparation within five seconds through
`process_list_threads`, `symbol_list_registered` and `debugger_list_breakpoints`, respectively.
Their native producers construct whole lists before copy limits apply; browsing reads the retained bounded copy.
Thread snapshots must still match the selected process and epoch. Symbol snapshots describe global CE state and
remain available without a selected process; ownership is re-evaluated from this activation's current registrations.
Structure details use a bounded global-name lookup and read only the requested element page.

Only cheap, bounded path variables are completed.
Module names use a fresh admitted snapshot check for each request, with concurrent requests sharing an in-flight check and failures throttled briefly.
Structure names use cached dispatch completion, while scanner and pointer-scan names use instance memory completion.
Addresses, record IDs, and query values deliberately do not complete.

## Project layout

| Path | Responsibility |
| --- | --- |
| `CheatEngineResourcesBuilderExtensions.cs` | Registers the static and live resource containers. |
| `Knowledge/CheatEngineKnowledge.cs` | Loads embedded documentation and workflow bodies. |
| `Knowledge/Documents/` | Source Markdown for the served reference documentation. |
| `Knowledge/Workflows/` | Source Markdown rendered by the prompts project and served as workflow resources. |
| `Docs/CheatEngineDocResources.cs` | Declares local document resources and the workflow-body template. |
| `Live/` | Declares resource templates, validates URI arguments, and calls the associated read-only tools. |

## Changing this project

### Add or revise documentation

Add the Markdown file under the appropriate `Knowledge/` folder.
For a document, add its slug to `CheatEngineKnowledge.DocumentSlugs` and declare the matching local resource in `CheatEngineDocResources`.
For a workflow body, keep its kebab-case name aligned with the workflow definition in [CheatEngine.Mcp.Prompts](../CheatEngine.Mcp.Prompts/README.md).

Keep the documentation contract current when a public tool, resource, prompt, parameter, allowed value, or capability gate changes.
That includes `Documents/tool-map.md`, `Documents/workflows.md`, affected workflows, and any reviewed contract snapshots whose resource sizes change.

### Add a live resource

Add the resource to the domain-appropriate class in `Live/`, or create a focused resource container when no existing domain fits.
Declare the resource URI, source tool, assistant annotation, JSON serialization context, bounds, and completion behavior explicitly.
Register the container from `AddResources()`.

Choose a source tool that satisfies the live-resource rules and preserve the source tool's result schema.
If the resource needs pagination or a path variable, validate it with `McpResourceQuery` before invoking the source tool.

## Verification

The resource tests live in [`tests/CheatEngine.Mcp.Tests/Resources`](../../tests/CheatEngine.Mcp.Tests/Resources).
`KnowledgeResourceTests` verifies the embedded file set, resource metadata, relative links, and size budgets.
`KnowledgeLintTests` checks that documentation and workflow guidance cite only served contract elements.
`LiveResourceTests` verifies the source-tool projections and URI validation, while `LiveCompletionTests` covers the permitted completion variables.

Run the portable suite from the repository root:

```powershell
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

For the complete contribution and golden-file workflow, see [CONTRIBUTING.md](../../CONTRIBUTING.md).
