# CheatEngine.Mcp.Resources

The knowledge base and the MCP resources, declared through `AddResources()` with `AddResourceType<T>()`.

- `Knowledge/Documents/*.md` and `Knowledge/Workflows/*.md`: the knowledge base itself, the documents and the guided
  workflow bodies (one per prompt of the Prompts project). They are embedded here once, with logical names
  `CheatEngine.Mcp.Knowledge/docs/...` and `CheatEngine.Mcp.Knowledge/workflows/...` and `WithCulture=false`.
- `Knowledge/CheatEngineKnowledge`: the embedded files, loaded once with LF line endings and their relative links
  rewritten to `cheatengine://docs/...`; `DocumentSlugs` lists the served documents in reading order. The Prompts
  project reads the workflow bodies from here.
- `Docs/CheatEngineDocResources`: the knowledge documents, one static (Local) resource each at
  `cheatengine://docs/{slug}` (`text/markdown`, title, description and size), plus the workflow bodies as the
  `cheatengine://docs/workflows/{workflow}` template, whose `[AllowedValues]` drive completion. Every backend and the
  gateway serve them without any instance; reads carry a one-hour public cache hint.
- `Live/*LiveResources`: private, uncached (`ttlMs` 0), read-only projections of the instance a backend serves, under
  `cheatengine://instance/...`. Each returns the structured result of exactly one v2 tool, named by its
  `[McpSourceTool]` and published in its `_meta` as `cheatengine/sourceTool`; Core's validator requires that tool to
  be read-only, closed-world, ungated and `short`, and every read re-checks its gates. Every live resource is
  annotated for the `assistant` with priority 0.3 (`[McpResourceAnnotations]`).

  | URI after `cheatengine://instance/` | Tool | Defaults (bounds) |
  |---|---|---|
  | `runtime`, `process`, `threads` | `runtime_get_overview`, `process_get_current`, `process_list_threads` | |
  | `resources`, `jobs`, `patches` | `runtime_list_resources`, `runtime_list_jobs`, `asm_list_patches` | |
  | `scanners`, `scanners/{scannerName}` | `scan_list_scanners`, `scan_get_status` | |
  | `debugger` | `debugger_get_status` | |
  | `debugger/breakpoints{?limit}` | `debugger_list_breakpoints` | limit 256 (1-1024) |
  | `speedhack` | `speedhack_get_state` | |
  | `modules{?offset,limit}`, `modules/{module}` | `module_list`, `module_get` | limit 200 (1-1000) |
  | `modules/{module}/exports{?offset,limit}` | `module_list_exports` | limit 200 (1-1000) |
  | `regions{?offset,limit}` | `memory_list_regions` (committed) | limit **100** (1-2000); the tool's is 500 |
  | `memory/{address}{?size}` | `memory_read` (valueType bytes) | size 256 (1-16384) |
  | `disassembly/{address}{?count}` | `code_disassemble` | count 20 (1-1024) |
  | `records{?offset,limit}`, `records/{recordId}` | `record_list`, `record_get` (one id) | limit 100 (1-1000) |
  | `structures{?offset,limit}` | `structure_list` | limit 100 (1-1000) |
  | `structures/{structure}{?offset,limit}` | `structure_get` (concise) | limit 256 (1-1024) |
  | `symbols{?offset,limit}` | `symbol_list_registered` | limit 200 (1-1000) |
  | `pointer-maps`, `pointer-scans` | `pointer_list_maps`, `pointer_list_scans` | |
  | `pointer-scans/{scanName}/paths{?offset,limit}` | `pointer_list_paths` (sorted by depth) | limit 100 (1-500) |

  Offsets default to 0. A bare list URI reads the default page. The SDK matcher enforces the query order of the
  template and refuses unknown keys (not found); path values are percent-decoded. `McpResourceQuery` (Core) parses the
  variables: an empty, malformed or out-of-range value is `invalid_argument` (JSON-RPC `-32602`), checked before any
  dispatch. `memory/{address}` matches every `memory/...` path, so no other resource may add a literal segment there.
  The gateway publishes the same templates under `cheatengine://instances/{instanceId}/...`, forwards the URI verbatim
  and verifies the selected instance before reading; its resource list also names the concrete ones of every instance
  it confirmed (see the [Hosting README](../CheatEngine.Mcp.Hosting/README.md)). `LiveResourceTests` prove that each
  projection returns its tool's result for the same arguments.

  Completion: `{module}` (`modules/{module}` and its exports) completes from the attached process's module names,
  `{structure}` from the first 1000 global structure names, `{scannerName}` from `main` and the named scanners this
  activation retains, and `{scanName}` from this activation's pointer scans. Each container implements
  `IMcpCompletionSource` and marks the parameter with `[McpCompletion]`: `Memory` for the scanner and pointer-scan
  names, read on every request, and `Dispatch` for the module and structure names, which Core's handler reads with one
  Cheat Engine dispatch, caches for 5 s and waits for at most 750 ms. Record ids, addresses and query values are not
  completed. The gateway forwards these variables to the instance named by `context.arguments.instanceId`.
  `LiveCompletionTests` cover the sources and the markers.

A document links another document as `slug.md` and a workflow as `../Workflows/name.md`; a workflow links a document as
`../Documents/slug.md`. The links read correctly on GitHub and are served as `cheatengine://docs/...` URIs.
`KnowledgeResourceTests` keep the embedded documents equal to `DocumentSlugs`, every link resolvable and every document
within its size budgets; `ToolMapDocumentTests` keep `tool-map.md` in step with the tool catalog; `KnowledgeLintTests`
keep the cited tools, parameters, values and URIs served and the workflow bodies in step with their prompts. Each
document's size is also recorded in the `*-resources.json` goldens.

**Depends on:** Core and Tools.
