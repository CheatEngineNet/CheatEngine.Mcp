# CheatEngine.Mcp.Tools

The Cheat Engine MCP tools: public `[McpServerToolType]` classes with DI constructors, their helpers, and the single
entry point `AddTools()`. The activation scopes each tool; the gateway only reads their schemas.

v2 tools live in one folder per domain (`Runtime/`, `Processes/`, `Memory/`, ... `Modules/`, `Structures/`, ... `Lua/`),
each with an `Add<Domain>Tools()` entry point that `AddTools()` calls in catalog order.
Tool names come from `CheatEngineToolNames` in Core, the frozen v2 catalog.

**Depends on:** Core. Never hosting, configuration, NLog or raw SDK Lua state.

**Belongs here when** it is a tool, or state or logic that only tools use. Tool names, schemas and result shapes are a
public contract checked by the golden snapshots in `tests/CheatEngine.Mcp.Tests/Contract`.
