# CheatEngine.Mcp.Prompts

MCP prompts, declared through `AddPrompts()` with `AddPromptType<T>()`, and the `initialize` server instructions.

- `CheatEngineWorkflowPrompts`: the 22 guided workflows, one static (Local) prompt each, without `instanceId`. A prompt
  renders one user message (the workflow title, a fixed preamble that routes through `instance_list`, the checked
  inputs and the workflow body) and one resource link per related `cheatengine://docs/...` document, enriched from the
  serving host's own resource listing. Enumerated arguments use `[AllowedValues]`, which drive completion; invalid
  arguments are JSON-RPC `-32602` errors with the contract data.
- `Workflows/`: the definitions (prompt, body, linked documents) and the renderer. The bodies are embedded by MSBuild
  link
  from `skills/cheatengine-mcp/references/workflows/*.md`, the same files the Resources project serves.
- `McpServerInstructions`: the gateway and backend variants, built from `CheatEngineToolNames`; `AddPrompts()` records
  them in the Core manifest.

**Depends on:** Core only.
