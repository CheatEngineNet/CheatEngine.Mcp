using CheatEngine.Mcp.Core.Features;

namespace CheatEngine.Mcp.Core.Tables;

/// <summary>What <see cref="CheatTableInspector" /> found in a table file before it is loaded.</summary>
/// <remarks>For an opaque table every "contains" answer is <see langword="true" />: it may hold anything.</remarks>
public sealed class CheatTableInspection
{
	internal CheatTableInspection(CheatTableFormat format, bool containsLua, bool containsForms, bool usesMono,
		int recordCount, int assemblerScriptCount, McpFeatureRequirements requirements)
	{
		Format = format;
		ContainsLua = containsLua;
		ContainsForms = containsForms;
		UsesMono = usesMono;
		RecordCount = recordCount;
		AssemblerScriptCount = assemblerScriptCount;
		Requirements = requirements;
	}

	/// <summary>How the file could be read.</summary>
	public CheatTableFormat Format
	{
		get;
	}

	/// <summary>Whether the content was inspected completely, which only a plain XML table allows.</summary>
	public bool IsInspected => Format is CheatTableFormat.Xml;

	/// <summary>Whether the table has a Lua script (<c>LuaScript</c>), or could not be inspected.</summary>
	public bool ContainsLua
	{
		get;
	}

	/// <summary>Whether the table has forms (<c>Forms</c>), whose events call Lua functions, or could not be inspected.</summary>
	public bool ContainsForms
	{
		get;
	}

	/// <summary>
	///     Whether the table sets the <c>UsesMono</c> option, after which Cheat Engine injects its Mono data collector into
	///     an open process, or could not be inspected.
	/// </summary>
	public bool UsesMono
	{
		get;
	}

	/// <summary>The number of address-list records (<c>CheatEntry</c>) found; zero when the table was not inspected.</summary>
	public int RecordCount
	{
		get;
	}

	/// <summary>The number of Auto Assembler scripts (<c>AssemblerScript</c>) found; zero when the table was not inspected.</summary>
	public int AssemblerScriptCount
	{
		get;
	}

	/// <summary>The exposure switches the load needs; enforce them before the Client loads the table.</summary>
	public McpFeatureRequirements Requirements
	{
		get;
	}
}
