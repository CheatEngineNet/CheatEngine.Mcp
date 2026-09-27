# CheatEngine.Mcp.Tools

The Cheat Engine MCP tools: public `[McpServerToolType]` classes with DI constructors, their helpers, and the single
entry point `AddTools()`. The activation scopes each tool; the gateway only reads their schemas.

**Depends on:** Core. Never hosting, configuration, NLog or raw SDK Lua state.

**Belongs here when** it is a tool, or state or logic that only tools use. Tool names, schemas and result shapes are a
public contract checked by the golden snapshots in `tests/CheatEngine.Mcp.Tests/Contract`.
