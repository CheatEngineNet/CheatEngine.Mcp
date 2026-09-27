# CheatEngine.Mcp.Resources

MCP resources, declared through `AddResources()` with `AddResourceType<T>()`.

- `Docs/CheatEngineDocResources`: the knowledge documents, one static (Local) resource each at
  `cheatengine://docs/{slug}` (`text/markdown`, title, description and size), plus the workflow bodies as the
  `cheatengine://docs/workflows/{workflow}` template, whose `[AllowedValues]` drive completion. Every backend and the
  gateway serve them without any instance; reads carry a one-hour public cache hint.
- `Knowledge/CheatEngineKnowledge`: the embedded files, loaded once with LF line endings and their relative links
  rewritten to `cheatengine://docs/...`.

The text is not written here: the project file embeds `skills/cheatengine-mcp/references/*.md` (an explicit list of 18
documents) and `references/workflows/*.md` by MSBuild link, with logical names `CheatEngine.Mcp.Knowledge/docs/...`
and `.../workflows/...` and `WithCulture=false`. Never `local-*.md` or the legacy reference files. Tests keep the
embedded set equal to the list, every link resolvable and each document within 16 KiB (200 KiB in total).

The live `cheatengine://instance/...` projections of read-only tools join here once those tools exist.

**Depends on:** Core only.
