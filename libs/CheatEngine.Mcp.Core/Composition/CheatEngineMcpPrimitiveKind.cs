namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The kind of MCP primitive a declared type contributes.</summary>
public enum CheatEngineMcpPrimitiveKind
{
	/// <summary>Methods marked with <c>McpServerToolAttribute</c>.</summary>
	Tool,

	/// <summary>Methods marked with <c>McpServerResourceAttribute</c>.</summary>
	Resource,

	/// <summary>Methods marked with <c>McpServerPromptAttribute</c>.</summary>
	Prompt
}
