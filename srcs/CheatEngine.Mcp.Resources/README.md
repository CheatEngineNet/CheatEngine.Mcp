# CheatEngine.Mcp.Resources

MCP resources, declared through `AddResources()` with `AddResourceType<T>()`.

- `Docs/CheatEngineDocResources`: the knowledge documents, one static (Local) resource each at
  `cheatengine://docs/{slug}` (`text/markdown`, title, description and size), plus the workflow bodies as the
  `cheatengine://docs/workflows/{workflow}` template, whose `[AllowedValues]` drive completion. Every backend and the
  gateway serve them without any instance; reads carry a one-hour public cache hint.
- `Knowledge/CheatEngineKnowledge`: the embedded files, loaded once with LF line endings and their relative links
  rewritten to `cheatengine://docs/...`.
- `Live/*LiveResources`: private, uncached snapshots of the selected instance at `cheatengine://instance/runtime`,
  `/process`, `/modules`, `/memory/regions`, `/records` and `/structures`.
  Each response is the JSON form of the corresponding read-only v2 tool result with its default arguments:
  `runtime_get_overview`, `process_get_current`, `module_list`, `memory_list_regions`, `record_list` or
  `structure_list`.
  The gateway publishes the same resources under `cheatengine://instances/{instanceId}/...` and verifies the selected
  instance before reading.

The text is not written here: the project file embeds `skills/cheatengine-mcp/references/*.md` (an explicit list of 18
documents) and `references/workflows/*.md` by MSBuild link, with logical names `CheatEngine.Mcp.Knowledge/docs/...`
and `.../workflows/...` and `WithCulture=false`. Never `local-*.md` or the legacy reference files. Tests keep the
embedded set equal to the list, every link resolvable and each document within 24 KiB (200 KiB in total).

**Depends on:** Core and Tools.
