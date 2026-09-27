# CheatEngine.Mcp.Prompts

MCP prompts, declared through `AddPrompts()` with `AddPromptType<T>()`, and the `initialize` server instructions:
`McpServerInstructions` holds the gateway and backend variants, built from `CheatEngineToolNames`, and `AddPrompts()`
records them in the Core manifest. No prompt is shipped yet; the gateway must be able to route a prompt before one is
added.

**Depends on:** Core only.
