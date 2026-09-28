namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Selects what a CheatEngine MCP composition registers.</summary>
public enum CheatEngineMcpMode
{
	/// <summary>A live backend inside Cheat Engine: primitive targets are real instances.</summary>
	Backend,

	/// <summary>A schema-only catalog, such as the gateway's: no primitive type is ever constructed.</summary>
	Catalog
}
