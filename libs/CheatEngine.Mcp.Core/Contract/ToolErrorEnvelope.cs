namespace CheatEngine.Mcp.Core.Contract;

/// <summary>The wire form of a tool failure: <c>{"error":{...}}</c>.</summary>
/// <param name="Error">The failure.</param>
public sealed record ToolErrorEnvelope(ToolError Error);
