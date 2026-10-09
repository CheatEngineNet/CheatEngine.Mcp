using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>The structured MCP outcome for a call expected to succeed or fail during a live qualification assertion.</summary>
internal sealed record LiveMcpToolResult(bool IsError, JsonNode? Payload, string? ErrorText);
