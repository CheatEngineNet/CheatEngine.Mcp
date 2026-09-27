namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Selects where MCP primitive targets come from when the manifest is registered in an MCP server.</summary>
public sealed class McpPrimitiveBinding
{
	private McpPrimitiveBinding(McpPrimitiveTargets? targets)
	{
		Targets = targets;
	}

	/// <summary>Schema only: instance methods get targets that refuse invocation, so no primitive type is constructed.</summary>
	public static McpPrimitiveBinding Catalog
	{
		get;
	} = new(null);

	internal McpPrimitiveTargets? Targets
	{
		get;
	}

	/// <summary>
	///     Live: every call borrows the activation-owned instance. The MCP host never registers, constructs or disposes
	///     primitive types; the activation scope does, after Cheat Engine drained the activation's leases.
	/// </summary>
	/// <param name="targets">The activation's resolved primitive instances.</param>
	/// <returns>The binding.</returns>
	public static McpPrimitiveBinding FromTargets(McpPrimitiveTargets targets)
	{
		ArgumentNullException.ThrowIfNull(targets);
		return new McpPrimitiveBinding(targets);
	}
}
