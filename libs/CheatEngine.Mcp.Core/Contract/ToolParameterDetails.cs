namespace CheatEngine.Mcp.Core.Contract;

/// <summary>The <c>details</c> of an argument failure: which tool parameter was rejected.</summary>
/// <param name="Parameter">The parameter name as it appears in the tool's input schema.</param>
internal sealed record ToolParameterDetails(string Parameter);
