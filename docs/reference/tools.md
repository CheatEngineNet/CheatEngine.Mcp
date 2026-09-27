# Tool reference

CheatEngine.Mcp exposes the frozen v2 contract.
Read this page together with the live MCP schema: the schema returned by `tools/list` is the authoritative list of tools, arguments, output schemas, annotations, and currently available names.

## Which catalogue applies

`CheatEngineToolNames` defines the v2 inventory of 173 names: the gateway-local `instance_list` and 172 backend names across 21 domains.
Those names are deliberately fixed as the public contract.

The effective catalogue is assembled by `AddTools()` and the gateway at startup.
It contains every v2 registration and no legacy aliases.
The live schema remains the source for exact parameter and result shapes of the installed build.

| Source | What it establishes |
|---|---|
| `tools/list` from the connected gateway | The effective tools, parameters, output schemas, annotations, and descriptions for this build. |
| `libs/CheatEngine.Mcp.Core/Contract/CheatEngineToolNames.cs` | The frozen v2 names and their domain order. |
| [`../../skills/cheatengine-mcp/references/tool-catalog.md`](../../skills/cheatengine-mcp/references/tool-catalog.md) | The full v2 inventory, purpose of each name, and the labelled historic-to-v2 migration table. |
| Tests under `tests/CheatEngine.Mcp.Tests/Contract` | The reviewed protocol snapshots for a specific build. |

## Complete 173-name table

The [complete v2 tool table](../../skills/cheatengine-mcp/references/tool-catalog.md) is the exhaustive reference for all 173 names.
It has one row for each gateway or backend tool, grouped by domain, with its gate and purpose.
The historic-name mapping appears in its separate **Historic name migration** section and does not describe registered tools.
This page covers routing, resources, configuration, and how to use the running schema without duplicating the 173-row table.

## Calling tools through the gateway

Call `instance_list` first.
It is the only gateway-local tool and returns the current, verified Cheat Engine instances.
Every other gateway tool requires the exact `instanceId` from that result.
There is no selected default instance, fallback to another instance, or safe replay of a mutation after a lost response.

The usual session sequence is:

1. Call `instance_list` and choose the intended instance by its CE process and label.
2. Inspect the live schema for the available orientation and process tools.
3. Attach explicitly to the authorized target.
4. Use the live schema for exact input formats, bounds, and result fields.
5. Read state before retrying a failure whose `hostEffect` is `started`, `unknown`, or `cleanup_unconfirmed`.
6. Release session-owned resources before changing targets or finishing the session.

Backend tools are routed through the named instance after the gateway verifies its identity.
The gateway adds `instanceId` to every backend schema; the backend itself does not receive that routing argument.

## v2 domains

The frozen names are grouped in this order.
The group indicates the contract area; use the live schema for its exact arguments, result shape, annotations, and descriptions.

| Domain | Intended scope |
|---|---|
| `runtime` | Instance information, jobs, and resources. |
| `process` | Process discovery, explicit attachment, files opened as processes, pause state, and threads. |
| `memory` | Reads, writes, regions, allocations, protection, and file transfer. |
| `scan` | Value scans and scanner sessions. |
| `aob` | Array-of-bytes search and signature generation. |
| `pointer` | Pointer chains, maps, scans, and references. |
| `module` | Loaded modules, exports, and patch comparison. |
| `symbol` | Resolution, registration, reload, modules, and configured symbol sources. |
| `speedhack` | Speedhack state and configuration. |
| `util` | Bounded conversion and helper operations. |
| `code` | Disassembly, comments, references, strings, and code analysis. |
| `asm` | Assembly, Auto Assembler checks, and reversible patches. |
| `record` | Address-list records, activation, scripts, grouping, and clearing. |
| `table` | Cheat-table inspection, loading, saving, and file roots. |
| `structure` | Structure definitions, fields, values, and comparisons. |
| `debugger` | Debugger attachment, breakpoints, contexts, captures, and traces. |
| `exec` | Explicit target-code execution and injection. |
| `dotnet` | Managed runtime and metadata inspection. |
| `mono` | Mono and Unity metadata and invocation. |
| `kernel` | DBK, DBVM, physical memory, and kernel controls. |
| `lua` | Caller-authored Lua execution under its capability gate. |

## Prompts and resources

Prompts and resources are independent MCP surfaces rather than tools.
The gateway currently composes 22 static workflow prompts, 18 static knowledge documents at `cheatengine://docs/{slug}`, one workflow-body template at `cheatengine://docs/workflows/{workflow}`, and the local `cheatengine://instances` resource.
These static entries work even when no Cheat Engine instance is running.

The instance-scoped resources are routed as `cheatengine://instances/{instanceId}/runtime`, `cheatengine://instances/{instanceId}/process`, `cheatengine://instances/{instanceId}/modules`, `cheatengine://instances/{instanceId}/memory/regions`, `cheatengine://instances/{instanceId}/records` and `cheatengine://instances/{instanceId}/structures`.
Each contains the JSON structured result of its corresponding read-only tool with default arguments: `runtime_get_overview`, `process_get_current`, `module_list`, `memory_list_regions`, `record_list` or `structure_list`.
Use that tool when filtering, paging, or requesting a non-default view.
The gateway applies the same instance identity check as it does for a tool call.
Inspect `resources/list` and `resources/templates/list` for the effective resource surface.

Some MCP clients show only tools.
The same guidance ships in [`../../skills/cheatengine-mcp/references/`](../../skills/cheatengine-mcp/references/), and the relevant state remains available through read-only tools where they are registered.

## Annotations, gates, and errors

The v2 contract uses MCP annotations to communicate read-only, destructive, idempotent, and open-world effects.
Treat annotations as planning information, not as a security boundary.
Capability gates and file policies can refuse an otherwise listed tool; a disabled capability is reported as `capability_disabled` before the operation starts.

Errors use `isError: true` and carry an error envelope with `kind`, `hostEffect`, `retryable`, and a recovery hint.
Do not repeat a mutation after a timeout or an unknown host effect.
Read state from the same instance first and use the explicit release, stop, delete, or restore operation when one exists.

For target authorization, state ownership, cleanup, and kernel restrictions, read the operator skill's [safety guide](../../skills/cheatengine-mcp/references/safety.md).
